using MementoMori.Exceptions;
using MementoMori.Ortega.Share.Data.ApiInterface.LoginBonus;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task GetLoginBonus()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await GetMonthlyLoginBonusInfo();
            var day = TimeManager.ServerNow.Day;
            if (!MonthlyLoginBonusInfo.ReceivedDailyRewardDayList.Contains(day))
            {
                if (TimeManager.ServerNow.Hour >= 4)
                {
                    var bonus = await GetResponse<ReceiveDailyLoginBonusRequest, ReceiveDailyLoginBonusResponse>(new ReceiveDailyLoginBonusRequest {ReceiveDay = day});
                    log($"{TextResourceTable.Get("[MyPageButtonLoginBonusLabel]")}:\n");
                    bonus.RewardItemList.PrintUserItems(log);
                }

                await GetMonthlyLoginBonusInfo();
            }
            else
                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.LoginBonusAlreadyReceivedDailyReward));

            var monthlyLoginBonusMb = MonthlyLoginBonusTable.GetById(MonthlyLoginBonusInfo.MonthlyLoginBonusId);
            var monthlyLoginBonusRewardListMb = MonthlyLoginBonusRewardListTable.GetById(monthlyLoginBonusMb.RewardListId);
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
                }
            }

            await GetMonthlyLoginBonusInfo();

            // 周年庆等经常同时开好几个限时登录奖励，每个都领；单个失败不影响其他
            var limitedLoginBonusIds = Mypage.MypageInfo.MypageIconInfos
                .Where(d => d.TransferDetailInfo.TransferSpotType == TransferSpotType.LimitedLoginBonus)
                .Select(d => d.TransferDetailInfo.NumberInfo1).Distinct().ToList();
            foreach (var limitedLoginBonusId in limitedLoginBonusIds)
            {
                var limitedLoginBonusMb = LimitedLoginBonusTable.GetById(limitedLoginBonusId);
                var loginBonusRewardListMb = LimitedLoginBonusRewardListTable.GetById(limitedLoginBonusId);
                if (limitedLoginBonusMb == null || loginBonusRewardListMb == null) continue;
                var name = TextResourceTable.Get(limitedLoginBonusMb.TitleTextKey);
                try
                {
                    var limitedLoginBonusInfoResponse = await GetResponse<GetLimitedLoginBonusInfoRequest, GetLimitedLoginBonusInfoResponse>(new GetLimitedLoginBonusInfoRequest
                    {
                        LimitedLoginBonusId = limitedLoginBonusId
                    });

                    foreach (var dailyLimitedLoginBonusItem in loginBonusRewardListMb.DailyRewardList)
                    {
                        if (limitedLoginBonusInfoResponse.ReceivedDateList.Contains(dailyLimitedLoginBonusItem.Date)) continue;
                        if (limitedLoginBonusInfoResponse.TotalLoginCount < dailyLimitedLoginBonusItem.Date) continue;
                        log(name);
                        var resp = await GetResponse<ReceiveDailyLimitedLoginBonusRequest, ReceiveDailyLimitedLoginBonusResponse>(new ReceiveDailyLimitedLoginBonusRequest
                        {
                            LimitedLoginBonusId = limitedLoginBonusId, ReceiveDate = dailyLimitedLoginBonusItem.Date
                        });
                        resp.RewardItemList.PrintUserItems(log);
                    }

                    if (loginBonusRewardListMb.ExistSpecialReward && limitedLoginBonusInfoResponse.TotalLoginCount >= loginBonusRewardListMb.SpecialRewardItem.Date &&
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