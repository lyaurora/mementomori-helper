using System.Diagnostics;
using MementoMori.Exceptions;
using MementoMori.MagicOnion;
using MementoMori.Ortega.Common.Enums;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Share.Data.ApiInterface.LocalRaid;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Data.LocalRaid;
using Microsoft.Extensions.Logging;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task AutoLocalRaid()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var localRaid = PlayerOption.LocalRaid;
            var rewardItems = localRaid.RewardItems;
            if (rewardItems.Count == 0) rewardItems = GameConfig.LocalRaid.RewardItems;
            if (rewardItems.Count == 0)
            {
                rewardItems =
                [
                    new GameConfig.WeightedItem(ItemType.ExchangePlaceItem, 4, 4), // 符石兑换券
                    new GameConfig.WeightedItem(ItemType.CharacterTrainingMaterial, 2, 3), // 潜能宝珠
                    new GameConfig.WeightedItem(ItemType.EquipmentReinforcementItem, 2, 2.5), // 强化秘药
                    new GameConfig.WeightedItem(ItemType.CharacterTrainingMaterial, 1, 2), // 经验珠
                    new GameConfig.WeightedItem(ItemType.EquipmentReinforcementItem, 1, 1) // 强化水
                ];
            }

            var createRoom = localRaid.SelfCreateRoom;
            var joinWaitMinutes = Math.Max(1, localRaid.JoinWaitMinutes);
            var lowerAfterMinutes = Math.Max(1, localRaid.LowerQuestAfterMinutes);
            var roomWaitMinutes = Math.Max(1, localRaid.RoomWaitMinutes);
            var maxTaskMinutes = Math.Max(1, localRaid.MaxTaskMinutes);
            var maxSoloLosses = Math.Max(0, localRaid.MaxSoloLosses);
            var joinWait = TimeSpan.FromMinutes(joinWaitMinutes);
            var lowerAfter = TimeSpan.FromMinutes(lowerAfterMinutes);
            var roomWait = TimeSpan.FromMinutes(roomWaitMinutes);
            var questLevel = localRaid.QuestLevel;
            var questStep = 0; // 0 = 起始关卡；DropToLowerQuest 时每步降一档

            // 先查询幻影神殿信息：若挑战次数已用尽或无可用任务，直接返回而不建立 MagicOnion 连接
            var prefetchedInfo = await GetResponse<GetLocalRaidInfoRequest, GetLocalRaidInfoResponse>(new GetLocalRaidInfoRequest(), token);
            var lastInfo = prefetchedInfo;
            var initialRemaining = prefetchedInfo.UserSyncData?.LocalRaidChallengeCount ?? UserSyncData?.LocalRaidChallengeCount;
            if (initialRemaining is <= 0)
            {
                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges));
                return;
            }
            if (SelectLocalRaidQuestId(prefetchedInfo, rewardItems, questLevel, questStep) == 0) return;

            using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            taskCts.CancelAfter(TimeSpan.FromMinutes(maxTaskMinutes));
            var taskToken = taskCts.Token;
            var joinSw = Stopwatch.StartNew();

            try
            {
                // 加入模式等不到房间：可选先降一级关卡再找 (DropToLowerQuest)，再可选自己开房单刷 (HostWhenNoRoom)，否则停止。
                // 自己开房连输 MaxSoloLosses 次：可选降一级关卡继续，否则停止。全部受 MaxTaskMinutes 限制。
                while (!taskToken.IsCancellationRequested)
                {
                    var soloLosses = 0;
                    var stalled = false;
                    var passTimedOut = false;
                    OrtegaMagicOnionClient? client = null;
                    LocalRaidBaseReceiver? localRaidReceiver = null;
                    using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(taskToken);
                    var keepaliveTask = Task.CompletedTask;
                    try
                    {
                        var remainingJoin = joinWait - joinSw.Elapsed;
                        if (remainingJoin < TimeSpan.FromMinutes(1)) remainingJoin = TimeSpan.FromMinutes(1);
                        var passWait = !createRoom && localRaid.DropToLowerQuest && HasLowerLocalRaidQuest(lastInfo, rewardItems, questLevel, questStep) && lowerAfter < remainingJoin
                            ? lowerAfter
                            : remainingJoin;

                        for (var attempt = 0; ; attempt++)
                        {
                            taskToken.ThrowIfCancellationRequested();
                            client = NetworkManager.GetOnionClient();
                            localRaidReceiver = createRoom
                                ? new LocalRaidCreateRoomReceiver(client, log, localRaid.WaitSeconds, connectionCts.Token)
                                : new LocalRaidJoinRoomReceiver(client, log, connectionCts.Token, passWait);
                            client.SetupLocalRaid(localRaidReceiver, localRaidReceiver);
                            try
                            {
                                await client.Connect(taskToken);
                                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(taskToken);
                                timeout.CancelAfter(TimeSpan.FromMinutes(1));
                                while (client.GetState() != HubClientState.Ready) await Task.Delay(1000, timeout.Token);
                                break;
                            }
                            catch (Exception) when (!taskToken.IsCancellationRequested && attempt < 2)
                            {
                                await client.DisposeAsync();
                                client = null;
                                await Task.Delay(2000, taskToken);
                            }
                        }

                        keepaliveTask = client.KeepAlive(connectionCts.Token);
                        while (!taskToken.IsCancellationRequested)
                        {
                            var localRaidInfoResponse = prefetchedInfo ?? await GetResponse<GetLocalRaidInfoRequest, GetLocalRaidInfoResponse>(new GetLocalRaidInfoRequest(), taskToken);
                            prefetchedInfo = null;
                            lastInfo = localRaidInfoResponse;
                            var remaining = localRaidInfoResponse.UserSyncData?.LocalRaidChallengeCount ?? UserSyncData?.LocalRaidChallengeCount;
                            if (remaining is <= 0)
                            {
                                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges));
                                return;
                            }

                            var questId = SelectLocalRaidQuestId(localRaidInfoResponse, rewardItems, questLevel, questStep);
                            if (questId == 0) return;
                            localRaidReceiver.QuestId = questId;
                            localRaidReceiver.IsBattleStarted = false;

                            if (createRoom)
                            {
                                log(TextResourceTable.Get("[LocalRaidRoomSearchButtonCreateRoom]"));
                                client.SendLocalRaidOpenRoom(LocalRaidRoomConditionsType.None, questId, 0, 0);
                            }
                            else
                            {
                                log(TextResourceTable.Get("[LocalRaidRoomSearchButtonJoinRandomRoom]"));
                                client.SendLocalRaidJoinRandomRoom(questId);
                            }

                            var roundStart = DateTime.UtcNow;
                            var passSw = Stopwatch.StartNew();
                            DateTime? leaveSentAt = null;
                            while (!taskToken.IsCancellationRequested)
                            {
                                await Task.Delay(1000, taskToken);
                                if (keepaliveTask.IsFaulted) await keepaliveTask;
                                if (localRaidReceiver.IsNoRemainingChallenges) return;
                                if (localRaidReceiver.IsMaxTimeExceeded) goto Done;

                                // 加入模式进房后一直不开战（房主挂机等）：退房重新寻找其他房间
                                if (localRaidReceiver is LocalRaidJoinRoomReceiver joined && !joined.IsBattleStarted)
                                {
                                    if (joined.InRoomSw is { } inRoom && inRoom.Elapsed > roomWait)
                                    {
                                        joined.InRoomSw = null;
                                        leaveSentAt = DateTime.UtcNow;
                                        log(string.Format(ResourceStrings.LocalRaidRoomStuck, roomWaitMinutes));
                                        client.SendLocalRaidLeaveRoom();
                                    }
                                    else if (leaveSentAt != null)
                                    {
                                        if (joined.IsSearching || joined.InRoomSw != null)
                                        {
                                            leaveSentAt = null;
                                        }
                                        else if (DateTime.UtcNow - leaveSentAt > TimeSpan.FromSeconds(30))
                                        {
                                            // 退房回调超时：重建连接避免死等
                                            stalled = true;
                                            goto Done;
                                        }
                                    }
                                    else if (joined.InRoomSw == null && passSw.Elapsed > passWait)
                                    {
                                        // 反复被踢/解散/退房时 _startSw 会被重置；用 passSw 兜底触发降级或开房
                                        passTimedOut = true;
                                        goto Done;
                                    }
                                }

                                // 自己开房单轮总超时，防止未处理异常导致无限等待
                                if (createRoom && !localRaidReceiver.IsBattleStarted)
                                {
                                    var hostLimit = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(Math.Max(0, localRaid.WaitSeconds));
                                    if (DateTime.UtcNow - roundStart > hostLimit)
                                    {
                                        log(ResourceStrings.LocalRaidWaitTimeout);
                                        return;
                                    }
                                }

                                if (localRaidReceiver.IsBattleStarted)
                                {
                                    if (localRaidReceiver is LocalRaidJoinRoomReceiver started) started.InRoomSw = null;
                                    joinSw.Restart();
                                    await Task.Delay(2000, taskToken);
                                    try
                                    {
                                        var battleResultResponse = await GetResponse<GetLocalRaidBattleResultRequest, GetLocalRaidBattleResultResponse>(new GetLocalRaidBattleResultRequest(), taskToken);
                                        var isWinAttacker = battleResultResponse.BattleResult.SimulationResult.BattleEndInfo.IsWinAttacker();
                                        log(isWinAttacker ? TextResourceTable.Get("[LocalRaidBattleWinMessage]") : TextResourceTable.Get("[LocalRaidBattleLoseMessage]"));
                                        battleResultResponse.BattleRewardResult?.FixedItemList?.PrintUserItems(log);
                                        battleResultResponse.BattleRewardResult?.DropItemList?.PrintUserItems(log);
                                        soloLosses = isWinAttacker ? 0 : soloLosses + 1;
                                        if (createRoom && maxSoloLosses > 0 && soloLosses >= maxSoloLosses)
                                        {
                                            if (localRaid.DropToLowerQuest && HasLowerLocalRaidQuest(localRaidInfoResponse, rewardItems, questLevel, questStep))
                                            {
                                                questStep++;
                                                soloLosses = 0;
                                                var nextId = SelectLocalRaidQuestId(localRaidInfoResponse, rewardItems, questLevel, questStep);
                                                log(string.Format(ResourceStrings.LocalRaidLowerQuest, FormatQuestLabel(localRaidInfoResponse, nextId)));
                                            }
                                            else
                                            {
                                                log(string.Format(ResourceStrings.LocalRaidSoloLossesStop, soloLosses));
                                                return;
                                            }
                                        }
                                    }
                                    catch (OperationCanceledException) when (taskToken.IsCancellationRequested) { throw; }
                                    catch (ApiErrorException e) when (e.ErrorCode is ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice) { throw; }
                                    catch (Exception e)
                                    {
                                        log(e.Message);
                                    }

                                    break;
                                }
                            }
                        }
                    }
                    finally
                    {
                        connectionCts.Cancel();
                        try { await keepaliveTask; }
                        catch (OperationCanceledException) when (connectionCts.IsCancellationRequested) { }
                        catch (Exception e)
                        {
                            _logger.LogWarning(e, "LocalRaid keepalive ended with error");
                        }
                        finally
                        {
                            if (client != null)
                            {
                                client.ClearLocalRaidReceiver();
                                await client.DisposeAsync();
                            }
                        }
                    }

                    Done:
                    if (createRoom || taskToken.IsCancellationRequested) return;
                    if (stalled) continue;
                    if (localRaidReceiver is not { IsMaxTimeExceeded: true } && !passTimedOut) return;

                    // 加入等超时：仍在总等待时间内且同类型有更低关卡 -> 降一级继续找
                    if (localRaid.DropToLowerQuest && joinSw.Elapsed < joinWait && HasLowerLocalRaidQuest(lastInfo, rewardItems, questLevel, questStep))
                    {
                        questStep++;
                        var nextId = SelectLocalRaidQuestId(lastInfo, rewardItems, questLevel, questStep);
                        log(string.Format(ResourceStrings.LocalRaidLowerQuest, FormatQuestLabel(lastInfo, nextId)));
                        continue;
                    }

                    // 未开启兜底开房时直接放弃（保留小号等待主号的行为）
                    if (!localRaid.HostWhenNoRoom)
                    {
                        log(ResourceStrings.LocalRaidWaitTimeout);
                        return;
                    }

                    log(ResourceStrings.LocalRaidNoTeamCreateOwn);
                    createRoom = true;
                }
            }
            catch (OperationCanceledException) when (taskCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                log(ResourceStrings.LocalRaidWaitTimeout);
            }
        });
    }

    internal static List<LocalRaidQuestInfo> GetOpenLocalRaidQuestPool(GetLocalRaidInfoResponse response)
    {
        // 只选开放的任务（活动期间只选活动任务），筛完为空时退回原列表
        var openIds = response.OpenEventLocalRaidQuestIds is { Count: > 0 } ? response.OpenEventLocalRaidQuestIds : response.OpenLocalRaidQuestIds;
        var pool = response.LocalRaidQuestInfos?.Where(d => openIds?.Contains(d.Id) == true).ToList() ?? [];
        if (pool.Count == 0) pool = response.LocalRaidQuestInfos?.ToList() ?? [];
        return pool;
    }

    internal static long SelectLocalRaidQuestId(GetLocalRaidInfoResponse response, IReadOnlyList<GameConfig.WeightedItem> rewardItems, int questLevel, int step)
    {
        var pool = GetOpenLocalRaidQuestPool(response);
        if (pool.Count == 0) return 0;
        var startId = GetStartQuestId(response, pool, rewardItems, questLevel);
        if (step <= 0) return startId;
        var lower = GetLowerQuestIds(pool, startId);
        return lower.Count == 0 ? startId : lower[Math.Min(step, lower.Count) - 1];
    }

    internal static bool HasLowerLocalRaidQuest(GetLocalRaidInfoResponse response, IReadOnlyList<GameConfig.WeightedItem> rewardItems, int questLevel, int step)
    {
        var pool = GetOpenLocalRaidQuestPool(response);
        if (pool.Count == 0) return false;
        var startId = GetStartQuestId(response, pool, rewardItems, questLevel);
        return GetLowerQuestIds(pool, startId).Count > step;
    }

    private static long GetStartQuestId(GetLocalRaidInfoResponse response, List<LocalRaidQuestInfo> pool, IReadOnlyList<GameConfig.WeightedItem> rewardItems, int questLevel)
    {
        if (questLevel > 0)
        {
            var pick = pool.Where(q => q.Level <= questLevel).OrderByDescending(q => q.Level).FirstOrDefault()
                       ?? pool.OrderBy(q => q.Level).FirstOrDefault();
            if (pick != null) return pick.Id;
        }

        if (rewardItems.Count == 0) return pool.OrderByDescending(d => d.Level).First().Id;

        return pool.Select(d =>
        {
            var count = 0D;
            var isFirst = response.ClearCountDict == null || !response.ClearCountDict.ContainsKey(d.Id);
            foreach (var rewardItem in rewardItems)
            {
                foreach (var reward in d.FixedBattleRewards ?? [])
                {
                    if (reward.IsEqual(rewardItem.ItemType, rewardItem.ItemId)) count += reward.ItemCount / GetMaxLocalRaidItemCount(rewardItem) * rewardItem.Weight;
                }

                if (!isFirst) continue;
                foreach (var reward in d.FirstBattleRewards ?? [])
                {
                    if (reward.IsEqual(rewardItem.ItemType, rewardItem.ItemId)) count += reward.ItemCount / GetMaxLocalRaidItemCount(rewardItem) * rewardItem.Weight;
                }
            }

            return new { quest = d, count };
        }).OrderByDescending(d => d.count).ThenByDescending(d => d.quest.Level).First().quest.Id;
    }

    private static List<long> GetLowerQuestIds(List<LocalRaidQuestInfo> pool, long startId)
    {
        var startQuest = pool.FirstOrDefault(q => q.Id == startId);
        if (startQuest == null) return [];
        return pool.Where(q => q.Level < startQuest.Level)
            .OrderByDescending(q => q.Level)
            .Select(q => q.Id)
            .ToList();
    }

    private static string FormatQuestLabel(GetLocalRaidInfoResponse response, long questId)
    {
        var quest = response.LocalRaidQuestInfos?.FirstOrDefault(q => q.Id == questId);
        return quest != null ? $"Lv.{quest.Level} (#{quest.Id})" : $"#{questId}";
    }

    private static double GetMaxLocalRaidItemCount(IUserItem userItem)
    {
        return (userItem.ItemType, userItem.ItemId) switch
        {
            (ItemType.CharacterTrainingMaterial, 1) => 241032D,
            (ItemType.EquipmentReinforcementItem, 1) => 296D,
            (ItemType.EquipmentReinforcementItem, 2) => 34D,
            (ItemType.CharacterTrainingMaterial, 2) => 378D,
            (ItemType.ExchangePlaceItem, 4) => 27D,
            _ => 99999999D
        };
    }
}
