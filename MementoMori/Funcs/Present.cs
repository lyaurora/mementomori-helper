using MementoMori.Exceptions;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share.Data.ApiInterface.Equipment;
using MementoMori.Ortega.Share.Data.ApiInterface.Item;
using MementoMori.Ortega.Share.Data.ApiInterface.Present;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Item.Model;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    private const double UseOverLimitItemRate = 0.3;

    public async Task PresentReceiveItem()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var getListResponse = await GetResponse<GetListRequest, GetListResponse>(new GetListRequest {LanguageType = LanguageType.zhTW});
            // the list also holds received presents; only the ones still waiting need room
            var pending = getListResponse.userPresentDtoInfos.Where(d => !d.IsReceived).ToList();
            if (pending.Count > 0)
            {
                // use over limit items before receiving
                // only QuestQuickTicket and Equipment are considered; a present that another full item (coins, perfume, chests)
                // holds back stays stuck whatever is used, so its items must not trigger a clean-up on every run
                foreach (var presentItem in pending.Where(p => !HeldBackByOtherItem(p)).SelectMany(d => d.ItemList).GroupBy(d => new {d.Item.ItemType, d.Item.ItemId}))
                {
                    try
                    {
                        if (presentItem.Key.ItemType == ItemType.QuestQuickTicket)
                        {
                            var count = UserSyncData.GetUserItemCount(presentItem.Key.ItemType, presentItem.Key.ItemId);
                            var itemMb = ItemTable.GetByItemTypeAndItemId(presentItem.Key.ItemType, presentItem.Key.ItemId);
                            var maxItemCount = itemMb?.MaxItemCount ?? 0;
                            // tickets turn into their rewards, so make room for everything waiting, at least the usual share
                            var excess = count + presentItem.Sum(d => d.Item.ItemCount) - maxItemCount;
                            var useCount = (int) Math.Min(count, Math.Max(excess, Math.Floor(maxItemCount * UseOverLimitItemRate)));
                            if (maxItemCount <= 0 || excess <= 0 || useCount <= 0) continue;

                            var name = TextResourceTable.Get(itemMb.NameKey);
                            log($"{ResourceStrings.UseOverLimitItem}: {name}×{useCount}, {count}/{maxItemCount}");
                            var response = await GetResponse<UseAutoBattleRewardItemRequest, UseAutoBattleRewardItemResponse>(new UseAutoBattleRewardItemRequest
                            {
                                ItemType = (QuestQuickTicketType) itemMb.ItemId,
                                UseCount = useCount
                            });
                            response.RewardItemList.PrintUserItems(log);
                        }
                        else if (presentItem.Key.ItemType == ItemType.Equipment)
                        {
                            var count = UserSyncData.GetUserItemCount(presentItem.Key.ItemType, presentItem.Key.ItemId);
                            var maxItemCount = 999;
                            if (count < maxItemCount) continue;

                            var name = ItemUtil.GetItemName(presentItem.Key.ItemType, presentItem.Key.ItemId);
                            var useCount = (int) Math.Floor(maxItemCount * UseOverLimitItemRate);
                            log($"{ResourceStrings.UseOverLimitItem}: {name}×{useCount}, {count}/{maxItemCount}");
                            var response = await GetResponse<CastRequest, CastResponse>(new CastRequest {UserEquipment = new UserEquipment(presentItem.Key.ItemId, useCount)});
                            response.ResultItemList.PrintUserItems(log);
                        }
                    }
                    catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                    {
                        // a failed clean-up must not keep the other presents from being received
                        log(e.Message);
                    }
                }

                try
                {
                    var resp = await GetResponse<ReceiveItemRequest, ReceiveItemResponse>(new ReceiveItemRequest {LanguageType = NetworkManager.LanguageType});
                    log($"{TextResourceTable.Get("[MyPagePresentBoxButtonTitle]")} {TextResourceTable.Get("[MyPagePresentBoxButtonAllReceive]")}");
                    resp.ResultItems.Select(d => d.Item).PrintUserItems(log);
                }
                catch (ApiErrorException e) when (e.ErrorCode == ErrorCode.PresentReceiveOverLimitCountPresent)
                {
                    log(e.Message);
                    // receive one by one
                    foreach (var present in pending)
                    {
                        try
                        {
                            var resp = await GetResponse<ReceiveItemRequest, ReceiveItemResponse>(
                                new ReceiveItemRequest {LanguageType = NetworkManager.LanguageType, PresentGuid = present.Guid});
                            resp.ResultItems.Select(d => d.Item).PrintUserItems(log);
                        }
                        catch (ApiErrorException e2) when (e2.ErrorCode == ErrorCode.PresentReceiveOverLimitCountPresent)
                        {
                            // name what is stuck (one line per present): it is lost when the present expires unless room is made in time
                            log($"{e2.Message}: {present.Title} ({string.Join(", ", present.ItemList.Select(d => $"{ItemUtil.GetItemName(d.Item.ItemType, d.Item.ItemId)}×{d.Item.ItemCount}"))})");
                        }
                    }
                }
            }
            else
                log(TextResourceTable.GetErrorCodeMessage(ErrorCode.PresentReceiveAlreadyReceivedPresent));
        });

        bool HeldBackByOtherItem(UserPresentDtoInfo present) => present.ItemList.Any(d =>
        {
            var (type, id) = (d.Item.ItemType, d.Item.ItemId);
            if (type is ItemType.QuestQuickTicket or ItemType.Equipment) return false;
            var cap = type == ItemType.TreasureChest ? TreasureChestTable.GetById(id)?.MaxItemCount ?? 0 : ItemTable.GetByItemTypeAndItemId(type, id)?.MaxItemCount ?? 0;
            return cap > 0 && UserSyncData.GetUserItemCount(type, id) + d.Item.ItemCount > cap;
        });
    }
}