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
using MementoMori.Chat;
using Grpc.Net.Client;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Network.MagicOnion.Interface;
using MementoMori.Ortega.Share.Data.ApiInterface.Chat;
using MementoMori.Ortega.Share.MagicOnionShare.Interfaces.Receiver;
using MementoMori.Ortega.Share.MagicOnionShare.Interfaces.Sender;
using MementoMori.Ortega.Share.MagicOnionShare.Request;
using MementoMori.Ortega.Share.MagicOnionShare.Response;
using MementoMori.BlazorShared.Models;
using MementoMori.Funcs;
using MementoMori.Jobs;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor.Services;
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
    ("manual quick-action batch stops after cancellation", CheckBatchCancellation),
    ("chat routing, history, reactions, delivery confirmation and cancellation", CheckChat),
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

static async Task CheckBatchCancellation()
{
    var auth = new Writable<AuthOption>(new() { AuthUrl = "https://offline.invalid/api/", AppVersion = "4.22.0" });
    var config = new Writable<GameConfig>(new());
    using var network = Construct<MementoNetworkManager>(auth, config, NullLogger<MementoNetworkManager>.Instance);
    Field(network, "_apiHost").SetValue(network, new Uri("https://offline.invalid/api/"));
    using var funcs = Construct<MementoMoriFuncs>(auth, config, NullLogger<MementoMoriFuncs>.Instance);
    funcs.NetworkManager = network;
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requests = 0;
    ReplaceClient(network, "_httpClient", new HttpClient(new Handler(async (_, token) =>
    {
        if (++requests == 1)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    })));
    var batch = funcs.ExecuteAllQuickAction();
    try
    {
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(funcs.IsQuickActionExecuting, "The manual batch is not marked busy");
        funcs.CancelQuickAction();
        await batch.WaitAsync(TimeSpan.FromSeconds(2));
        Require(requests == 1 && !funcs.IsQuickActionExecuting, "Cancelling one step allowed later batch operations to run");
    }
    finally { funcs.CancelQuickAction(); }
}

static Task CheckProtocol()
{
    // Fixed wire payloads from the 4.22.0 contract, independent of the serializer's output.
    var chat = MessagePackSerializer.Deserialize<ChatInfo>(MessagePackSerializer.ConvertFromJson(
        """[0,0,"message",1,"player","",0,0,0,0,[],"guild",0,null,null,0,[2,"recruit guild",3,4,0,0,"join us"]]"""));
    Require(chat.ChatRecruitGuildMemberInfo.GuildId == 2 && chat.ChatRecruitGuildMemberInfo.RecruitMessage == "join us", "Chat recruitment key 16 is not decoded");
    var privatePlayer = MessagePackSerializer.Deserialize<PrivateChatLogPlayerInfo>(MessagePackSerializer.ConvertFromJson(
        """[true,{"PlayerId":123,"PlayerName":"private player"},456]"""));
    Require(privatePlayer.ExistUnread && privatePlayer.PlayerInfo.PlayerId == 123 && privatePlayer.LocalTimestamp == 456, "Private chat numeric keys drifted");
    var privatePush = MessagePackSerializer.Deserialize<OnReceiveMessageResponse>(MessagePackSerializer.ConvertFromJson(
        """[[0,3,"private",101,"me","",0,456,0,0,[],"",0,null,null,0,null],202]"""));
    Require(privatePush.OtherPlayerId == 202 && privatePush.ChatInfo.PlayerId == 101, "Private push peer key 1 is missing");
    var guildChat = MessagePackSerializer.Deserialize<OnReceiveGuildChatLogResponse>(MessagePackSerializer.ConvertFromJson(
        """[null,[[[0,2,"guild",123,"player","",0,456,0,0,[],"guild",0,null,null,0,null],0,{},true,true]],789]"""));
    Require(guildChat.GuildChatInfoList[0].CanReact && guildChat.GuildChatInfoList[0].IsAnnounced && guildChat.AnnounceChatEndIntervalTimestamp == 789, "Modern guild chat keys drifted");
    var settings = new ChatSettingData { FontSize = 25, BalloonItemId = 7, BackgroundTypeDictionary = new() { [ChatType.Guild] = ChatBackgroundType.Default } };
    var copy = settings.DeepCopy();
    copy.BackgroundTypeDictionary.Clear();
    Require(settings.BackgroundTypeDictionary.Count == 1 && copy.FontSize == 25 && copy.BalloonItemId == 7, "Chat settings copy loses data or aliases mutable settings");
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

static async Task CheckChat()
{
    var auth = new Writable<AuthOption>(new() { AuthUrl = "https://offline.invalid/api/", AppVersion = "4.22.0" });
    var config = new Writable<GameConfig>(new());
    using var network = Construct<MementoNetworkManager>(auth, config, NullLogger<MementoNetworkManager>.Instance);
    Field(network, "_apiHost").SetValue(network, new Uri("https://offline.invalid/api/"));
    network.PlayerId = 101;
    using var funcs = Construct<MementoMoriFuncs>(auth, config, NullLogger<MementoMoriFuncs>.Instance);
    funcs.NetworkManager = network;
    funcs.LoginOk = true;
    funcs.UserSyncData.BlockPlayerIdList = [999];
    using var chat = funcs.Chat;
    using var otherChat = new ChatSession(funcs);
    using var lifetime = new CancellationTokenSource();
    Field(chat, "_lifetime").SetValue(chat, lifetime);
    Field(chat, "_playerId").SetValue(chat, 101L);
    Field(chat, "_connected").SetValue(chat, true);
    var receiverType = typeof(ChatSession).GetNestedType("Receiver", BindingFlags.NonPublic)!;
    var receiver = (IMagicOnionChatReceiver)Activator.CreateInstance(receiverType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [chat, lifetime.Token], null)!;
    using var channel = GrpcChannel.ForAddress("https://offline.invalid");
    var client = new OrtegaMagicOnionClient(channel, 101, "offline", null);
    client.SetupChat(receiver, (IMagicOnionAuthenticateReceiver)receiver, (IMagicOnionErrorReceiver)receiver);
    Field(chat, "_client").SetValue(chat, client);
    var inbound = (IOrtegaReceiver)client;
    inbound.OnAuthenticateSuccess();
    var hub = DispatchProxy.Create<IOrtegaSender, ChatHubProxy>();
    Field(client, "_sender").SetValue(client, hub);
    var proxy = (ChatHubProxy)hub;
    var sentRequests = new List<SendMessageRequest>();
    long echoTimestamp = 10_000;
    proxy.Send = request =>
    {
        sentRequests.Add(request);
        inbound.OnReceiveMessage(new() { ChatInfo = new() { ChatType = request.ChatType, PlayerId = 101, Message = request.Message, LocalTimeStamp = echoTimestamp++ } });
        return Task.CompletedTask;
    };
    try
    {
        ChatInfo Message(long id, long time, string text = "hello", ChatType type = ChatType.World) => new() { PlayerId = id, LocalTimeStamp = time, Message = text, ChatType = type };
        inbound.OnReceiveWorldChatLog(new() { ChatInfoList = [Message(2, 20), Message(2, 10), Message(999, 30)] });
        inbound.OnReceiveWorldChatLog(new() { ChatInfoList = [Message(2, 20)] });
        inbound.OnReceiveBlockChatLog(new() { ChatInfoList = [Message(3, 30, type: ChatType.Block)] });
        inbound.OnReceiveSvSChatLog(new() { ChatInfoList = [Message(4, 40, type: ChatType.SvS)] });
        Require(chat.Read(ChatType.World).Messages.Select(m => m.ChatInfo.LocalTimeStamp).SequenceEqual([10L, 20L]), "History is not deduplicated/sorted or blocked players leak");
        Require(chat.Read(ChatType.Block).Messages.Count == 1 && chat.Read(ChatType.SvS).Messages.Count == 1, "Cross-world channels are dropped");
        Require(otherChat.Read(ChatType.World).Messages.Count == 0, "Chat state leaks across sessions");

        var guildMessage = Message(5, 50, type: ChatType.Guild);
        inbound.OnReceiveGuildChatLog(new() { GuildChatInfoList = [new() { ChatInfo = guildMessage, CanReact = true, ChatReactionCountMap = new() }] });
        var reaction = new ReactChatInfo { ChatIdentityInfo = ChatSession.Identity(guildMessage), ChatReactionType = ChatReactionType.Heart, ReactPlayerId = 101 };
        inbound.OnReactChat(new() { ReactChatInfoList = [reaction, reaction] });
        var guild = chat.Read(ChatType.Guild).Messages.Single();
        Require(guild.MyChatReactionType == ChatReactionType.Heart && guild.ChatReactionCountMap[ChatReactionType.Heart] == 1, "Duplicate reaction increments twice");
        reaction.IsCanceled = true;
        inbound.OnReactChat(new() { ReactChatInfoList = [reaction, reaction] });
        guild = chat.Read(ChatType.Guild).Messages.Single();
        Require(guild.MyChatReactionType == ChatReactionType.None && guild.ChatReactionCountMap[ChatReactionType.Heart] == 0, "Reaction cancellation is ignored or goes negative");
        inbound.OnChangeChatOption(new() { ChangeChatOptionInfoList = [new() { ChatIdentityInfo = ChatSession.Identity(guildMessage), CanReact = false, IsAnnounced = true }] });
        inbound.OnReceiveMessage(new() { ChatInfo = guildMessage });
        guild = chat.Read(ChatType.Guild).Messages.Single();
        Require(!guild.CanReact && guild.IsAnnounced, "Duplicate message resets guild chat options");
        guild.ChatReactionCountMap[ChatReactionType.Heart] = 42;
        Require(chat.Read(ChatType.Guild).Messages.Single().ChatReactionCountMap[ChatReactionType.Heart] == 0, "Snapshot exposes live mutable reaction counts");
        inbound.OnRemovedFromGuild();
        Require(chat.Read(ChatType.Guild).Messages.Count == 0, "Leaving a guild retains its chat");
        inbound.OnReceiveWorldChatLog(new() { ChatInfoList = Enumerable.Range(1, 250).Select(i => Message(2, i)).ToList() });
        Require(chat.Read(ChatType.World).Messages.Count == ChatSession.HistoryLimit && chat.Read(ChatType.World).Messages[0].ChatInfo.LocalTimeStamp == 51, "History grows without a bound");

        await Throws<ArgumentException>(() => chat.SendAsync(ChatType.World, "   "));
        await Throws<ArgumentException>(() => chat.SendAsync(ChatType.World, new string('x', 81)));
        await Throws<ArgumentOutOfRangeException>(() => chat.SendAsync(ChatType.Friend, "hello"));
        await Throws<ArgumentException>(() => chat.SendAsync(ChatType.Private, "hello", 101));
        await Throws<InvalidOperationException>(() => chat.SendAsync(ChatType.World, "#1001#"));
        Require(chat.IsEmoticonUnlocked(1) && !chat.IsEmoticonUnlocked(1001), "Sticker ownership rules differ from the game");
        funcs.UserSyncData.UserItemDtoInfo = [new() { ItemType = ItemType.ChatEmoticon, ItemId = 1001, ItemCount = 1 }];
        Require(chat.IsEmoticonUnlocked(1001), "Owned character sticker remains locked");
        Require(!ChatSession.CanManageGuildChat(PlayerGuildPositionType.Member)
            && ChatSession.CanDeleteGuildPost(PlayerGuildPositionType.Veteran, 101, 101)
            && !ChatSession.CanDeleteGuildPost(PlayerGuildPositionType.Veteran, 202, 101), "Guild management permissions are wrong");
        Require(!ChatSession.CanRegisterAnnouncement(PlayerGuildPositionType.Member, Message(101, 1, type: ChatType.Guild), 101)
            && !ChatSession.CanRegisterAnnouncement(PlayerGuildPositionType.Veteran, Message(202, 1, type: ChatType.Guild), 101)
            && ChatSession.CanRegisterAnnouncement(PlayerGuildPositionType.Veteran, Message(101, 1, type: ChatType.Guild), 101), "Announcement registration ignores ownership or rank");
        Require(sentRequests.Count == 0, "Invalid input reached the hub");
        await chat.SendAsync(ChatType.Block, " hello ");
        Require(sentRequests.Single().ChatType == ChatType.Block && sentRequests[0].Message == "hello", "Send targets the wrong channel or loses text");
        proxy.Send = request => { sentRequests.Add(request); inbound.OnError(ErrorCode.MagicOnionChatLimitOver); return Task.CompletedTask; };
        await Throws<ApiErrorException>(() => chat.SendAsync(ChatType.World, "rejected"));
        Require(sentRequests.Count == 2, "Rejected send was retried");

        inbound.OnReceiveGuildChatLog(new() { GuildChatInfoList = [new() { ChatInfo = guildMessage, CanReact = false, MyChatReactionType = ChatReactionType.Heart, ChatReactionCountMap = new() { [ChatReactionType.Heart] = 1 } }] });
        var reactionRequests = new List<ChatReactionType>();
        var oldAnnouncement = Message(404, 1, "old announcement", ChatType.Guild);
        ReplaceClient(network, "_httpClient", new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/chat/getAnnounceChat")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MessagePackSerializer.Serialize(new GetAnnounceChatResponse
                {
                    GuildChatInfoList = [new() { RegisterLocalTimestamp = 2, GuildChatInfo = new() { ChatInfo = oldAnnouncement, IsAnnounced = true, MyChatReactionType = ChatReactionType.Heart, ChatReactionCountMap = new() { [ChatReactionType.Heart] = 2 } } }]
                })) };
            Require(request.RequestUri!.AbsolutePath == "/api/chat/reactChat", "Reaction uses the wrong route");
            var requestData = MessagePackSerializer.Deserialize<ReactChatRequest>(await request.Content!.ReadAsByteArrayAsync(token));
            reactionRequests.Add(requestData.ChatReactionType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MessagePackSerializer.ConvertFromJson("{}")) };
        })));
        await chat.ReactAsync(guildMessage, ChatReactionType.Heart);
        await chat.ReactAsync(guildMessage, ChatReactionType.Cheers);
        Require(reactionRequests.SequenceEqual([ChatReactionType.None, ChatReactionType.Cheers]), "Cancel must send None; switching must send the newly selected type");
        await chat.RequestAsync<GetAnnounceChatRequest, GetAnnounceChatResponse>(new());
        Require(chat.ReadAnnouncements().Single().GuildChatInfo.ChatReactionCountMap[ChatReactionType.Heart] == 2, "Announcement reactions are missing");
        inbound.OnReactChat(new() { ReactChatInfoList = [new() { ChatIdentityInfo = ChatSession.Identity(oldAnnouncement), ReactPlayerId = 303, ChatReactionType = ChatReactionType.Heart }] });
        Require(chat.ReadAnnouncements().Single().GuildChatInfo.ChatReactionCountMap[ChatReactionType.Heart] == 3, "Live reactions ignore announcements outside recent history");
        await chat.ReactAsync(oldAnnouncement, ChatReactionType.Heart);
        Require(reactionRequests.Last() == ChatReactionType.None, "Old announcements cannot be reacted to or canceled");

        var requests = new List<string>();
        inbound.OnReceiveMessage(new() { ChatInfo = Message(101, 499, "sent on another client", ChatType.Private), OtherPlayerId = 202 });
        Require(chat.Read(ChatType.Private, 202).Messages.Count == 1, "Outgoing private push lacks recipient routing");
        ReplaceClient(network, "_httpClient", new HttpClient(new Handler(async (request, token) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsByteArrayAsync(token);
            byte[] response;
            if (request.RequestUri.AbsolutePath.EndsWith("sendPrivateMessage"))
            {
                var message = MessagePackSerializer.Deserialize<SendPrivateMessageRequest>(body);
                Require(message.TargetPlayerId == 202 && message.Message == "private hello", "Private send lost target or content");
                response = MessagePackSerializer.ConvertFromJson("{}");
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("getPrivateMessage"))
            {
                var history = MessagePackSerializer.Deserialize<GetPrivateMessageRequest>(body);
                Require(history.TargetPlayerId == 202, "Private history uses a different recipient");
                Require(history.LatestTimestamp == 0 && history.OldestTimestamp == 0, "An early private push skips initial history");
                response = MessagePackSerializer.Serialize(new GetPrivateMessageResponse { ChatInfoList = [Message(101, 500, "private hello", ChatType.Private), Message(202, 501, "reply", ChatType.Private)] });
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("getPrivateChatLogPlayer"))
                response = MessagePackSerializer.ConvertFromJson("""{"PrivateChatLogPlayerInfoList":[[false,{"PlayerId":202,"PlayerName":"recipient"},501]]}""");
            else throw new InvalidOperationException("Unexpected chat API " + request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) };
        })));
        await chat.SendAsync(ChatType.Private, "private hello", 202);
        Require(requests.SequenceEqual(["/api/chat/sendPrivateMessage", "/api/chat/getPrivateMessage"]), "Private send must use HTTP and refresh once");
        Require(chat.Read(ChatType.Private, 202).Messages.Count == 3 && chat.Read(ChatType.Private, 203).Messages.Count == 0, "Private conversations are mixed");
        await chat.RefreshContactsAsync();
        inbound.OnNoticePrivateMessage(new() { PlayerId = 202 });
        Require(chat.Read(ChatType.Private, 202).Contacts.Single().ExistUnread, "Private notice is ignored");

        inbound.OnReceiveMessage(new() { ChatInfo = Message(8, 9_000, "<script>alert('chat')</script>") });
        Masters.TextResourceTable.Load(MessagePackSerializer.ConvertFromJson("""[{"StringKey":"[ChatTestSystem]","Text":"Castle {0}"},{"StringKey":"[GlobalGvgCastleName21]","Text":"Rula"}]"""));
        inbound.OnReceiveMessage(new() { ChatInfo = new() { PlayerId = 0, ChatType = ChatType.World, LocalTimeStamp = 9001, SystemChatMessageKey = "[ChatTestSystem]", SystemChatMessageArgs = ["[GlobalGvgCastleName21]"] } });
        auth.Value.Accounts.Add(new AccountInfo { UserId = 1, Name = "offline account" });
        var manager = Construct<AccountManager>(auth, config, NullLogger<AccountManager>.Instance);
        var accounts = (ConcurrentDictionary<long, Account>)Field(manager, "_accounts").GetValue(manager)!;
        accounts[1] = new Account { AccountInfo = auth.Value.Accounts[0], Funcs = funcs, NetworkManager = network };
        var services = new ServiceCollection().AddLogging().AddSingleton(manager).AddScoped<AccountSelection>()
            .AddSingleton(Construct<MementoMori.WebUI.UI.AtlasManager>(config))
            .AddSingleton<IJSRuntime, ChatJsStub>().AddSingleton<NavigationManager, ChatNavigation>()
            .AddSingleton<MementoMori.BlazorShared.IFileSaver, ChatFileSaver>();
        services.AddMudServices();
        Masters.SpecialIconItemTable.Load(MessagePackSerializer.ConvertFromJson("""[{"Id":2,"CharacterId":109,"IconId":1}]"""));
        var atlas = Construct<MementoMori.WebUI.UI.AtlasManager>(config);
        Require(atlas.GetPlayerIcon(109)!.EndsWith("CHR_000109_00_s.png")
            && atlas.GetPlayerIcon(long.MinValue | 2)!.EndsWith("CHR_000109_00_em_001.png"), "Special player avatar ID is treated as missing or as a character ID");
        await using (var provider = services.BuildServiceProvider())
        await using (var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance))
        {
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<MementoMori.BlazorShared.Pages.Chat>()).ToHtmlString());
            Require(html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "Chat text is interpreted as HTML");
            Require(html.Contains("Castle Rula") && !html.Contains("[GlobalGvgCastleName21]"), "System-message parameters are not localized");
            Require(ChatEmoticons.Ids.Count() == 39 && ChatEmoticons.TryGetId("#1007#", out var sticker) && sticker == 1007
                && !ChatEmoticons.TryGetId("#999999#", out _), "Sticker token or atlas lookup is wrong");
            Require((int)Field(chat, "_viewers").GetValue(chat)! == 0, "Prerender opens a live chat connection");
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.Send = request => { sentRequests.Add(request); started.TrySetResult(); return Task.CompletedTask; };
        var waiting = chat.SendAsync(ChatType.World, "pending");
        await started.Task;
        Require(!waiting.IsCompleted, "Hub return is mistaken for delivery confirmation");
        var queued = chat.SendAsync(ChatType.World, "must not send");
        await chat.ResetAsync();
        await Throws<Exception>(() => waiting);
        await Throws<OperationCanceledException>(() => queued);
        Require(sentRequests.Count == 3, "A queued message was sent after session cancellation");
        inbound.OnReceiveWorldChatLog(new() { ChatInfoList = [Message(8, 88)] });
        Require(chat.Read(ChatType.World).Messages.Count == 0 && chat.Read(ChatType.Private, 202).Contacts.Count == 0, "Late callbacks repopulate a cleared session");
    }
    finally { await client.DisposeAsync(); }
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

static FieldInfo Field(object value, string name)
{
    for (var type = value.GetType(); type != null; type = type.BaseType)
        if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } field) return field;
    throw new MissingFieldException(value.GetType().Name, name);
}

public class ChatHubProxy : DispatchProxy
{
    public Func<SendMessageRequest, Task> Send { get; set; } = _ => Task.CompletedTask;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "SendMessageAsync" => Send((SendMessageRequest)args![0]!),
        "DisposeAsync" => Task.CompletedTask,
        _ => throw new NotSupportedException(method?.Name)
    };
}

sealed class ChatJsStub : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}

sealed class ChatNavigation : NavigationManager
{
    public ChatNavigation() => Initialize("http://offline.invalid/", "http://offline.invalid/Chat");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}

sealed class ChatFileSaver : MementoMori.BlazorShared.IFileSaver
{
    public Task SaveFile(string content, string filename) => Task.CompletedTask;
}

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
