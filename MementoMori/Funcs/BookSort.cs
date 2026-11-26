using MementoMori.Exceptions;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.ApiInterface.BookSort;
using MementoMori.Ortega.Share.Data.ApiInterface.Mission;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Master.Data;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    private const int BookSortGridWidth = 7;
    private const int BookSortGridHeight = 5;
    private const int BookSortTotalCells = BookSortGridWidth * BookSortGridHeight;

    // 书库大扫除活动自动清扫（活动期外静默返回；支持奖励层自动选最高档、出钥匙+大奖升层、大扫除道具最优覆盖、普通掸子批量开与顺手领任务）
    public async Task AutoBookSort()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var bookSortEventMb = BookSortEventTable.GetArray().FirstOrDefault(e => TimeManager.IsInTime(e));
            if (bookSortEventMb == null)
            {
                log(ResourceStrings.BookSortNotActive);
                return;
            }

            if (OpenContentTable.GetByOpenCommandType(OpenCommandType.BookSort) is { } oc
                && (UserSyncData.UserBattleBossDtoInfo?.BossClearMaxQuestId ?? 0) < oc.OpenContentValue)
            {
                return;
            }

            log(ResourceStrings.BookSortStarting);
            var maxClearQuestId = UserSyncData.UserBattleBossDtoInfo?.BossClearMaxQuestId ?? 0;
            var bookSortOption = PlayerOption.BookSort ?? new BookSortOption();

            for (var iteration = 0; iteration < 50 && !token.IsCancellationRequested; iteration++)
            {
                BookSortGetInfoResponse infoResponse;
                try
                {
                    infoResponse = await GetResponse<BookSortGetInfoRequest, BookSortGetInfoResponse>(new BookSortGetInfoRequest(), token);
                }
                catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                {
                    log(e.Message);
                    break;
                }

                var sync = infoResponse.BookSortSyncData;
                if (sync == null) break;

                // 1. 奖励层未选大奖时，按玩家配置（首选高价值 / 指定项 / 暂停等待手动）处理
                var isBonusFloor = bookSortEventMb.IsBonusFloor(sync.CurrentFloor)
                                   || (bookSortEventMb.BonusFloorRepeat <= 0
                                       && (bookSortEventMb.BonusFloorList == null || bookSortEventMb.BonusFloorList.Count == 0)
                                       && sync.CurrentFloor > 0 && sync.CurrentFloor % 5 == 0);
                if (isBonusFloor && sync.SelectedBonusFloorRewardIndex == -1)
                {
                    var rewardMb = BookSortBonusFloorRewardTable.GetByFloor(sync.CurrentFloor);
                    var chosenIndex = ResolveBonusFloorRewardIndex(rewardMb, maxClearQuestId, bookSortOption);
                    if (chosenIndex == BookSortOption.PauseForManual)
                    {
                        log(string.Format(ResourceStrings.BookSortPausedForBonusReward, sync.CurrentFloor));
                        break;
                    }
                    if (chosenIndex >= 0)
                    {
                        var label = FormatBonusRewardLabel(rewardMb, chosenIndex);
                        log(string.Format(ResourceStrings.BookSortRewardFloor, sync.CurrentFloor, label));
                        await GetResponse<BookSortSelectBonusFloorRewardRequest, BookSortSelectBonusFloorRewardResponse>(
                            new BookSortSelectBonusFloorRewardRequest { BonusFloorRewardIndex = chosenIndex }, token);
                        continue;
                    }
                }

                // 2. 已同时找到钥匙和大奖，或本层全部开完 -> 直接升层
                var unlocked = sync.UnlockedGridCellInfoList?.Select(x => x.GridCellIndex).ToHashSet() ?? [];
                if (sync.IsUnlockedWinAndKeyGridCell())
                {
                    log(string.Format(ResourceStrings.BookSortKeyAndRewardFound, sync.CurrentFloor));
                    await GetResponse<BookSortUpFloorRequest, BookSortUpFloorResponse>(new BookSortUpFloorRequest(), token);
                    continue;
                }

                if (unlocked.Count >= BookSortTotalCells)
                {
                    log(string.Format(ResourceStrings.BookSortFloorCleared, sync.CurrentFloor));
                    await GetResponse<BookSortUpFloorRequest, BookSortUpFloorResponse>(new BookSortUpFloorRequest(), token);
                    continue;
                }

                // 3. 如启用多格道具，优先用覆盖收益 >= 2 的大扫除道具；同收益优先小面积避免浪费
                var unlockItems = UserSyncData.UserItemDtoInfo?
                    .Where(x => x.ItemType == ItemType.BookSortGridCellUnlockItem && x.ItemCount > 0)
                    .ToList() ?? [];

                var acted = false;
                if (bookSortOption.UseLargeUnlockItems)
                {
                    var bestMove = PickBestLargeUnlockMove(unlockItems, unlocked);
                    if (bestMove is { Yield: >= 2 } move)
                    {
                        log(string.Format(ResourceStrings.BookSortUseBigItem, move.ItemId, move.GridCellIndex, move.Yield));
                        var resp = await GetResponse<BookSortUnlockGridCellRequest, BookSortUnlockGridCellResponse>(
                            new BookSortUnlockGridCellRequest { GridCellUnlockItemId = move.ItemId, GridCellIndex = move.GridCellIndex }, token);
                        PrintBookSortRewards(resp.UnlockGridCellResultList, log);
                        acted = true;
                    }
                }

                // 4. 否则使用普通掸子（ItemId == 1）批量清扫
                if (!acted && unlockItems.Any(x => x.ItemId == 1 && x.ItemCount > 0))
                {
                    log(ResourceStrings.BookSortUseDuster);
                    var resp = await GetResponse<BookSortBulkUnlockGridCellRequest, BookSortBulkUnlockGridCellResponse>(
                        new BookSortBulkUnlockGridCellRequest(), token);
                    PrintBookSortRewards(resp.UnlockGridCellResultList, log);
                    acted = true;
                }

                // 5. 无可用道具时尝试领取书库任务奖励，领到新道具则继续循环
                if (!acted)
                {
                    if (await TryClaimBookSortMissionsAsync(log, token)) continue;
                    log(ResourceStrings.BookSortNothingLeft);
                    break;
                }
            }

            log(ResourceStrings.BookSortFinished);
        });
    }

    private async Task<bool> TryClaimBookSortMissionsAsync(Action<string> log, CancellationToken token)
    {
        try
        {
            var resp = await GetResponse<GetMissionInfoRequest, GetMissionInfoResponse>(
                new GetMissionInfoRequest { TargetMissionGroupList = [MissionGroupType.BookSort] }, token);
            var ids = new List<long>();
            if (resp.MissionInfoDict != null)
            {
                foreach (var group in resp.MissionInfoDict.Values)
                {
                    if (group.UserMissionDtoInfoDict == null) continue;
                    foreach (var list in group.UserMissionDtoInfoDict.Values)
                    {
                        foreach (var mission in list)
                        {
                            if (mission.MissionStatusHistory != null
                                && mission.MissionStatusHistory.TryGetValue(MissionStatusType.NotReceived, out var ready))
                            {
                                ids.AddRange(ready);
                            }
                        }
                    }
                }
            }

            if (ids.Count == 0) return false;
            var reward = await GetResponse<RewardMissionRequest, RewardMissionResponse>(
                new RewardMissionRequest { TargetMissionIdList = ids }, token);
            log(string.Format(ResourceStrings.BookSortMissionRewardsClaimed, ids.Count));
            reward.RewardInfo?.ItemList?.PrintUserItems(log);
            return true;
        }
        catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
        {
            log(e.Message);
            return false;
        }
    }

    private static void PrintBookSortRewards(List<UnlockGridCellResult>? results, Action<string> log)
    {
        if (results == null) return;
        var items = results.Where(r => r.RewardUserItemList != null).SelectMany(r => r.RewardUserItemList).ToList();
        if (items.Count > 0) items.PrintUserItems(log);
    }

    internal readonly record struct BookSortMove(long ItemId, int GridCellIndex, int Yield, int Area);

    internal static int ResolveBonusFloorRewardIndex(BookSortBonusFloorRewardMB? rewardMb, long maxClearQuestId, BookSortOption? option)
    {
        if (rewardMb == null) return -1;
        var eligible = rewardMb.GetSelectableIndexList(maxClearQuestId);
        if (eligible.Count == 0) return -1;

        var configured = option?.BonusFloorRewardChoices != null
                         && option.BonusFloorRewardChoices.TryGetValue(rewardMb.Id, out var saved)
            ? saved
            : BookSortOption.AutoFirstEligible;

        if (configured == BookSortOption.PauseForManual) return BookSortOption.PauseForManual;
        if (configured >= 0 && eligible.Contains(configured)) return configured;

        // 默认选首个符合主线进度的选项（如白金券 / 命运券 / 潜能宝珠，避免落到末尾的普通封印钥匙）
        return eligible[0];
    }

    private static string FormatBonusRewardLabel(BookSortBonusFloorRewardMB? rewardMb, int index)
    {
        var items = rewardMb?.SelectItemsList != null && index >= 0 && index < rewardMb.SelectItemsList.Count
            ? rewardMb.SelectItemsList[index]?.ItemList
            : null;
        if (items is not { Count: > 0 }) return $"#{index}";
        return string.Join(" + ", items.Select(x => $"{ItemUtil.GetItemName(x.ItemType, x.ItemId)}×{x.ItemCount}"));
    }

    internal static int SelectBestBonusFloorRewardIndex(IReadOnlyList<BookSortBonusFloorSelectItems>? selectItems, long maxClearQuestId)
    {
        if (selectItems == null || selectItems.Count == 0) return -1;
        for (var i = 0; i < selectItems.Count; i++)
        {
            var item = selectItems[i];
            if (item == null) continue;
            if (item.StartMaxClearQuestId <= maxClearQuestId && (item.EndMaxClearQuestId == 0 || item.EndMaxClearQuestId >= maxClearQuestId))
            {
                return i;
            }
        }
        return -1;
    }

    internal static BookSortMove? PickBestLargeUnlockMove(IEnumerable<IUserItem> unlockItems, IReadOnlySet<int> unlockedIndexes)
    {
        var largeItems = unlockItems
            .Where(x => x.ItemType == ItemType.BookSortGridCellUnlockItem && x.ItemCount > 0 && x.ItemId > 1)
            .OrderByDescending(x => GetBookSortItemShape(x.ItemId).Count)
            .ToList();
        if (largeItems.Count == 0) return null;

        BookSortMove? best = null;
        foreach (var item in largeItems)
        {
            var shape = GetBookSortItemShape(item.ItemId);
            var area = shape.Count;
            for (var c = 0; c < BookSortTotalCells; c++)
            {
                var centerCol = c % BookSortGridWidth;
                var centerRow = c / BookSortGridWidth;
                var yield = 0;
                foreach (var (cOff, rOff) in shape)
                {
                    var col = centerCol + cOff;
                    var row = centerRow + rOff;
                    if (col is >= 0 and < BookSortGridWidth && row is >= 0 and < BookSortGridHeight
                        && !unlockedIndexes.Contains(row * BookSortGridWidth + col))
                    {
                        yield++;
                    }
                }

                if (yield <= 0) continue;
                if (best == null || yield > best.Value.Yield || (yield == best.Value.Yield && area < best.Value.Area))
                {
                    best = new BookSortMove(item.ItemId, c, yield, area);
                }
            }
        }

        return best;
    }

    internal static IReadOnlyList<(int ColOffset, int RowOffset)> GetBookSortItemShape(long itemId) => itemId switch
    {
        2 => [(0, 0), (1, 0)], // 2x1
        3 => [(0, 0), (1, 0), (2, 0), (3, 0)], // 4x1
        4 => [(0, 0), (0, 1), (0, 2), (0, 3)], // 1x4
        5 => [(0, 0), (1, 0), (0, 1), (1, 1)], // 2x2
        6 => [(0, 0), (-1, 0), (-2, 0), (1, 0), (2, 0), (0, -1), (0, -2), (0, 1), (0, 2)], // Cross 9
        7 => [(0, 0), (-1, -1), (-2, -2), (-1, 1), (-2, 2), (1, -1), (2, -2), (1, 1), (2, 2)], // DiagonalCross 9
        8 => [(-1, -1), (0, -1), (1, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1)], // 3x3
        _ => [(0, 0)]
    };
}
