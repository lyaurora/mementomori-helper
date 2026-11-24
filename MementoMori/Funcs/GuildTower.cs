using MementoMori.Exceptions;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share.Data.ApiInterface.Guild;
using MementoMori.Ortega.Share.Data.ApiInterface.GuildTower;
using MementoMori.Ortega.Share.Data.Item;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task AutoGuildTower()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var guildTowerOption = PlayerOption.GuildTower;

            var guildTowerEventMb = GuildTowerEventTable.GetByInTime(TimeManager.IsInTime);
            if (guildTowerEventMb == null)
            {
                log(TextResourceTable.Get("[GuildTowerEndEventDialogMessage]"));
                return;
            }

            // get guild tower info
            var guildId = await GetResponse<GetGuildIdRequest, GetGuildIdResponse>(new GetGuildIdRequest());
            if (guildId.GuildId == 0)
            {
                log(TextResourceTable.Get("[RankingNotGuild]"));
                return;
            }

            var guildTowerInfo = await GetResponse<GetGuildTowerInfoRequest, GetGuildTowerInfoResponse>(new GetGuildTowerInfoRequest());
            if (!guildTowerInfo.IsAlreadyEntryToday)
            {
                if (!guildTowerOption.AutoEntry) return;

                var guids = UserSyncData.UserCharacterDtoInfos.OrderByDescending(d => BattlePowerCalculatorUtil.GetUserCharacterBattlePower(UserId, d.Guid)).Take(20).Select(d => d.Guid).ToList();
                var entryCharacterResponse =
                    await GetResponse<EntryCharacterRequest, EntryCharacterResponse>(new EntryCharacterRequest
                        {CharacterGuidList = guids, GuildTowerEntryType = GuildTowerEntryType.Entry, IsContinueEntry = false});
                log(TextResourceTable.Get("[GuildTowerEntryToastMessage]"));
                guildTowerInfo = await GetResponse<GetGuildTowerInfoRequest, GetGuildTowerInfoResponse>(new GetGuildTowerInfoRequest());
            }

            // chanllenge guild tower for each job
            if (guildTowerOption.AutoChallenge)
            {
                while (guildTowerInfo.TodayWinCount < 3 && !token.IsCancellationRequested)
                {
                    var nextFloor = GuildTowerFloorTable.GetById(guildTowerInfo.CurrentFloorMBId);
                    // var nextFloor = GuildTowerFloorTable.GetByEventIdAndFloor(guildTowerFloorMb.EventNo, guildTowerFloorMb.FloorCount + 1);
                    if (nextFloor == null || nextFloor.SelectableDifficultyList is not { Count: > 0 }) break;

                    var entryCharacters = (guildTowerInfo.GuildTowerEntryCharacterList ?? [])
                        .Select(d => new { tc = d, cha = UserSyncData.GetUserCharacterInfoByUserCharacterGuid(d.CharacterGuid) })
                        .Where(d => d.cha != null && CharacterTable.GetById(d.cha.CharacterId) != null)
                        .ToList();
                    if (entryCharacters.Count == 0) break;

                    // get the least used job
                    var jobFlag = entryCharacters
                        .GroupBy(d => CharacterTable.GetById(d.cha.CharacterId).JobFlags)
                        .OrderBy(d => d // order by used count ascending
                                .Select(x => new {cha = x, bp = BattlePowerCalculatorUtil.GetUserCharacterBattlePower(UserId, x.cha)})
                                .OrderByDescending(x => x.bp) // get the most powerful character
                                .Take(5) // get the top 5
                                .Sum(x => x.cha.tc.TodayUseCount) // get used count
                        ).First().Key;

                    var isWin = false;
                    var isFloorClearedByOther = false;
                    // from max difficulty to min difficulty
                    for (var i = nextFloor.SelectableDifficultyList.Count - 1; i >= 0 && !token.IsCancellationRequested; i--)
                    {
                        var guids = entryCharacters
                            .Select(d => d.cha)
                            .Where(d => CharacterTable.GetById(d.CharacterId).JobFlags == jobFlag)
                            .OrderByDescending(d => BattlePowerCalculatorUtil.GetUserCharacterBattlePower(UserId, d))
                            .Take(5)
                            .Select(d => d.Guid)
                            .ToList();
                        if (guids.Count == 0) break;

                        var battleRequest = new BattleRequest
                        {
                            CharacterGuidList = guids, Difficulty = nextFloor.SelectableDifficultyList[i], GuildTowerFloor = nextFloor.FloorCount
                        };

                        var retryCount = Math.Max(1, guildTowerOption.AutoChallengeRetryCount);
                        retryCount = Math.Min(retryCount, 1000);
                        for (var j = 0; j < retryCount && !token.IsCancellationRequested; j++)
                        {
                            try
                            {
                                var battleResponse = await GetResponse<BattleRequest, BattleResponse>(battleRequest);
                                var msg =
                                    $"{TextResourceTable.Get("[GuildTowerStageFormat]", nextFloor.FloorCount)} {TextResourceTable.Get("[GuildTowerDifficultyLabel]")} {nextFloor.SelectableDifficultyList[i]}";
                                if (battleResponse.BattleResult.SimulationResult.BattleEndInfo.IsWinAttacker())
                                {
                                    isWin = true;
                                    log($"{TextResourceTable.Get("[GuildTowerTitle]")} {msg} {TextResourceTable.Get("[LocalRaidBattleWinMessage]")}");
                                    battleResponse.BattleRewardResult.DropItemList.PrintUserItems(log);
                                    battleResponse.BattleRewardResult.FixedItemList.PrintUserItems(log);
                                    break;
                                }

                                log($"{TextResourceTable.Get("[GuildTowerTitle]")} {msg} {TextResourceTable.Get("[LocalRaidBattleLoseMessage]")}");
                            }
                            catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                            {
                                if (e.ErrorCode == ErrorCode.GuildTowerAlreadyClearFloor)
                                {
                                    log(e.Message);
                                    isFloorClearedByOther = true;
                                    break;
                                }

                                // 次数不足、不在挑战时间等：停止挑战，继续后面的强化和楼层奖励
                                log(e.Message);
                                goto ChallengeDone;
                            }
                        }

                        if (isWin || isFloorClearedByOther) break;
                    }

                    // All difficulties on this floor failed: stop instead of looping forever.
                    if (!isWin && !isFloorClearedByOther) break;

                    guildTowerInfo = await GetResponse<GetGuildTowerInfoRequest, GetGuildTowerInfoResponse>(new GetGuildTowerInfoRequest());
                }

                ChallengeDone: ;
            }

            // job reinforcement
            if (guildTowerOption.AutoReinforcement)
            {
                var getReinforcementJobDataResponse = await GetResponse<GetReinforcementJobDataRequest, GetReinforcementJobDataResponse>(new GetReinforcementJobDataRequest
                {
                    JobLevelMap = guildTowerInfo.ReinforcementJobDataList.ToDictionary(d => d.JobFlags, d => d.Level)
                });
                foreach (var guildTowerReinforcementJobData in getReinforcementJobDataResponse.ReinforcementJobDataList)
                {
                    var reinforcementJobData = guildTowerReinforcementJobData;
                    while (!token.IsCancellationRequested && reinforcementJobData != null)
                    {
                        var guildTowerReinforcementJobLevelMb =
                            GuildTowerReinforcementJobLevelTable.GetByEventNoJobFlagsLevel(guildTowerEventMb.EventNo, reinforcementJobData.JobFlags, reinforcementJobData.Level);
                        // 职业满级时没有对应行或材料为 null
                        if (guildTowerReinforcementJobLevelMb?.RequiredMaterialList is not {Count: > 0}) break;

                        var candidate = guildTowerReinforcementJobLevelMb.RequiredMaterialList
                            .OrderByDescending(d => d.ItemCount)
                            .Select(item =>
                            {
                                var owned = UserSyncData.GetUserItemCount(item.ItemType, item.ItemId);
                                var consumed = reinforcementJobData.GetConsumedMaterialCount(item.ItemType, item.ItemId);
                                var toConsume = Math.Min(item.ItemCount - consumed, owned);
                                return (item, consumed, toConsume);
                            })
                            .FirstOrDefault(x => x.toConsume > 0);
                        if (candidate.item == null) break;

                        log($"{TextResourceTable.Get("[GuildTowerJobReinforceLabel]")} {TextResourceTable.Get(reinforcementJobData.JobFlags)}");
                        try
                        {
                            var prevLevel = reinforcementJobData.Level;
                            var reinforceJobResponse = await GetResponse<ReinforceJobRequest, ReinforceJobResponse>(new ReinforceJobRequest
                            {
                                JobFlags = reinforcementJobData.JobFlags,
                                Level = reinforcementJobData.Level,
                                MaterialItemList = [new UserItem {ItemType = candidate.item.ItemType, ItemId = candidate.item.ItemId, ItemCount = candidate.toConsume}]
                            });
                            reinforcementJobData = reinforceJobResponse.ReinforcementJobData;
                            if (reinforcementJobData == null) break;
                            if (reinforcementJobData.Level == prevLevel
                                && reinforcementJobData.GetConsumedMaterialCount(candidate.item.ItemType, candidate.item.ItemId) <= candidate.consumed)
                                break;
                        }
                        catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                        {
                            // 一个职业失败不影响其他职业和楼层奖励
                            log(e.Message);
                            break;
                        }
                    }
                }
            }

            // receive floor reward
            if (guildTowerOption.AutoReceiveReward)
            {
                guildTowerInfo = await GetResponse<GetGuildTowerInfoRequest, GetGuildTowerInfoResponse>(new GetGuildTowerInfoRequest());
                var notRewardedIds = GuildTowerFloorTable.GetListByEventId(guildTowerEventMb.EventNo).Where(d =>
                        d.Id < guildTowerInfo.CurrentFloorMBId &&
                        d.FloorRewardList != null &&
                        d.FloorRewardList.Count > 0 &&
                        !UserSyncData.ReceivedGuildTowerFloorRewardIdList.Contains(d.Id))
                    .Select(d => d.Id).ToList();
                if (notRewardedIds.Count > 0)
                {
                    log($"{TextResourceTable.Get("[GuildTowerTitle]")} {TextResourceTable.Get("[GuildTowerStageRewardLabel]")}");
                    var receiveFloorRewardResponse = await GetResponse<ReceiveFloorRewardRequest, ReceiveFloorRewardResponse>(new ReceiveFloorRewardRequest {FloorRewardMBIdList = notRewardedIds});
                    receiveFloorRewardResponse.RewardItemList.PrintUserItems(log);
                }
            }
        });
    }
}