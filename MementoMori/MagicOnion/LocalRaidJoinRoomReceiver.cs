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
    public long QuestId
    {
        get => _questId;
        set
        {
            _questId = value;
            // A new search can use the same quest as the previous battle.
            _startSw.Restart();
            IsMaxTimeExceeded = false;
        }
    }
    public bool IsNoRemainingChallenges { get; protected set; }
    public bool IsMaxTimeExceeded { get; protected set; }

    public bool IsBattleStarted { get; set; }

    public virtual void OnError(ErrorCode errorCode)
    {
        _log($"OnError: {TextResourceTable.GetErrorCodeMessage(errorCode)}");
    }

    public virtual void OnGetRoomList(OnGetRoomListResponse response)
    {
        _log("OnGetRoomList");
    }

    public virtual void OnDisbandRoom()
    {
        _log("OnDisbandRoom");
    }

    public virtual void OnInvite(OnInviteResponse response)
    {
        _log("OnInvite");
    }

    public void OnInviteRefuse(OnInviteRefuseResponse response)
    {
        _log("OnInviteRefuse");
    }

    public virtual void OnLeaveRoom()
    {
        _log("OnLeaveRoom");
    }

    public virtual void OnLockRoom()
    {
        _log("OnLockRoom");
    }

    public virtual void OnJoinRoom(OnJoinRoomResponse response)
    {
        _log("OnJoinRoom");
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
        _log("OnUpdateRoom");
    }

    public virtual void OnUpdatePartyCount(OnUpdatePartyCountResponse response)
    {
        _log("OnUpdatePartyCount");
    }
}

public class LocalRaidJoinRoomReceiver : LocalRaidBaseReceiver
{
    public LocalRaidJoinRoomReceiver(OrtegaMagicOnionClient ortegaMagicOnionClient, Action<string> log, CancellationToken cancellationToken) : base(ortegaMagicOnionClient, log, cancellationToken)
    {
    }


    public override async void OnDisbandRoom()
    {
        _log(TextResourceTable.GetErrorCodeMessage(ClientErrorCode.LocalRaidDismissedRoom));
        _startSw.Restart();
        await RetryJoin();
    }

    public override async void OnLeaveRoom()
    {
        _startSw.Restart();
        await RetryJoin();
    }

    public override async void OnRefuse()
    {
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
        _log("OnJoinRoom");
        _startSw.Stop();
        IsMaxTimeExceeded = false;
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
                    if (_startSw.Elapsed > TimeSpan.FromMinutes(2))
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
    private volatile LocalRaidPartyInfo _party;
    private int _loopRunning;

    public LocalRaidCreateRoomReceiver(OrtegaMagicOnionClient ortegaMagicOnionClient, Action<string> log, int waitSeconds, CancellationToken cancellationToken) : base(ortegaMagicOnionClient, log,
        cancellationToken)
    {
        _waitSeconds = waitSeconds;
    }

    public override void OnJoinRoom(OnJoinRoomResponse response)
    {
        _log("OnJoinRoom");
        StartBattle(response.LocalRaidPartyInfo);
    }

    // 每次加入/更新房间只刷新最新的队伍信息，同一时间只跑一个开战循环
    private void StartBattle(LocalRaidPartyInfo partyInfo)
    {
        _party = partyInfo;
        if (Interlocked.Exchange(ref _loopRunning, 1) == 1) return;
        _ = StartBattleLoop();
    }

    private async Task StartBattleLoop()
    {
        try
        {
            var roomId = _party.RoomId;
            await Task.Delay(TimeSpan.FromSeconds(_waitSeconds), CancellationToken);
            while (!IsBattleStarted && !CancellationToken.IsCancellationRequested)
            {
                var party = _party;
                if (party.RoomId != roomId)
                {
                    // 换了新房间，重新等待
                    roomId = party.RoomId;
                    await Task.Delay(TimeSpan.FromSeconds(_waitSeconds), CancellationToken);
                    continue;
                }

                if (party.IsReady) _ortegaMagicOnionClient.SendLocalRaidStartBattle();

                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
            }
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
        catch (Exception e)
        {
            _log(e.Message);
        }
        finally
        {
            Volatile.Write(ref _loopRunning, 0);
        }
    }

    public override void OnUpdateRoom(OnUpdateRoomResponse response)
    {
        _log("OnUpdateRoom");
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
