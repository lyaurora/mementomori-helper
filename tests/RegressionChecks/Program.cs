using System.Collections.Concurrent;
using System.Reflection;
using MementoMori;
using MementoMori.BlazorShared.Models;
using MementoMori.Funcs;
using MementoMori.Jobs;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Quartz;
using Quartz.Impl;

var checks = new (string Name, Func<Task> Run)[]
{
    ("account switching during initialization", CheckAccountSwitch),
    ("logout invalidates queued jobs and waits for active work", CheckLogout),
    ("configuration permissions and contents", CheckPermissions)
};
foreach (var (name, run) in checks)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception e)
    {
        Environment.ExitCode = 1;
        Console.Error.WriteLine($"FAIL: {name}: {e}");
    }
}

static async Task CheckAccountSwitch()
{
    var manager = Construct<AccountManager>(new Writable<AuthOption>(new()), new Writable<GameConfig>(new()), NullLogger<AccountManager>.Instance);
    var accounts = (ConcurrentDictionary<long, Account>)Field(manager, "_accounts").GetValue(manager)!;
    accounts[1] = new Account {AccountInfo = new AccountInfo {UserId = 1}};
    accounts[2] = new Account {AccountInfo = new AccountInfo {UserId = 2}};
    using var services = new ServiceCollection().BuildServiceProvider();
    using var renderer = new CheckRenderer(services);

    foreach (var switchDuringLoad in new[] {true, false})
    {
        manager.CurrentUserId = 1;
        using var component = new AccountProbe {AccountManager = manager, Logger = NullLogger<AccountComponent>.Instance};
        await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(component));
        var initializing = renderer.Dispatcher.InvokeAsync(component.Initialize);
        await component.Started.Task;
        if (switchDuringLoad) manager.CurrentUserId = 2;
        component.Release.SetResult();
        await initializing;
        if (switchDuringLoad) await component.Switched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        else await Task.Delay(250);
        Require(component.BoundUserId == manager.CurrentUserId, "Component is bound to the previous account");
        Require(component.Changes == (switchDuringLoad ? 2 : 1), "Initialization repeats an unchanged account");
        component.Dispose();
        var changes = component.Changes;
        manager.CurrentUserId = switchDuringLoad ? 1 : 2;
        await Task.Delay(250);
        Require(component.Changes == changes, "Disposed component still receives account changes");
    }

    manager.CurrentUserId = 1;
    using var disposed = new AccountProbe {AccountManager = manager, Logger = NullLogger<AccountComponent>.Instance};
    await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(disposed));
    var pending = renderer.Dispatcher.InvokeAsync(disposed.Initialize);
    await disposed.Started.Task;
    disposed.Dispose();
    manager.CurrentUserId = 2;
    disposed.Release.SetResult();
    await pending;
    await Task.Delay(250);
    Require(disposed.Changes == 1, "Initialization subscribed after disposal");
}

static async Task CheckLogout()
{
    var auth = new Writable<AuthOption>(new());
    var config = new Writable<GameConfig>(new());
    var manager = Construct<AccountManager>(auth, config, NullLogger<AccountManager>.Instance);
    var factory = new StdSchedulerFactory();
    var scheduler = await factory.GetScheduler();
    var register = Construct<TimeZoneAwareJobRegister>(manager, config, factory, NullLogger<TimeZoneAwareJobRegister>.Instance);
    using var funcs = Construct<MementoMoriFuncs>(manager, auth, config, register, NullLogger<MementoMoriFuncs>.Instance);
    funcs.UserId = 1;
    funcs.LoginOk = true;
    Field(funcs, "_lastPlayerDataInfo").SetValue(funcs, new PlayerDataInfo {WorldId = 1001});
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var activeToken = CancellationToken.None;
    var job = JobBuilder.Create<DailyJob>().WithIdentity($"1-{typeof(DailyJob).FullName}").StoreDurably().Build();
    try
    {
        var active = funcs.ExecuteQuickAction(async (_, token) =>
        {
            activeToken = token;
            await release.Task;
            // Simulate a login already registering jobs when logout is requested.
            funcs.LoginOk = true;
            await scheduler.AddJob(job, true);
        });
        var ran = false;
        var queued = funcs.ExecuteScheduledJob(() => { ran = true; return Task.CompletedTask; }, CancellationToken.None);
        Require(!queued.IsCompleted, "Scheduled job did not wait for the active action");
        var logout = funcs.Logout();
        var cancelled = activeToken.IsCancellationRequested;
        var waited = !logout.IsCompleted;
        release.TrySetResult();
        await Task.WhenAll(active, queued, logout);
        Require(auth.Updates == 0 && !ran, "A queued job attempted login after logout");
        Require(cancelled && waited, "Logout did not cancel and wait for the active action");
        Require(!funcs.LoginOk && !await scheduler.CheckExists(job.Key), "Active login revived the logged-out account");
        await funcs.ExecuteScheduledJob(() => Task.CompletedTask, CancellationToken.None);
        Require(auth.Updates == 0, "A new job attempted login while logged out");

        // Fail before any network access; an explicit login must still be allowed.
        auth.RejectUpdates = true;
        await funcs.Login(new PlayerDataInfo {WorldId = 1001});
        Require(auth.Updates == 1, "Explicit login was blocked after logout");

        using var retry = Construct<MementoMoriFuncs>(manager, auth, config, register, NullLogger<MementoMoriFuncs>.Instance);
        Field(retry, "_lastPlayerDataInfo").SetValue(retry, new PlayerDataInfo {WorldId = 1001});
        await retry.ExecuteScheduledJob(() => Task.CompletedTask, CancellationToken.None);
        await retry.ExecuteScheduledJob(() => Task.CompletedTask, CancellationToken.None);
        Require(auth.Updates == 3, "A failed login disabled later scheduled retries");

        var nestedRan = false;
        await retry.ExecuteQuickAction(async (_, _) =>
        {
            retry.CancelQuickAction();
            try
            {
                await retry.ExecuteQuickAction((_, _) => { nestedRan = true; return Task.CompletedTask; });
            }
            catch (OperationCanceledException) { }
        });
        Require(!nestedRan, "A cancelled operation started another nested action");
    }
    finally
    {
        release.TrySetResult();
        await scheduler.Shutdown();
    }
}

static Task CheckPermissions()
{
    var directory = Directory.CreateTempSubdirectory("mementomori-regression-");
    try
    {
        using var provider = new PhysicalFileProvider(directory.FullName);
        using var configuration = new ConfigurationRoot([]);
        var path = Path.Combine(directory.FullName, "settings.json");
        var options = new WritableOptions<AuthOption>(provider, new Monitor<AuthOption>(new()), configuration, "AuthOption", "settings.json");
        foreach (var mode in new[] {0x180, 0x1A0, 0x1B0}) // 0600, 0640, 0660
        {
            File.WriteAllText(path, """{"AuthOption":{"AppVersion":"old"},"Other":{"keep":true}}""");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)mode);
            options.Update(value => value.AppVersion = "updated");
            if (!OperatingSystem.IsWindows()) Require(File.GetUnixFileMode(path) == (UnixFileMode)mode, "Saving changed the file permissions");
            var json = JObject.Parse(File.ReadAllText(path));
            Require((string?)json["AuthOption"]?["AppVersion"] == "updated" && (bool?)json["Other"]?["keep"] == true,
                "Saving lost configuration data");
        }
        File.Delete(path);
        options.Update(value => value.AppVersion = "new");
        if (!OperatingSystem.IsWindows())
            Require(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "New configuration is not private");
        Require(!Directory.EnumerateFiles(directory.FullName, "*.tmp").Any(), "Temporary configuration files were left behind");
    }
    finally
    {
        directory.Delete(true);
    }
    return Task.CompletedTask;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static T Construct<T>(params object[] services)
{
    var ctor = typeof(T).GetConstructors().Single();
    return (T)ctor.Invoke(ctor.GetParameters().Select(parameter => services.FirstOrDefault(parameter.ParameterType.IsInstanceOfType)).ToArray());
}

static FieldInfo Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

sealed class Monitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

sealed class Writable<T>(T value) : IWritableOptions<T> where T : class, new()
{
    public T Value => value;
    public int Updates { get; private set; }
    public bool RejectUpdates { get; set; }
    public void Update(Action<T> change)
    {
        Updates++;
        if (RejectUpdates) throw new InvalidOperationException("Expected offline login failure");
        change(value);
    }
}

sealed class AccountProbe : AccountComponent
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Switched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long BoundUserId => AccountInfo.UserId;
    public int Changes { get; private set; }
    public Task Initialize() => base.OnInitializedAsync();
    protected override Task AccountChanged()
    {
        Changes++;
        Started.TrySetResult();
        if (BoundUserId == 2) Switched.TrySetResult();
        return Release.Task;
    }
}

#pragma warning disable BL0006
sealed class CheckRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public void Attach(IComponent component) => AssignRootComponentId(component);
    protected override void HandleException(Exception exception) => throw exception;
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
}
