using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using AutoCtor;
using Injectio.Attributes;
using MementoMori.Jobs;
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
    private string? _operationError;
    private CancellationToken OperationCancellation => _ownsExecutionSemaphore.Value ? _cancellationTokenSource?.Token ?? default : default;

    public ConcurrentDictionary<string, JobRun> JobRuns { get; } = new();
    public record JobRun(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Error = null, bool Cancelled = false);

    private PlayerDataInfo _lastPlayerDataInfo;
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

    public ObservableCollection<string> MesssageList { get; } = new();

    public long UserId { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
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

    public async Task<List<PlayerDataInfo>> GetPlayerDataInfo()
    {
        var reqBody = new LoginRequest
        {
            ClientKey = _accountManager.GetAccountInfo(UserId).ClientKey,
            DeviceToken = AuthOption.DeviceToken,
            AppVersion = AuthOption.AppVersion,
            OSVersion = AuthOption.OSVersion,
            ModelName = AuthOption.ModelName,
            AdverisementId = Guid.NewGuid().ToString("D"),
            UserId = UserId
        };
        return await NetworkManager.GetPlayerDataInfoList(reqBody, AddLog, OperationCancellation);
    }

    public async Task<GetUserDataResponse> UserGetUserData()
    {
        var req = new GetUserDataRequest();
        var data = await GetResponse<GetUserDataRequest, GetUserDataResponse>(req);
        UserSyncData = data.UserSyncData;
        IsNotClearDungeonBattleMap = data.IsNotClearDungeonBattleMap;
        return data;
    }

    public async Task<TResp> GetResponse<TReq, TResp>(TReq req)
        where TReq : ApiRequestBase
        where TResp : ApiResponseBase
    {
        return await NetworkManager.GetResponse<TReq, TResp>(req, AddLog, data =>
        {
            UserSyncData.UserItemEditorMergeUserSyncData(data);
            this.RaisePropertyChanged(nameof(UserSyncData));
        }, cancellationToken: OperationCancellation);
    }

    public async Task SyncUserData()
    {
        await UserGetUserData();
    }

    private void AddLog(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{_lastPlayerDataInfo?.Name}(Lv{_lastPlayerDataInfo?.PlayerRank})] {message}");
        lock (MesssageList)
        {
            MesssageList.Insert(0, message);
            if (MesssageList.Count > 100) MesssageList.RemoveAt(MesssageList.Count - 1);
        }
    }

    public async Task ExecuteQuickAction(Func<Action<string>, CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        var nested = _ownsExecutionSemaphore.Value;
        try
        {
            await ExecuteExclusive(async token =>
            {
                try
                {
                    await func(AddLog, token);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    _operationError = e.Message;
                    AddLog(e.ToString());
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (!nested) { }
    }

    public async Task ExecuteScheduledJob(Func<Task> func, CancellationToken cancellationToken, Func<bool>? canExecute = null, string? jobName = null)
    {
        var logoutVersion = Volatile.Read(ref _logoutVersion);
        if (_loggedOut || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;

        await ExecuteQuickAction(async (_, token) =>
        {
            if (_loggedOut || logoutVersion != Volatile.Read(ref _logoutVersion)
                || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;
            var key = jobName ?? func.Method.Name;
            var run = new JobRun(DateTimeOffset.UtcNow);
            JobRuns[key] = run;
            this.RaisePropertyChanged(nameof(JobRuns));
            try
            {
                await Login();
                if (token.IsCancellationRequested || _loggedOut || !LoginOk
                    || GameConfig.AutoJob.DisableAll || canExecute?.Invoke() == false) return;
                await func();
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
            await ReceiveAchievementReward();
            await BountyQuestRewardAuto();
            await BountyQuestStartAuto();
            if (GameConfig.AutoJob.AutoDungeonBattle) await AutoDungeonBattle();
            await CompleteMissions();
            await RewardMissonActivity();
            if (GameConfig.AutoJob.AutoUseItems) await AutoUseItems();
            if (GameConfig.AutoJob.AutoFreeGacha) await FreeGacha();
            if (GameConfig.AutoJob.AutoUseItems) await AutoUseItems();
            if (GameConfig.AutoJob.AutoRankUpCharacter) await AutoRankUpCharacter();
        });
    }
}
