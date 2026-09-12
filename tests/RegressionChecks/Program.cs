using System.Security.Cryptography;
using MementoMori.Ortega.Share.Master;
using System.Net;
using MementoMori.Exceptions;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface;
using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.Battle;
using MementoMori.Ortega.Share.Data.Chat;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Enums.Battle.Skill;
using MementoMori.Ortega.Share.Master.Data;
using MementoMori.Ortega.Share.Master.Table;
using MessagePack;
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
    ("configuration permissions and contents", CheckPermissions),
    ("login preferences, retry backoff and deletion", CheckAccountLifecycle),
    ("HTTP cancellation and bounded version negotiation", CheckTransport),
    ("4.22 wire data and master rollback", CheckProtocol),
    ("master download rejects corruption without replacing cached files", CheckMasterDownload)
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
    var auth = new Writable<AuthOption>(new() { Accounts = [new AccountInfo { UserId = 1 }, new AccountInfo { UserId = 2 }] });
    var manager = Construct<AccountManager>(auth, new Writable<GameConfig>(new()), NullLogger<AccountManager>.Instance);
    using var selection = new AccountSelection(manager);
    using var otherSelection = new AccountSelection(manager);
    selection.CurrentUserId = 2;
    Require(otherSelection.CurrentUserId == 1, "Account selection leaks across browser sessions");
    var accounts = (ConcurrentDictionary<long, Account>)Field(manager, "_accounts").GetValue(manager)!;
    accounts[1] = new Account {AccountInfo = new AccountInfo {UserId = 1}};
    accounts[2] = new Account {AccountInfo = new AccountInfo {UserId = 2}};
    using var services = new ServiceCollection().BuildServiceProvider();
    using var renderer = new CheckRenderer(services);

    foreach (var switchDuringLoad in new[] {true, false})
    {
        selection.CurrentUserId = 1;
        using var component = new AccountProbe {AccountManager = manager, AccountSelection = selection, Logger = NullLogger<AccountComponent>.Instance};
        await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(component));
        var initializing = renderer.Dispatcher.InvokeAsync(component.Initialize);
        await component.Started.Task;
        if (switchDuringLoad) selection.CurrentUserId = 2;
        component.Release.SetResult();
        await initializing;
        if (switchDuringLoad) await component.Switched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        else await Task.Delay(250);
        Require(component.BoundUserId == selection.CurrentUserId, "Component is bound to the previous account");
        Require(component.Changes == (switchDuringLoad ? 2 : 1), "Initialization repeats an unchanged account");
        component.Dispose();
        var changes = component.Changes;
        selection.CurrentUserId = switchDuringLoad ? 1 : 2;
        await Task.Delay(250);
        Require(component.Changes == changes, "Disposed component still receives account changes");
    }

    selection.CurrentUserId = 1;
    using var disposed = new AccountProbe {AccountManager = manager, AccountSelection = selection, Logger = NullLogger<AccountComponent>.Instance};
    await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(disposed));
    var pending = renderer.Dispatcher.InvokeAsync(disposed.Initialize);
    await disposed.Started.Task;
    disposed.Dispose();
    selection.CurrentUserId = 2;
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
        await funcs.Login(new PlayerDataInfo {WorldId = 1001}, true);
        Require(auth.Updates == 1, "Explicit login was blocked after logout");

        using var retry = Construct<MementoMoriFuncs>(manager, auth, config, register, NullLogger<MementoMoriFuncs>.Instance);
        Field(retry, "_lastPlayerDataInfo").SetValue(retry, new PlayerDataInfo {WorldId = 1001});
        await retry.ExecuteScheduledJob(() => Task.CompletedTask, CancellationToken.None);
        await retry.ExecuteScheduledJob(() => Task.CompletedTask, CancellationToken.None);
        Require(retry.MesssageList.Count == 2 && auth.Updates == 1, "Scheduled retries stopped or rewrote login preferences");

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

static async Task CheckAccountLifecycle()
{
    var auth = new Writable<AuthOption>(new() { Accounts = [new AccountInfo { UserId = 1, AutoLogin = true, AutoLoginWorldId = 1001 }] });
    var config = new Writable<GameConfig>(new());
    var manager = Construct<AccountManager>(auth, config, NullLogger<AccountManager>.Instance);
    var factory = new StdSchedulerFactory();
    var scheduler = await factory.GetScheduler();
    var register = Construct<TimeZoneAwareJobRegister>(manager, config, factory, NullLogger<TimeZoneAwareJobRegister>.Instance);
    using var funcs = Construct<MementoMoriFuncs>(manager, auth, config, register, NullLogger<MementoMoriFuncs>.Instance);
    funcs.UserId = 1;
    Field(funcs, "_lastPlayerDataInfo").SetValue(funcs, new PlayerDataInfo { WorldId = 1001 });
    var accounts = (ConcurrentDictionary<long, Account>)Field(manager, "_accounts").GetValue(manager)!;
    accounts[1] = new Account { AccountInfo = auth.Value.Accounts[0], Funcs = funcs };
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    try
    {
        // No network manager is supplied: login fails before any request leaves the process.
        await funcs.ExecuteScheduledJob(() => throw new Exception("Must not run after failed login"), default, jobName: "daily");
        Require(auth.Updates == 0 && auth.Value.Accounts[0].AutoLoginWorldId == 1001, "A scheduled login changed the selected world");
        Require(funcs.JobRuns["daily"] is { Error: not null, FinishedAt: not null }, "Failed login was reported as a successful job");
        await funcs.AutoLogin();
        var messages = funcs.MesssageList.Count;
        Require(funcs.NextAutoLoginAttempt > DateTimeOffset.UtcNow && funcs.LastLoginError != null, "Failed startup login has no retry state");
        await funcs.AutoLogin();
        Require(funcs.MesssageList.Count == messages, "Automatic login ignores backoff");
        await funcs.AutoLogin(true);
        Require(funcs.MesssageList.Count > messages, "Manual login is blocked by automatic retry backoff");
        await funcs.Login(new PlayerDataInfo { WorldId = 1001 }, false);
        Require(auth.Value.Accounts[0].AutoLoginWorldId == 0, "An explicit preference change was ignored");

        var activeToken = CancellationToken.None;
        var active = funcs.ExecuteQuickAction(async (_, token) => { activeToken = token; await release.Task; });
        var deleting = manager.RemoveAccount(1);
        Require(activeToken.IsCancellationRequested && !deleting.IsCompleted, "Deleting an account did not cancel and wait for its work");
        release.TrySetResult();
        await Task.WhenAll(active, deleting);
        Require(!manager.Contains(1) && auth.Value.Accounts.Count == 0, "Deleted account remains registered");
        var ran = false;
        await funcs.ExecuteScheduledJob(() => { ran = true; return Task.CompletedTask; }, default);
        Require(!ran, "A deleted account ran another scheduled action");
    }
    finally { release.TrySetResult(); await scheduler.Shutdown(); }
}

static async Task CheckTransport()
{
    var auth = new Writable<AuthOption>(new() { AuthUrl = "https://offline.invalid/api/", AppVersion = "4.19.2" });
    var config = new Writable<GameConfig>(new());
    using var network = Construct<MementoNetworkManager>(auth, config, NullLogger<MementoNetworkManager>.Instance);
    using var funcs = Construct<MementoMoriFuncs>(auth, config, NullLogger<MementoMoriFuncs>.Instance);
    funcs.NetworkManager = network;
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var cancelled = false;
    ReplaceClient(network, "_httpClient", new HttpClient(new Handler(async (_, token) =>
    {
        calls++;
        started.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        finally { cancelled = token.IsCancellationRequested; }
        return new HttpResponseMessage(HttpStatusCode.OK);
    })));
    var active = funcs.ExecuteQuickAction(async (_, _) => await funcs.GetResponse<GetDataUriRequest, GetDataUriResponse>(new()));
    try
    {
        await started.Task;
        using var waitingCancellation = new CancellationTokenSource();
        var waiting = network.GetResponse<GetDataUriRequest, GetDataUriResponse>(new(), cancellationToken: waitingCancellation.Token);
        waitingCancellation.Cancel();
        await Throws<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(1)));
        Require(calls == 1, "Cancelled queued HTTP request reached the server");
        funcs.CancelQuickAction();
        await active.WaitAsync(TimeSpan.FromSeconds(1));
        Require(cancelled, "The stop action did not cancel the active HTTP request");
    }
    finally { funcs.CancelQuickAction(); await active; }

    calls = 0;
    var versionRequests = 0;
    ReplaceClient(network, "_httpClient", new HttpClient(new Handler((_, _) =>
    {
        calls++;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(MessagePackSerializer.Serialize(new ApiErrorResponse { ErrorCode = ErrorCode.CommonRequireClientUpdate }))
        };
        response.Headers.Add("ortegastatuscode", "1");
        return Task.FromResult(response);
    })));
    ReplaceClient(network, "_unityHttpClient", new HttpClient(new Handler((_, _) =>
    {
        versionRequests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("var downloadApk = '/apps/mementomori_4.22.0.apk';") });
    })));
    await Throws<ApiErrorException>(() => network.GetResponse<GetDataUriRequest, GetDataUriResponse>(new()).WaitAsync(TimeSpan.FromSeconds(2)));
    Require(calls == 2 && versionRequests == 1 && auth.Value.AppVersion == "4.22.0", "Version negotiation retries indefinitely or fails to update the version");
}

static Task CheckProtocol()
{
    // Fixed wire payloads from the 4.22.0 contract, independent of the serializer's output.
    var chat = MessagePackSerializer.Deserialize<ChatInfo>(MessagePackSerializer.ConvertFromJson(
        """[0,0,"message",1,"player","",0,0,0,0,[],"guild",0,null,null,0,[2,"recruit guild",3,4,0,0,"join us"]]"""));
    Require(chat.ChatRecruitGuildMemberInfo.GuildId == 2 && chat.ChatRecruitGuildMemberInfo.RecruitMessage == "join us", "Chat recruitment key 16 is not decoded");
    var effect = MessagePackSerializer.Deserialize<Effect>(MessagePackSerializer.ConvertFromJson("""{"EffectType":6005,"EffectValue":20,"EffectSubValue":123}"""));
    Require(effect.EffectType == EffectType.Imprison && effect.DeepCopy().EffectSubValue == 123, "New battle effect data is lost");
    var boss = MessagePackSerializer.Deserialize<GuildRaidBossMB>(MessagePackSerializer.ConvertFromJson("""{"Id":1,"EventTutorialId":42}"""));
    var media = MessagePackSerializer.Deserialize<MediaMB>(MessagePackSerializer.ConvertFromJson("""{"Id":3,"MediaType":2,"DisplayLanguageTypeList":[1,2]}"""));
    Require(boss.EventTutorialId == 42 && media.Id == 3 && media.DisplayLanguageTypeList.Count == 2, "Master constructor does not preserve new fields");
    var table = new TextResourceTable();
    table.SetLanguageType(LanguageType.zhTW);
    table.Load(MessagePackSerializer.ConvertFromJson("""[{"StringKey":"known","Text":"before"}]"""));
    try { table.Load([0xc1]); throw new Exception("Invalid master data was accepted"); }
    catch (MessagePackSerializationException) { }
    Require(table.Get("known") == "before", "Failed text update erased the working cache");
    return Task.CompletedTask;
}

static async Task CheckMasterDownload()
{
    var directory = Directory.CreateTempSubdirectory("mementomori-master-check-");
    var previousDirectory = Directory.GetCurrentDirectory();
    try
    {
        Directory.SetCurrentDirectory(directory.FullName);
        Directory.CreateDirectory("Master");
        byte[] old = [0x90];
        await File.WriteAllBytesAsync("Master/TimeServerMB", old);
        var valid = MessagePackSerializer.ConvertFromJson("""[{"Id":1,"DifferenceDateTimeFromUtc":"09:00:00"}]""");
        var catalog = new MasterBookCatalog { MasterBookInfoMap = new()
        {
            ["TimeServerMB"] = new() { Hash = Convert.ToHexString(MD5.HashData(valid)) },
            ["ItemMB"] = new() { Hash = "00000000000000000000000000000000" }
        } };
        var auth = new Writable<AuthOption>(new() { AuthUrl = "https://offline.invalid/api/", AppVersion = "4.22.0" });
        using var network = Construct<MementoNetworkManager>(auth, new Writable<GameConfig>(new()), NullLogger<MementoNetworkManager>.Instance);
        ReplaceClient(network, "_httpClient", new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(MessagePackSerializer.Serialize(new GetDataUriResponse { MasterUriFormat = "https://offline.invalid/{0}/{1}" }))
        }))));
        ReplaceClient(network, "_unityHttpClient", new HttpClient(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("vars.js")
                ? new StringContent("var downloadApk = '/apps/mementomori_4.22.0.apk';")
                : new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("master-catalog") ? MessagePackSerializer.Serialize(catalog)
                    : request.RequestUri.AbsolutePath.EndsWith("TimeServerMB") ? valid : [0x90])
        }))));
        await Throws<InvalidDataException>(() => network.DownloadMasterCatalog(_ => { }));
        Require(File.ReadAllBytes("Master/TimeServerMB").SequenceEqual(old), "A failed batch replaced the working master file");
        Require(!File.Exists("Master/ItemMB") && !Directory.EnumerateFiles("Master", "*.tmp").Any(), "A rejected download left data or temporary files");
        Require(!MementoNetworkManager.HasUsableMasterData(), "An incomplete master directory was accepted as usable");
    }
    finally { Directory.SetCurrentDirectory(previousDirectory); directory.Delete(true); }
}

static void ReplaceClient(MementoNetworkManager network, string field, HttpClient replacement)
{
    ((HttpClient)Field(network, field).GetValue(network)!).Dispose();
    Field(network, field).SetValue(network, replacement);
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
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

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
