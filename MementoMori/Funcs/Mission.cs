using MementoMori.Exceptions;
using MementoMori.Ortega.Share.Data.ApiInterface.Guild;
using MementoMori.Ortega.Share.Data.ApiInterface.Mission;
using MementoMori.Ortega.Share.Data.ApiInterface.Ranking;
using MementoMori.Ortega.Share.Data.Mission;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    // 主页图标 → 任务组：只请求当前开放的组（关闭的组会让整个请求报错）
    private static readonly Dictionary<TransferSpotType, MissionGroupType> MissionIconGroups = new()
    {
        [TransferSpotType.EventMission] = MissionGroupType.Limited,
        [TransferSpotType.CollabMission] = MissionGroupType.Collab,
        [TransferSpotType.ComebackMission] = MissionGroupType.Comeback,
        [TransferSpotType.BeginnerMission] = MissionGroupType.Beginner,
        [TransferSpotType.NewCharacterMission] = MissionGroupType.NewCharacter,
        [TransferSpotType.PanelMission] = MissionGroupType.Panel,
        [TransferSpotType.PopularityVote] = MissionGroupType.PopularityVote,
        [TransferSpotType.BookSort] = MissionGroupType.BookSort,
        [TransferSpotType.RentalRaid] = MissionGroupType.RentalRaid,
    };

    // 面板 3x3 连线对应的格子下标
    private static readonly Dictionary<BingoType, int[]> BingoLines = new()
    {
        [BingoType.UpperRow] = [0, 1, 2], [BingoType.CenterRow] = [3, 4, 5], [BingoType.LowerRow] = [6, 7, 8],
        [BingoType.LeftColumn] = [0, 3, 6], [BingoType.CenterColumn] = [1, 4, 7], [BingoType.RightColumn] = [2, 5, 8],
    };

    public async Task GetMissionInfo()
    {
        var missionGroupTypes = new List<MissionGroupType> {MissionGroupType.Daily, MissionGroupType.Weekly, MissionGroupType.Main};
        var response1 = await GetResponse<GetGuildIdRequest, GetGuildIdResponse>(new GetGuildIdRequest());
        if (response1.GuildId > 0)
        {
            var currentGuildMissionMb = GuildMissionTable.GetCurrentGuildMissionMB(TimeManager.ServerNow);
            if (currentGuildMissionMb != null) missionGroupTypes.Add(MissionGroupType.Guild);

            var guildTowerEventMb = GuildTowerEventTable.GetByInTime(TimeManager.IsInTime);
            if (guildTowerEventMb != null) missionGroupTypes.Add(MissionGroupType.GuildTower);
        }

        foreach (var icon in Mypage.MypageInfo.MypageIconInfos)
        {
            if (icon.TransferDetailInfo != null && MissionIconGroups.TryGetValue(icon.TransferDetailInfo.TransferSpotType, out var group) && !missionGroupTypes.Contains(group))
                missionGroupTypes.Add(group);
        }

        try
        {
            var response = await GetResponse<GetMissionInfoRequest, GetMissionInfoResponse>(new GetMissionInfoRequest {TargetMissionGroupList = missionGroupTypes});
            MissionInfoDict = response.MissionInfoDict;
        }
        catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
        {
            // 有组未开放时整个请求失败：逐组请求，跳过失败的组
            var dict = new Dictionary<MissionGroupType, MissionInfo>();
            foreach (var group in missionGroupTypes)
            {
                try
                {
                    var response = await GetResponse<GetMissionInfoRequest, GetMissionInfoResponse>(new GetMissionInfoRequest {TargetMissionGroupList = [group]});
                    foreach (var (groupType, info) in response.MissionInfoDict ?? []) dict[groupType] = info;
                }
                catch (ApiErrorException ex) when (ex.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                {
                }
            }

            MissionInfoDict = dict;
        }
    }

    public async Task CompleteMissions()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await GetMissionInfo();
            var missionIds = new List<long>();
            var claimedIds = new HashSet<long>();
            foreach (var (missionGroupType, missionInfo) in MissionInfoDict)
            {
                if (missionInfo.UserMissionDtoInfoDict == null) continue;
                if (missionGroupType == MissionGroupType.Panel)
                {
                    // 前一张面板全部完成后才能领下一张
                    foreach (var sheetType in new[] {MissionType.PanelSheet1, MissionType.PanelSheet2, MissionType.PanelSheet3})
                    {
                        if (!missionInfo.UserMissionDtoInfoDict.TryGetValue(sheetType, out var sheet)) break;
                        missionIds.AddRange(sheet.SelectMany(x => x.GetNotReceivedIdList()));
                        await RewardMission();
                        if (sheet.Any(d => d.Any(MissionStatusType.Locked) || d.Any(MissionStatusType.Progress))) break;
                    }

                    await ReceivePanelBingo(missionInfo);
                }
                else
                {
                    missionIds.AddRange(missionInfo.UserMissionDtoInfoDict.Values.SelectMany(d => d.SelectMany(x => x.GetNotReceivedIdList())));
                    await RewardMission();
                }
            }


            async Task RewardMission()
            {
                if (missionIds.Count == 0) return;

                try
                {
                    var rewardMissionResponse = await GetResponse<RewardMissionRequest, RewardMissionResponse>(new RewardMissionRequest {TargetMissionIdList = missionIds});
                    claimedIds.UnionWith(missionIds);
                    rewardMissionResponse.RewardInfo.ItemList.PrintUserItems(log);
                    rewardMissionResponse.RewardInfo.CharacterList.PrintCharacterDtos(log);
                }
                catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                {
                    // 一组失败不影响其他组
                    log(e.Message);
                }

                missionIds.Clear();
            }

            // 面板连线奖励：一行/一列 3 个任务都已领取后才能领
            async Task ReceivePanelBingo(MissionInfo missionInfo)
            {
                var done = missionInfo.UserMissionDtoInfoDict.Values.SelectMany(d => d)
                    .SelectMany(d => d.MissionStatusHistory.TryGetValue(MissionStatusType.Received, out var ids) ? ids : [])
                    .Concat(claimedIds).ToHashSet();
                var known = missionInfo.UserMissionDtoInfoDict.Values.SelectMany(d => d).SelectMany(d => d.MissionStatusHistory.Values.SelectMany(x => x)).ToHashSet();
                foreach (var userPanel in missionInfo.UserPanelMissionDtoInfoList ?? [])
                {
                    // 任务 ID 只属于一期面板：9 个格子都出现在服务器返回里的那张就是当前面板
                    var sheet = PanelMissionTable.GetArray().SelectMany(d => d.PanelMissionSheetInfoList ?? [])
                        .FirstOrDefault(d => d.SheetNo == userPanel.SheetNo && d.MissionIdList?.Count == 9 && d.MissionIdList.All(known.Contains));
                    if (sheet == null) continue;
                    foreach (var (bingoType, cells) in BingoLines)
                    {
                        if (userPanel.ReceivedBingoTypeList?.Contains(bingoType) == true || !cells.All(c => done.Contains(sheet.MissionIdList[c]))) continue;
                        try
                        {
                            var response = await GetResponse<ReceivePanelMissionBingoRewardRequest, ReceivePanelMissionBingoRewardResponse>(
                                new ReceivePanelMissionBingoRewardRequest {SheetNo = userPanel.SheetNo, BingoType = bingoType});
                            response.RewardInfo?.ItemList.PrintUserItems(log);
                            response.RewardInfo?.CharacterList.PrintCharacterDtos(log);
                        }
                        catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                        {
                            log(e.Message);
                        }
                    }
                }
            }
        });
    }

    public async Task RewardMissonActivity()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await GetMissionInfo();
            foreach (var pair in MissionInfoDict)
            {
                if (pair.Value.UserMissionActivityDtoInfo == null) continue;

                // 客户端改为一次领取最高可领档位，并自动包含此前未领的较低档位
                var maxRequiredCount = pair.Value.UserMissionActivityDtoInfo.RewardStatusDict
                    .Where(d => d.Value == MissionActivityRewardStatusType.NotReceived)
                    .Select(d => TotalActivityMedalRewardTable.GetById(d.Key)?.RequiredActivityMedalCount ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();
                if (maxRequiredCount <= 0) continue;

                log(string.Format(ResourceStrings.RewardMissionMsg, pair.Key, maxRequiredCount));
                var response = await GetResponse<RewardMissionActivityRequest, RewardMissionActivityResponse>(new RewardMissionActivityRequest
                    {MissionGroupType = pair.Key, RequiredCount = maxRequiredCount});
                response.RewardInfo.ItemList.PrintUserItems(log);
                response.RewardInfo.CharacterList.PrintCharacterDtos(log);
            }
        });
    }

    public async Task ReceiveAchievementReward()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await GetResponse<GetPlayerRankingRequest, GetPlayerRankingResponse>(new GetPlayerRankingRequest());
            foreach (var (rankingDataType, mbId) in UserSyncData.ReceivableAchieveRankingRewardIdMap)
            {
                foreach (var mb in AchieveRankingRewardTable.GetByRankingDataType(rankingDataType))
                {
                    if (mb.Id > mbId || UserSyncData.ReceivedAchieveRankingRewardIdList.Contains(mb.Id)) continue;

                    var response = await GetResponse<ReceiveAchieveRankingRewardRequest, ReceiveAchieveRankingRewardResponse>(new ReceiveAchieveRankingRewardRequest
                        {AchieveRankingRewardMBId = mb.Id});
                    log($"{TextResourceTable.Get(mb.AchieveTargetDescriptionKey)}");
                    response.RewardItemList.PrintUserItems(log);
                }
            }
        });
    }
}