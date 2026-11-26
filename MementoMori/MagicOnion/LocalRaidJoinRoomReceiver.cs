using System.Diagnostics;
using MementoMori.Ortega.Common.Enums;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Network.MagicOnion.Interface;
using MementoMori.Ortega.Share.Data.LocalRaid;
using MementoMori.Ortega.Share.MagicOnionShare.Response;

namespace MementoMori.MagicOnion;

public abstract class LocalRaidBaseReceiver : IMagicOnionLocalRaidReceiver, IMagicOnionErrorReceiver
{
    protected readonly OrtegaMagicOnionClient _ortegaMagicOnionClient;
    protected readonly CancellationToken CancellationToken;
    protected Action<string> _log;
    protected Stopwatch _startSw;

    public LocalRaidBaseReceiver(OrtegaMagicOnionClient ortegaMagicOnionClient, Action<string> log, CancellationToken cancellationToken)
    {
        _ortegaMagicOnionClient = ortegaMagicOnionClient;
        _log = log;
        CancellationToken = cancellationToken;
        _startSw = Stopwatch.StartNew();
    }

    private long _questId;
    private volatile bool _isNoRemainingChallenges;
    private volatile bool _isMaxTimeExceeded;
    private volatile bool _isBattleStarted;

    public long QuestId
    {
        get => Interlocked.Read(ref _questId);
        set
        {
            Interlocked.Exchange(ref _questId, value);
            // A new search can use the same quest as the previous battle.
            _startSw.Restart();
            IsMaxTimeExceeded = false;
            OnRoundReset();
        }
    }
    public bool IsNoRemainingChallenges { get => _isNoRemainingChallenges; protected set => _isNoRemainingChallenges = value; }
    public bool IsMaxTimeExceeded { get => _isMaxTimeExceeded; protected set => _isMaxTimeExceeded = value; }

    public bool IsBattleStarted { get => _isBattleStarted; set => _isBattleStarted = value; }

    public bool IsSearching => _startSw.IsRunning;

    protected virtual void OnRoundReset()
    {
    }

    public virtual void OnError(ErrorCode errorCode)
    {
        _log($"OnError: {TextResourceTable.GetErrorCodeMessage(errorCode)}");
    }

    public virtual void OnGetRoomList(OnGetRoomListResponse response)
    {
    }

    public virtual void OnDisbandRoom()
    {
        _log("OnDisbandRoom");
    }

    public virtual void OnInvite(OnInviteResponse response)
    {
    }

    public void OnInviteRefuse(OnInviteRefuseResponse response)
    {
    }

    public virtual void OnLeaveRoom()
    {
    }

    public virtual void OnLockRoom()
    {
    }

    public virtual void OnJoinRoom(OnJoinRoomResponse response)
    {
    }

    public virtual void OnRefuse()
    {
        _log("OnRefuse");
    }

    public virtual void OnStartBattle()
    {
        _log(TextResourceTable.Get("[LocalRaidRoomManagementBattleStartMessage]"));
        IsBattleStarted = true;
    }

    public virtual void OnUpdateRoom(OnUpdateRoomResponse response)
    {
    }

    public virtual void OnUpdatePartyCount(OnUpdatePartyCountResponse response)
    {
    }
}

public class LocalRaidJoinRoomReceiver : LocalRaidBaseReceiver
{
    private readonly TimeSpan _maxWait;
    private Stopwatch? _inRoomSw;

    /// <summary>进房后计时器；用于检测房主挂机不开战的死房。</summary>
    public Stopwatch? InRoomSw
    {
        get => Volatile.Read(ref _inRoomSw);
        set => Volatile.Write(ref _inRoomSw, value);
    }

    public LocalRaidJoinRoomReceiver(OrtegaMagicOnionClient ortegaMagicOnionClient, Action<string> log, CancellationToken cancellationToken, TimeSpan? maxWait = null)
        : base(ortegaMagicOnionClient, log, cancellationToken)
    {
        _maxWait = maxWait is { TotalSeconds: > 0 } w ? w : TimeSpan.FromMinutes(2);
    }

    protected override void OnRoundReset()
    {
        InRoomSw = null;
    }

    public override void OnStartBattle()
    {
        InRoomSw = null;
        base.OnStartBattle();
    }

    public override async void OnDisbandRoom()
    {
        InRoomSw = null;
        _log(TextResourceTable.GetErrorCodeMessage(ClientErrorCode.LocalRaidDismissedRoom));
        _startSw.Restart();
        await RetryJoin();
    }

    public override async void OnLeaveRoom()
    {
        InRoomSw = null;
        _startSw.Restart();
        await RetryJoin();
    }

    public override async void OnRefuse()
    {
        InRoomSw = null;
        _log(TextResourceTable.Get("[LocalRaidRoomRefuseReceiveDialogMessage]"));
        _startSw.Restart();
        await RetryJoin();
    }

    private async Task RetryJoin()
    {
        try
        {
            await Task.Delay(3000, CancellationToken);
            if (IsNoRemainingChallenges || IsMaxTimeExceeded || !_startSw.IsRunning) return;
            _log(TextResourceTable.Get("[LocalRaidRoomSearchButtonJoinRandomRoom]"));
            _ortegaMagicOnionClient.SendLocalRaidJoinRandomRoom(QuestId);
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
        catch (Exception e)
        {
            _log(e.Message);
        }
    }

    public override void OnJoinRoom(OnJoinRoomResponse response)
    {
        if (CancellationToken.IsCancellationRequested) return;
        _startSw.Stop();
        IsMaxTimeExceeded = false;
        InRoomSw = Stopwatch.StartNew();
        _ortegaMagicOnionClient.SendLocalRaidReady(true);
    }

    public override async void OnError(ErrorCode errorCode)
    {
        if (CancellationToken.IsCancellationRequested) return;
        _log($"OnError: {TextResourceTable.GetErrorCodeMessage(errorCode)}");
        try
        {
            switch (errorCode)
            {
                case ErrorCode.MagicOnionLocalRaidJoinRandomRoomNotExistRoom:
                    if (!_startSw.IsRunning) break;
                    if (_startSw.Elapsed > _maxWait)
                    {
                        IsMaxTimeExceeded = true;
                        _startSw.Stop();
                        break;
                    }

                    await RetryJoin();
                    break;
                case ErrorCode.MagicOnionLocalRaidInviteNoRemainingChallenges:
                case ErrorCode.MagicOnionLocalRaidJoinRoomNoRemainingChallenges:
                case ErrorCode.MagicOnionLocalRaidOpenRoomNoRemainingChallenges:
                case ErrorCode.MagicOnionLocalRaidJoinFriendRoomNoRemainingChallenges:
                case ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges:
                    IsNoRemainingChallenges = true;
                    break;
            }
        }
        catch (Exception e)
        {
            _log(e.Message);
        }
    }
}

public class LocalRaidCreateRoomReceiver : LocalRaidBaseReceiver
{
    private readonly int _waitSeconds;
    private readonly object _loopLock = new();
    private volatile LocalRaidPartyInfo _party;
    private bool _loopRunning;
    private bool _pendingStart;

    public LocalRaidCreateRoomReceiver(OrtegaMagicOnionClient ortegaMagicOnionClient, Action<string> log, int waitSeconds, CancellationToken cancellationToken) : base(ortegaMagicOnionClient, log,
        cancellationToken)
    {
        _waitSeconds = waitSeconds;
    }

    public override void OnJoinRoom(OnJoinRoomResponse response)
    {
        StartBattle(response.LocalRaidPartyInfo);
    }

    // 每次加入/更新房间只刷新最新的队伍信息，同一时间只跑一个开战循环；避免退出窗口漏掉新事件
    private void StartBattle(LocalRaidPartyInfo partyInfo)
    {
        if (partyInfo == null) return;
        _party = partyInfo;
        lock (_loopLock)
        {
            if (_loopRunning)
            {
                _pendingStart = true;
                return;
            }
            _loopRunning = true;
            _pendingStart = false;
        }
        _ = StartBattleLoop();
    }

    private async Task StartBattleLoop()
    {
        while (true)
        {
            try
            {
                var roomId = _party.RoomId;
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _waitSeconds)), CancellationToken);
                while (!IsBattleStarted && !CancellationToken.IsCancellationRequested)
                {
                    var party = _party;
                    if (party.RoomId != roomId)
                    {
                        // 换了新房间，重新等待
                        roomId = party.RoomId;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _waitSeconds)), CancellationToken);
                        continue;
                    }

                    if (!IsBattleStarted && party.IsReady) _ortegaMagicOnionClient.SendLocalRaidStartBattle();

                    await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
                }
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                _log(e.Message);
            }

            lock (_loopLock)
            {
                if (_pendingStart && !IsBattleStarted && !CancellationToken.IsCancellationRequested)
                {
                    _pendingStart = false;
                    continue;
                }
                _loopRunning = false;
                return;
            }
        }
    }

    public override void OnUpdateRoom(OnUpdateRoomResponse response)
    {
        StartBattle(response.LocalRaidPartyInfo);
    }

    public override void OnError(ErrorCode errorCode)
    {
        _log($"OnError: {TextResourceTable.GetErrorCodeMessage(errorCode)}");
        switch (errorCode)
        {
            case ErrorCode.MagicOnionLocalRaidJoinRandomRoomNotExistRoom:
            case ErrorCode.MagicOnionLocalRaidInviteNoRemainingChallenges:
            case ErrorCode.MagicOnionLocalRaidJoinRoomNoRemainingChallenges:
            case ErrorCode.MagicOnionLocalRaidOpenRoomNoRemainingChallenges:
            case ErrorCode.MagicOnionLocalRaidJoinFriendRoomNoRemainingChallenges:
            case ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges:
                IsNoRemainingChallenges = true;
                break;
        }
    }
}
