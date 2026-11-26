using MementoMori.Exceptions;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share.Data.ApiInterface.TowerBattle;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task AutoInfiniteTowerRequest(long targetStopLayer)
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            if (SelectedAutoTowerType == TowerType.None) return;

            var totalCount = 0;
            var winCount = 0;
            var errCount = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var towerBattleDtoInfo = UserSyncData.UserTowerBattleDtoInfos?.FirstOrDefault(d => d.TowerType == SelectedAutoTowerType);
                    if (towerBattleDtoInfo == null) break;
                    if (SelectedAutoTowerType != TowerType.Infinite && towerBattleDtoInfo.TodayClearNewFloorCount >= 10)
                    {
                        log($"{TextResourceTable.Get(SelectedAutoTowerType)} {TextResourceTable.Get("[ClientErrorMessage1700007]")}");
                        break;
                    }

                    var targetQuestId = towerBattleDtoInfo.MaxTowerBattleId + 1;
                    var bossQuickResponse = await GetResponse<StartRequest, StartResponse>(new StartRequest
                    {
                        TargetTowerType = SelectedAutoTowerType, TowerBattleQuestId = targetQuestId
                    });
                    var win = bossQuickResponse.BattleResult.SimulationResult.BattleEndInfo.IsWinAttacker();
                    totalCount++;
                    if (win) winCount++;

                    await _battleLogManager.SaveBattleLog(bossQuickResponse.BattleResult, $@"tower-{SelectedAutoTowerType}", bossQuickResponse.BattleResult.QuestId.ToString(),
                        $"tower-{SelectedAutoTowerType}-*lose");

                    var name = TextResourceTable.Get(SelectedAutoTowerType);
                    var result = win ? TextResourceTable.Get("[LocalRaidBattleWinMessage]") : TextResourceTable.Get("[LocalRaidBattleLoseMessage]");
                    var updatedClearCount = UserSyncData.UserTowerBattleDtoInfos?.FirstOrDefault(d => d.TowerType == SelectedAutoTowerType)?.TodayClearNewFloorCount ?? towerBattleDtoInfo.TodayClearNewFloorCount;

                    if (SelectedAutoTowerType == TowerType.Infinite)
                        log(string.Format(ResourceStrings.AutoTowerInfiniteExecMsg, name, targetQuestId, result, totalCount, winCount, errCount));
                    else
                        log(string.Format(ResourceStrings.AutoTowerElementExecMsg, name, targetQuestId, result, totalCount, winCount, errCount, updatedClearCount));

                    if (win && targetStopLayer > 0 && targetQuestId >= targetStopLayer) break;
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    log(e.Message);
                    errCount++;
                    if (errCount > Max_Err_Count)
                    {
                        log(string.Format(ResourceStrings.AutoBossErrorMessage, Max_Err_Count));
                        return;
                    }

                    // Re-login only after a session error (GetResponse cleared LoginOk); other API errors repeat, so stop.
                    // Another device took over: don't kick the player's phone.
                    if (e is ApiErrorException { ErrorCode: ErrorCode.CommonLoggedInAnotherDevice }) return;
                    if (!LoginOk) await AuthLogin(_lastPlayerDataInfo);
                    else if (e is ApiErrorException) return;
                }
            }
        });
    }

    public TowerType[] GetAvailableTower()
    {
        if (!LoginOk) return Array.Empty<TowerType>();

        foreach (var limitedEventMb in LimitedEventTable.GetArray().Where(d => d.LimitedEventType == LimitedEventType.ElementTowerAllRelease))
        {
            if (NetworkManager.TimeManager.IsInTime(limitedEventMb)) return new[] {TowerType.Infinite, TowerType.Blue, TowerType.Green, TowerType.Red, TowerType.Yellow};
        }

        var now = DateTimeOffset.UtcNow.ToOffset(TimeManager.DiffFromUtc) - TimeSpan.FromHours(4);
        var dayOfWeek = now.DayOfWeek;
        // SelectedAutoTowerType = TowerType.Infinite;
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => new[] {TowerType.Infinite, TowerType.Blue, TowerType.Green, TowerType.Red, TowerType.Yellow},
            DayOfWeek.Monday => new[] {TowerType.Infinite, TowerType.Blue},
            DayOfWeek.Tuesday => new[] {TowerType.Infinite, TowerType.Red},
            DayOfWeek.Wednesday => new[] {TowerType.Infinite, TowerType.Green},
            DayOfWeek.Thursday => new[] {TowerType.Infinite, TowerType.Yellow},
            DayOfWeek.Friday => new[] {TowerType.Infinite, TowerType.Blue, TowerType.Red},
            DayOfWeek.Saturday => new[] {TowerType.Infinite, TowerType.Yellow, TowerType.Green},
            _ => new[] {TowerType.Infinite}
        };
    }

    public async Task InfiniteTowerQuick()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            try
            {
                var tower = UserSyncData.UserTowerBattleDtoInfos?.FirstOrDefault(d => d.TowerType == TowerType.Infinite);
                if (tower == null || tower.MaxTowerBattleId <= 0) return;
                log($"{TextResourceTable.Get("[CommonHeaderTowerBattleLabel]")}:");

                // 每天 3 次免费，只扫当天剩余次数（避免已手动打过后再传 3 导致整次扫荡报错）
                var remaining = 3 - tower.TodayBattleCount;
                if (remaining <= 0)
                {
                    log(TextResourceTable.GetErrorCodeMessage(ErrorCode.TowerBattleNotEnoughChallengeCount));
                    return;
                }

                var diamondsBefore = UserSyncData.GetUserItemCount(ItemType.CurrencyFree, isAnyCurrency: true);
                if (IsBossBattleQuickAvailable)
                {
                    var bossQuickResponse = await GetResponse<TowerBattleQuickRequest, TowerBattleQuickResponse>(
                        new TowerBattleQuickRequest
                        {
                            TargetTowerType = TowerType.Infinite, TowerBattleQuestId = tower.MaxTowerBattleId, QuickCount = remaining
                        });
                    if (bossQuickResponse.BattleRewardResult != null)
                    {
                        bossQuickResponse.BattleRewardResult.FixedItemList.PrintUserItems(log);
                        bossQuickResponse.BattleRewardResult.DropItemList.PrintUserItems(log);
                    }
                }
                else
                {
                    for (var i = 0; i < remaining; i++)
                    {
                        var bossQuickResponse = await GetResponse<StartRequest, StartResponse>(
                            new StartRequest
                            {
                                TargetTowerType = TowerType.Infinite, TowerBattleQuestId = tower.MaxTowerBattleId
                            });
                        if (bossQuickResponse.BattleRewardResult != null)
                        {
                            bossQuickResponse.BattleRewardResult.FixedItemList.PrintUserItems(log);
                            bossQuickResponse.BattleRewardResult.DropItemList.PrintUserItems(log);
                        }
                    }
                }

                var spentDiamonds = diamondsBefore - UserSyncData.GetUserItemCount(ItemType.CurrencyFree, isAnyCurrency: true);
                if (spentDiamonds > 0) AddError(string.Format(ResourceStrings.TowerInfiniteSpentDiamonds, spentDiamonds));
            }
            catch (ApiErrorException e) when (e.ErrorCode == ErrorCode.TowerBattleNotEnoughChallengeCount)
            {
                log(e.Message);
            }
        });
    }
}