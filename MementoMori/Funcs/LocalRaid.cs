using MementoMori.Exceptions;
using MementoMori.MagicOnion;
using MementoMori.Ortega.Common.Enums;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Share.Data.ApiInterface.LocalRaid;
using MementoMori.Ortega.Share.Data.Item;
using Microsoft.Extensions.Logging;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task AutoLocalRaid()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var rewardItems = PlayerOption.LocalRaid.RewardItems;
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

            var createRoom = _playersOption.Value.TryGetValue(NetworkManager.PlayerId, out var c) && c.LocalRaid.SelfCreateRoom;

            // 先查询幻影神殿信息：若挑战次数已用尽或无可用任务，直接返回而不建立 MagicOnion 连接
            var prefetchedInfo = await GetResponse<GetLocalRaidInfoRequest, GetLocalRaidInfoResponse>(new GetLocalRaidInfoRequest());
            var initialRemaining = prefetchedInfo.UserSyncData?.LocalRaidChallengeCount ?? UserSyncData?.LocalRaidChallengeCount;
            if (initialRemaining is <= 0)
            {
                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges));
                return;
            }
            if (GetQuestId(prefetchedInfo) == 0) return;

            while (!token.IsCancellationRequested)
            {
                var soloLosses = 0;
                var timedOut = false;
                OrtegaMagicOnionClient client = null;
                LocalRaidBaseReceiver localRaidReceiver = null;
                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var keepaliveTask = Task.CompletedTask;
                try
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        client = NetworkManager.GetOnionClient();
                        localRaidReceiver = createRoom ? new LocalRaidCreateRoomReceiver(client, log, PlayerOption.LocalRaid.WaitSeconds, connectionCts.Token) : new LocalRaidJoinRoomReceiver(client, log, connectionCts.Token);
                        client.SetupLocalRaid(localRaidReceiver, localRaidReceiver);
                        try
                        {
                            await client.Connect(token);
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            timeout.CancelAfter(TimeSpan.FromMinutes(1));
                            while (client.GetState() != HubClientState.Ready) await Task.Delay(1000, timeout.Token);
                            break;
                        }
                        catch (Exception) when (!token.IsCancellationRequested && attempt < 2)
                        {
                            await client.DisposeAsync();
                            client = null;
                            await Task.Delay(2000, token);
                        }
                    }
                    keepaliveTask = client.KeepAlive(connectionCts.Token);
                    while (!token.IsCancellationRequested)
                    {
                        var localRaidInfoResponse = prefetchedInfo ?? await GetResponse<GetLocalRaidInfoRequest, GetLocalRaidInfoResponse>(new GetLocalRaidInfoRequest());
                        prefetchedInfo = null;
                        var remaining = localRaidInfoResponse.UserSyncData?.LocalRaidChallengeCount ?? UserSyncData?.LocalRaidChallengeCount;
                        if (remaining is <= 0)
                        {
                            log(TextResourceTable.GetErrorCodeMessage(ErrorCode.MagicOnionLocalRaidJoinRandomRoomNoRemainingChallenges));
                            return;
                        }
                        var questId = GetQuestId(localRaidInfoResponse);
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

                        // 两种模式共用的总超时：被踢、房主挂机、未处理的错误都不会无限等待并占住账号锁
                        var roundStart = DateTime.UtcNow;
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(1000, token);
                            if (keepaliveTask.IsFaulted) await keepaliveTask;
                            if (localRaidReceiver.IsNoRemainingChallenges) return;
                            if (localRaidReceiver.IsMaxTimeExceeded) goto Done;
                            // host mode waits WaitSeconds before starting, so give it that on top
                            var limit = TimeSpan.FromMinutes(5) + (createRoom ? TimeSpan.FromSeconds(Math.Max(0, PlayerOption.LocalRaid.WaitSeconds)) : TimeSpan.Zero);
                            if (!localRaidReceiver.IsBattleStarted && DateTime.UtcNow - roundStart > limit)
                            {
                                log(ResourceStrings.LocalRaidWaitTimeout);
                                if (createRoom) return;
                                // join mode: fall back to creating our own room, like IsMaxTimeExceeded
                                timedOut = true;
                                goto Done;
                            }

                            if (localRaidReceiver.IsBattleStarted)
                            {
                                await Task.Delay(2000, token);
                                try
                                {
                                    var battleResultResponse = await GetResponse<GetLocalRaidBattleResultRequest, GetLocalRaidBattleResultResponse>(new GetLocalRaidBattleResultRequest());
                                    var isWinAttacker = battleResultResponse.BattleResult.SimulationResult.BattleEndInfo.IsWinAttacker();
                                    log(isWinAttacker ? TextResourceTable.Get("[LocalRaidBattleWinMessage]") : TextResourceTable.Get("[LocalRaidBattleLoseMessage]"));
                                    battleResultResponse.BattleRewardResult?.FixedItemList?.PrintUserItems(log);
                                    battleResultResponse.BattleRewardResult?.DropItemList?.PrintUserItems(log);
                                    soloLosses = isWinAttacker ? 0 : soloLosses + 1;
                                    if (createRoom && soloLosses >= 3)
                                    {
                                        log(string.Format(ResourceStrings.LocalRaidSoloLossesStop, soloLosses));
                                        return;
                                    }
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
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
                if (createRoom || !(localRaidReceiver.IsMaxTimeExceeded || timedOut) || token.IsCancellationRequested) return;
                log(ResourceStrings.LocalRaidNoTeamCreateOwn);
                createRoom = true;
            }

            long GetQuestId(GetLocalRaidInfoResponse response)
            {
                // 只选开放的任务（活动期间只选活动任务），筛完为空时退回原列表
                var openIds = response.OpenEventLocalRaidQuestIds is { Count: > 0 } ? response.OpenEventLocalRaidQuestIds : response.OpenLocalRaidQuestIds;
                var localRaidQuestMbs = response.LocalRaidQuestInfos?.Where(d => openIds?.Contains(d.Id) == true).ToList() ?? [];
                if (localRaidQuestMbs.Count == 0) localRaidQuestMbs = response.LocalRaidQuestInfos ?? [];
                if (localRaidQuestMbs.Count == 0) return 0;
                if (rewardItems.Count == 0) return localRaidQuestMbs.OrderByDescending(d => d.Level).First().Id;

                return localRaidQuestMbs.Select(d =>
                {
                    var count = 0D;
                    // if cleared dict contains the quest id, it means the quest has been cleared before
                    var isFirst = response.ClearCountDict == null || !response.ClearCountDict.ContainsKey(d.Id);
                    foreach (var rewardItem in rewardItems)
                    {
                        foreach (var reward in d.FixedBattleRewards ?? [])
                        {
                            if (reward.IsEqual(rewardItem.ItemType, rewardItem.ItemId)) count += reward.ItemCount / GetMaxItemCount(rewardItem) * rewardItem.Weight;
                        }

                        if (!isFirst) continue;
                        foreach (var reward in d.FirstBattleRewards ?? [])
                        {
                            if (reward.IsEqual(rewardItem.ItemType, rewardItem.ItemId)) count += reward.ItemCount / GetMaxItemCount(rewardItem) * rewardItem.Weight;
                        }
                    }

                    return new {quest = d, count};
                }).OrderByDescending(d => d.count).ThenByDescending(d => d.quest.Level).First().quest.Id;
            }

            double GetMaxItemCount(IUserItem userItem)
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
        });
    }
}
