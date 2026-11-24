using MementoMori.Exceptions;
using MementoMori.Ortega.Share.Data.ApiInterface.EventPortal;
using MementoMori.Ortega.Share.Data.ApiInterface.LoginBonus;
using MementoMori.Ortega.Share.Data.EventPortal;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    private async Task<List<EventPortalBannerInfo>> GetEventPortalBannersAsync()
    {
        var banners = new List<EventPortalBannerInfo>();
        var icons = Mypage?.MypageInfo?.MypageIconInfos;
        if (icons == null) return banners;

        var portalTypes = icons
            .Select(d => d.TransferDetailInfo?.TransferSpotType)
            .Where(t => t is TransferSpotType.EventPortal or TransferSpotType.SubEventPortal)
            .Select(t => t!.Value)
            .Distinct()
            .ToList();

        foreach (var portalType in portalTypes)
        {
            try
            {
                var portal = await GetResponse<GetEventPortalRequest, GetEventPortalResponse>(new GetEventPortalRequest { TransferSpotType = portalType });
                if (portal.InsideEventPortalBannerInfo != null)
                    banners.AddRange(portal.InsideEventPortalBannerInfo.Where(b => b != null && b.IsOpen));
                if (portal.OutsideEventPortalBannerInfo != null)
                    banners.AddRange(portal.OutsideEventPortalBannerInfo
                        .Select(o => o?.EventPortalBannerInfo)
                        .Where(b => b != null && b.IsOpen)!);
            }
            catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
            {
                AddLog(e.Message);
            }
        }

        return banners;
    }

    public async Task GetLoginBonus()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await GetMonthlyLoginBonusInfo();
            // 游戏内每日 04:00 跨日：00:00-03:59 属于前一日
            var day = (TimeManager.ServerNow - TimeSpan.FromHours(4)).Day;
            var receivedDaily = false;
            if (MonthlyLoginBonusInfo?.ReceivedDailyRewardDayList != null && !MonthlyLoginBonusInfo.ReceivedDailyRewardDayList.Contains(day))
            {
                var bonus = await GetResponse<ReceiveDailyLoginBonusRequest, ReceiveDailyLoginBonusResponse>(new ReceiveDailyLoginBonusRequest {ReceiveDay = day});
                log($"{TextResourceTable.Get("[MyPageButtonLoginBonusLabel]")}:");
                bonus.RewardItemList.PrintUserItems(log);
                receivedDaily = true;
            }
            else
                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.LoginBonusAlreadyReceivedDailyReward));

            if (receivedDaily) await GetMonthlyLoginBonusInfo();

            var monthlyLoginBonusMb = MonthlyLoginBonusInfo == null ? null : MonthlyLoginBonusTable.GetById(MonthlyLoginBonusInfo.MonthlyLoginBonusId);
            var monthlyLoginBonusRewardListMb = monthlyLoginBonusMb == null ? null : MonthlyLoginBonusRewardListTable.GetById(monthlyLoginBonusMb.RewardListId);
            var receivedCountBonus = false;
            if (monthlyLoginBonusRewardListMb?.LoginCountRewardList != null && MonthlyLoginBonusInfo?.ReceivedDailyRewardDayList != null && MonthlyLoginBonusInfo.ReceivedLoginCountRewardDayList != null)
            {
                foreach (var loginCountRewardInfo in monthlyLoginBonusRewardListMb.LoginCountRewardList)
                    // 登录次数达到 && 没有领取过
                {
                    if (loginCountRewardInfo.DayCount <= MonthlyLoginBonusInfo.ReceivedDailyRewardDayList.Count &&
                        !MonthlyLoginBonusInfo.ReceivedLoginCountRewardDayList.Contains(loginCountRewardInfo.DayCount))
                    {
                        var resp = await GetResponse<ReceiveLoginCountBonusRequest, ReceiveLoginCountBonusResponse>(new ReceiveLoginCountBonusRequest
                        {
                            ReceiveDayCount = loginCountRewardInfo.DayCount
                        });
                        log($"{TextResourceTable.Get("[LoginBonusCountFormat]", loginCountRewardInfo.DayCount, 30)}");
                        resp.RewardItemList.PrintUserItems(log);
                        receivedCountBonus = true;
                    }
                }
            }

            if (receivedCountBonus) await GetMonthlyLoginBonusInfo();

            // 周年庆等经常同时开好几个限时登录奖励（可能在主页图标、EventPortal 内或 ReceivableLimitedLoginBonusId 中），每个都领；单个失败不影响其他
            var portalBanners = await GetEventPortalBannersAsync();
            var limitedLoginBonusIds = (Mypage?.MypageInfo?.MypageIconInfos ?? [])
                .Where(d => d.TransferDetailInfo?.TransferSpotType == TransferSpotType.LimitedLoginBonus)
                .Select(d => d.TransferDetailInfo.NumberInfo1)
                .Concat(portalBanners
                    .Where(b => b.TransferDetailInfo?.TransferSpotType == TransferSpotType.LimitedLoginBonus)
                    .Select(b => b.TransferDetailInfo.NumberInfo1))
                .Concat(Mypage is { ReceivableLimitedLoginBonusId: > 0 } ? [Mypage.ReceivableLimitedLoginBonusId] : [])
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            foreach (var limitedLoginBonusId in limitedLoginBonusIds)
            {
                var limitedLoginBonusMb = LimitedLoginBonusTable.GetById(limitedLoginBonusId);
                if (limitedLoginBonusMb == null) continue;
                var loginBonusRewardListMb = LimitedLoginBonusRewardListTable.GetById(limitedLoginBonusMb.RewardListId);
                if (loginBonusRewardListMb == null) continue;
                var name = TextResourceTable.Get(limitedLoginBonusMb.TitleTextKey);
                try
                {
                    var limitedLoginBonusInfoResponse = await GetResponse<GetLimitedLoginBonusInfoRequest, GetLimitedLoginBonusInfoResponse>(new GetLimitedLoginBonusInfoRequest
                    {
                        LimitedLoginBonusId = limitedLoginBonusId
                    });

                    foreach (var dailyLimitedLoginBonusItem in loginBonusRewardListMb.DailyRewardList ?? [])
                    {
                        if (limitedLoginBonusInfoResponse.ReceivedDateList?.Contains(dailyLimitedLoginBonusItem.Date) == true) continue;
                        if (limitedLoginBonusInfoResponse.TotalLoginCount < dailyLimitedLoginBonusItem.Date) continue;
                        log(name);
                        var resp = await GetResponse<ReceiveDailyLimitedLoginBonusRequest, ReceiveDailyLimitedLoginBonusResponse>(new ReceiveDailyLimitedLoginBonusRequest
                        {
                            LimitedLoginBonusId = limitedLoginBonusId, ReceiveDate = dailyLimitedLoginBonusItem.Date
                        });
                        resp.RewardItemList.PrintUserItems(log);
                    }

                    if (loginBonusRewardListMb.ExistSpecialReward && loginBonusRewardListMb.SpecialRewardItem != null &&
                        limitedLoginBonusInfoResponse.TotalLoginCount >= loginBonusRewardListMb.SpecialRewardItem.Date &&
                        !limitedLoginBonusInfoResponse.IsReceivedSpecialReward)
                    {
                        log(name);
                        var response = await GetResponse<ReceiveSpecialLimitedLoginBonusRequest, ReceiveSpecialLimitedLoginBonusResponse>(new ReceiveSpecialLimitedLoginBonusRequest
                        {
                            LimitedLoginBonusId = limitedLoginBonusId
                        });
                        response.RewardItemList.PrintUserItems(log);
                    }
                }
                catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                {
                    log($"{name}: {e.Message}");
                }
            }
        });
    }

    public async Task GetMonthlyLoginBonusInfo()
    {
        var response = await GetResponse<GetMonthlyLoginBonusInfoRequest, GetMonthlyLoginBonusInfoResponse>(new GetMonthlyLoginBonusInfoRequest());
        MonthlyLoginBonusInfo = response;
    }
}