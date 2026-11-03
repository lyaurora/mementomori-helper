using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using AutoCtor;
using Injectio.Attributes;
using MementoMori.Jobs;
using MementoMori.Chat;
using MementoMori.Exceptions;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.ApiInterface;
using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.ApiInterface.LoginBonus;
using MementoMori.Ortega.Share.Data.ApiInterface.User;
using MementoMori.Ortega.Share.Data.Auth;
using MementoMori.Ortega.Share.Data.Mission;
using MementoMori.Ortega.Share.Data.Notice;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using BountyQuestGetListResponse = MementoMori.Ortega.Share.Data.ApiInterface.BountyQuest.GetListResponse;

namespace MementoMori.Funcs;

public record LogEntry(DateTime Time, string Message, bool IsError);

[RegisterTransient<MementoMoriFuncs>]
[AutoConstruct]
public partial class MementoMoriFuncs : ReactiveObject, IDisposable
{
    private const int Max_Err_Count = 20;
    private readonly AccountManager _accountManager;

    private readonly IWritableOptions<AuthOption> _AuthOption;
    private readonly BattleLogManager _battleLogManager;
    private readonly ILogger<MementoMoriFuncs> _logger;
    private readonly IWritableOptions<PlayersOption> _playersOption;
    private readonly IServiceProvider _serviceProvider;

    private readonly AsyncSemaphore _executionSemaphore = new(1);
    private readonly AsyncLocal<bool> _ownsExecutionSemaphore = new();
    private readonly TimeZoneAwareJobRegister _timeZoneAwareJobRegister;
    private readonly IWritableOptions<GameConfig> _writableGameConfig;

    private CancellationTokenSource? _cancellationTokenSource;
    private int _logoutVersion;
    private volatile bool _loggedOut;
    private int _disposed;
    private ChatSession? _chat;
    public ChatSession Chat => LazyInitializer.EnsureInitialized(ref _chat, () => new ChatSession(this));
    private string? _operationError;
    private CancellationToken OperationCancellation => _ownsExecutionSemaphore.Value ? _cancellationTokenSource?.Token ?? default : default;

    public ConcurrentDictionary<string, JobRun> JobRuns { get; } = new();
    public record JobRun(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Error = null, bool Cancelled = false);

    private PlayerDataInfo _lastPlayerDataInfo;

    /// <summary>World of the logged-in character (time server id * 1000 + world number); 0 before the first login.</summary>
    public long WorldId => _lastPlayerDataInfo?.WorldId ?? 0;
    public TimeManager TimeManager => NetworkManager.TimeManager;

    [Reactive]
    public UserSyncData UserSyncData { get; private set; }

    [Reactive]
    public Dictionary<MissionGroupType, MissionInfo> MissionInfoDict { get; set; }

    [Reactive]
    public GetMypageResponse Mypage { get; private set; }

    [Reactive]
    public BountyQuestGetListResponse BountyQuestResponseInfo { get; private set; }

    [Reactive]
    public GetMonthlyLoginBonusInfoResponse MonthlyLoginBonusInfo { get; private set; }

    [Reactive]
    public List<NoticeInfo> NoticeInfoList { get; set; }

    [Reactive]
    public List<NoticeInfo> EventInfoList { get; set; }

    [Reactive]
    public bool IsNotClearDungeonBattleMap { get; set; }

    private AuthOption AuthOption => _AuthOption.Value;
    private GameConfig GameConfig => _writableGameConfig.Value;

    public MementoNetworkManager NetworkManager { get; set; }

    private PlayerOption PlayerOption => _playersOption.Value.TryGetValue(NetworkManager.PlayerId, out var opt) ? opt : new PlayerOption();

    [Reactive]
    public bool Logining { get; private set; }

    [Reactive]
    public bool IsQuickActionExecuting { get; private set; }

    [Reactive]
    public string TrainingEquipmentGuid { get; set; }

    [Reactive]
    public BaseParameterType EquipmentTrainingTargetType { get; set; }

    [Reactive]
    public double EquipmentTrainingTargetPercent { get; set; }

    [Reactive]
    public TowerType SelectedAutoTowerType { get; set; }

    [Reactive]
    public bool ShowDebugInfo { get; set; }

    [Reactive]
    public bool BountyRequestForceAll { get; set; }

    [Reactive]
    public bool LoginOk { get; set; }

    public ObservableCollection<LogEntry> MesssageList { get; } = new();

    public long UserId { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _chat?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _executionSemaphore.Dispose();
    }

    [AutoPostConstruct]
    private void Initialize()
    {
        Mypage = new GetMypageResponse();
        NoticeInfoList = new List<NoticeInfo>();
        UserSyncData = new UserSyncData();
    }

    // Listing characters is a full title-screen login (auth/login): the server rotates the token and the current session dies.
    // While logged in, reuse the list from the last login; choosing a character logs in again anyway.
    private List<PlayerDataInfo>? _playerDataInfos;
    private string? _playerDataInfosKey;

    public async Task<List<PlayerDataInfo>> GetPlayerDataInfo()
    {
        var clientKey = _accountManager.GetAccountInfo(UserId).ClientKey;
        if (LoginOk && _playerDataInfos != null && _playerDataInfosKey == clientKey) return _playerDataInfos;
        var reqBody = new LoginRequest
        {
            ClientKey = clientKey,
            DeviceToken = AuthOption.DeviceToken,
            AppVersion = AuthOption.AppVersion,
            OSVersion = AuthOption.OSVersion,
            ModelName = AuthOption.ModelName,
            AdverisementId = Guid.NewGuid().ToString("D"),
            UserId = UserId
        };
        _playerDataInfos = await NetworkManager.GetPlayerDataInfoList(reqBody, AddLog, OperationCancellation);
        _playerDataInfosKey = clientKey;
        return _playerDataInfos;
    }

    public async Task<GetUserDataResponse> UserGetUserData()
    {
        var req = new GetUserDataRequest();
        var data = await GetResponse<GetUserDataRequest, GetUserDataResponse>(req);
        UserSyncData = data.UserSyncData;
        IsNotClearDungeonBattleMap = data.IsNotClearDungeonBattleMap;
        return data;
    }

    /// <param name="ignoreOperationCancellation">Only <paramref name="cancellationToken"/> can cancel the request; use for restore/cleanup requests that must still go out after the user cancels.</param>
    public async Task<TResp> GetResponse<TReq, TResp>(TReq req, CancellationToken cancellationToken = default, bool ignoreOperationCancellation = false)
        where TReq : ApiRequestBase
        where TResp : ApiResponseBase
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ignoreOperationCancellation ? default : OperationCancellation, cancellationToken);
        try
        {
            return await NetworkManager.GetResponse<TReq, TResp>(req, AddLog, data =>
            {
                RecordGrowth(data);
                UserSyncData.UserItemEditorMergeUserSyncData(data);
                this.RaisePropertyChanged(nameof(UserSyncData));
            }, cancellationToken: linked.Token);
        }
        catch (ApiErrorException e) when (e.ErrorCode is ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice)
        {
            // The server dropped this session; let AutoLoginJob / the next scheduled job log in again.
            LoginOk = false;
            _sessionLost = true;
            // Another device took over: give the player 15 minutes before the helper kicks it.
            if (e.ErrorCode == ErrorCode.CommonLoggedInAnotherDevice) NextAutoLoginAttempt = DateTimeOffset.UtcNow.AddMinutes(15);
            // Pages call this directly; their banner then only says "please log in", so record why the session ended.
            if (!_ownsExecutionSemaphore.Value) AddError(ErrorText(e));
            throw;
        }
    }

    // Game day starts at 04:00 server time; the extra minutes keep a job at exactly 04:00 from re-logging in on pre-reset data.
    private DateTime GameDay => (TimeManager.ServerNow - TimeSpan.FromHours(4) - TimeSpan.FromMinutes(5)).Date;
    private DateTime _loginGameDay;
    private bool _sessionLost;
    // Respect the back-off after another device took over (112) and a permanent auth failure.
    private bool CanJobLogin => !_autoLoginStopped && DateTimeOffset.UtcNow >= NextAutoLoginAttempt;

    // Through ExecuteQuickAction so a failed sync shows in the execution log instead of breaking the page.
    public Task SyncUserData() => ExecuteQuickAction(async (_, _) => await UserGetUserData());

    private void AddLog(string message) => AddEntry(message, false);

    private void AddError(string message) => AddEntry(message, true);

    // The API path makes a failed request easy to locate; Message alone is what the player sees elsewhere.
    private static string ErrorText(Exception e) => e is ApiErrorException { Path: { } path } ? $"{path}: {e.Message}" : e.Message;

    private void AddEntry(string message, bool isError)
    {
        var now = DateTime.Now;
        // Before the first login there is no player yet: name the account instead of printing "[(Lv)]".
        var who = _lastPlayerDataInfo is { } player ? $"{player.Name}(Lv{player.PlayerRank})" : _accountManager?.GetAccountInfo(UserId)?.Name ?? UserId.ToString();
        Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] [{who}] {(isError ? "ERROR " : "")}{message}");
        lock (MesssageList)
        {
            MesssageList.Insert(0, new LogEntry(now, message, isError));
            if (MesssageList.Count > 100) MesssageList.RemoveAt(MesssageList.Count - 1);
        }
    }

    public async Task ExecuteQuickAction(Func<Action<string>, CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        var nested = _ownsExecutionSemaphore.Value;
        // Session lost earlier in the same job: skip the remaining steps instead of failing each one on the dead session.
        if (nested && _sessionLost) return;
        try
        {
            await ExecuteExclusive(async token =>
            {
                // Each job starts fresh: its first request then logs why the session is gone, instead of every step skipping silently.
                if (!nested) _sessionLost = false;
                try
                {
                    await func(AddLog, token);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    _operationError = e.Message;
                    var message = ErrorText(e);
                    _logger.LogError(e, "Quick action failed: {Message}", message);
                    AddError(message);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (!nested)
        {
            // Our own token was cancelled (取消 button or logout), not the caller's (scheduler shutdown): say so in the output.
            if (!cancellationToken.IsCancellationRequested) AddLog(ResourceStrings.TaskCancelled);
        }
    }

    public async Task ExecuteScheduledJob(Func<Task> func, CancellationToken cancellationToken, Func<bool>? canExecute = null, string? jobName = null,
        bool retryAfterRelogin = false)
    {
        var logoutVersion = Volatile.Read(ref _logoutVersion);
        if (_loggedOut || IsPaused || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;

        await ExecuteQuickAction(async (_, token) =>
        {
            if (_loggedOut || IsPaused || logoutVersion != Volatile.Read(ref _logoutVersion)
                || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;
            var key = jobName ?? func.Method.Name;
            var run = new JobRun(DateTimeOffset.UtcNow);
            JobRuns[key] = run;
            this.RaisePropertyChanged(nameof(JobRuns));
            try
            {
                // Log in when the session is gone (GetResponse clears LoginOk on session errors) or once per game day so
                // daily work sees fresh user data / Mypage; otherwise keep the session, since a re-login resets chat.
                if (CanJobLogin && (!LoginOk || _loginGameDay != GameDay)) await Login();
                if (!LoginOk) _operationError ??= LastLoginError ?? ResourceStrings.PleaseLogin;
                if (token.IsCancellationRequested || _loggedOut || !LoginOk
                    || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;
                await func();
                // The session died silently during the job: log in again and run it once more. Only for jobs whose
                // steps are bounded by fresh server state; a replay of daily/relic jobs could spend diamonds twice.
                if (retryAfterRelogin && !LoginOk && CanJobLogin && !token.IsCancellationRequested)
                {
                    await Login();
                    if (LoginOk)
                    {
                        _operationError = null;
                        await func();
                    }
                }
            }
            catch (Exception e)
            {
                _operationError = e.Message;
                throw;
            }
            finally
            {
                JobRuns[key] = run with { FinishedAt = DateTimeOffset.UtcNow, Error = _operationError, Cancelled = token.IsCancellationRequested || _loggedOut || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false };
                this.RaisePropertyChanged(nameof(JobRuns));
            }
        }, cancellationToken);
    }

    public void CancelQuickAction()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ExecuteExclusive(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_ownsExecutionSemaphore.Value)
        {
            var token = _cancellationTokenSource?.Token ?? cancellationToken;
            token.ThrowIfCancellationRequested();
            await func(token);
            return;
        }

        using var releaser = await _executionSemaphore.EnterAsync(cancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ownsExecutionSemaphore.Value = true;
        _cancellationTokenSource = cts;
        _operationError = null;
        IsQuickActionExecuting = true;
        try
        {
            cts.Token.ThrowIfCancellationRequested();
            await func(cts.Token);
        }
        finally
        {
            IsQuickActionExecuting = false;
            _cancellationTokenSource = null;
            _ownsExecutionSemaphore.Value = false;
        }
    }


    public async Task GetMyPage()
    {
        await ExecuteQuickAction(async (log, token) => { Mypage = await GetResponse<GetMypageRequest, GetMypageResponse>(new GetMypageRequest {LanguageType = NetworkManager.LanguageType}); });
    }

    public async Task Debug()
    {
        await ExecuteQuickAction(async (log, token) => { await AutoGuildTower(); });
    }

    public async Task LogDebug()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var counter = 0;
            while (!token.IsCancellationRequested)
            {
                log($"Message {counter++}");
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            log("DDDDD");
        });
    }


    public Task ExecuteAllQuickAction()
    {
        return ExecuteQuickAction(async (_, _) =>
        {
            await GetLoginBonus();
            await GetVipGift();
            await ReceiveMonthlyBoost();
            await GetAutoBattleReward();
            await BulkTransferFriendPoint();
            await PresentReceiveItem();
            if (GameConfig.AutoJob.AutoReinforcementEquipmentOneTime) await ReinforcementEquipmentOneTime();
            await BattleBossQuick();
            await InfiniteTowerQuick();
            await BossHishSpeedBattle();
            await ReceiveGvgReward();
            await GuildCheckin();
            await GuildRaid();
            await AutoGuildTower();
            await AutoFriendManage();
            await RewardFriendMissions();
            await ReceiveAchievementReward();
            await ReadAllMemories();
            await BountyQuestRewardAuto();
            await BountyQuestStartAuto();
            if (GameConfig.AutoJob.AutoDungeonBattle) await AutoDungeonBattle();
            await CompleteMissions();
            await RewardMissonActivity();
            await ReceiveRentalRaidShareReward();
            if (GameConfig.AutoJob.AutoUseItems) await AutoUseItems();
            if (GameConfig.AutoJob.AutoFreeGacha) await FreeGacha();
            if (GameConfig.AutoJob.AutoUseItems) await AutoUseItems();
            if (GameConfig.AutoJob.AutoRankUpCharacter) await AutoRankUpCharacter();
        });
    }
}
