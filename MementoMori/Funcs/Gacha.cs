using MementoMori.Exceptions;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share.Data.ApiInterface.Gacha;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Gacha;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task FreeGacha()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            HashSet<long> ignoredButtonId = [];
            while (await DoFreeGacha())
            {
            }

            log(ResourceStrings.NoAvailableGacha);

            async Task<bool> DoFreeGacha()
            {
                var gachaListResponse = await GetResponse<GetListRequest, GetListResponse>(new GetListRequest());
                foreach (var gachaCaseInfo in gachaListResponse.GachaCaseInfoList ?? [])
                {
                    var userItems = (UserSyncData.UserItemDtoInfo ?? []).ToList();
                    var autoConsumeItems = GameConfig.GachaConfig.AutoGachaConsumeUserItems ?? [];
                    var buttonInfo = gachaCaseInfo.GachaButtonInfoList?.OrderByDescending(d => d.LotteryCount)
                        .FirstOrDefault(d => !ignoredButtonId.Contains(d.GachaButtonId) && (IsFreeButton(d) || autoConsumeItems.Exists(consumeUserItem => CheckCount(d, userItems, consumeUserItem.ItemType, consumeUserItem.ItemId))));
                    if (buttonInfo == null) continue;

                    var consumeName = buttonInfo.ConsumeUserItem == null
                        ? TextResourceTable.Get("[CommonFreeLabel]")
                        : ItemTable.GetByItemTypeAndItemId(buttonInfo.ConsumeUserItem.ItemType, buttonInfo.ConsumeUserItem.ItemId) is { } itemMb
                            ? TextResourceTable.Get(itemMb.NameKey)
                            : buttonInfo.ConsumeUserItem.ItemType.ToString();
                    var consumeCount = buttonInfo.ConsumeUserItem?.ItemCount ?? 0;
                    log(string.Format(ResourceStrings.GachaExecInfo, TextResourceTable.Get(GachaCaseUiTable.GetById(gachaCaseInfo.GachaCaseUiId)?.NameKey), buttonInfo.LotteryCount, consumeName, consumeCount));
                    try
                    {
                        var response = await GetResponse<DrawRequest, DrawResponse>(new DrawRequest {GachaButtonId = buttonInfo.GachaButtonId});
                        response.GachaRewardItemList.PrintUserItems(log);
                        response.BonusRewardItemList.PrintUserItems(log);
                    }
                    catch (ApiErrorException e) when (e.ErrorCode == ErrorCode.GachaHaveMaxCharacter)
                    {
                        log(e.Message);
                        ignoredButtonId.Add(buttonInfo.GachaButtonId);
                    }

                    return true;
                }

                return false;
            }

            static bool IsFreeButton(GachaButtonInfo buttonInfo) => buttonInfo.ConsumeUserItem == null || buttonInfo.ConsumeUserItem.ItemCount == 0;

            bool CheckCount(GachaButtonInfo buttonInfo, List<UserItemDtoInfo> userItems, ItemType itemType, long itemId = 1)
            {
                if (itemId == 0) itemId = 1;
                if (IsFreeButton(buttonInfo)) return true;

                if (buttonInfo.ConsumeUserItem.ItemType != itemType ||
                    buttonInfo.ConsumeUserItem.ItemId != itemId)
                    return false;

                var count = userItems.GetCount(itemType, itemId);
                return buttonInfo.ConsumeUserItem.ItemCount <= count;
            }
        });
    }

    public async Task AutoSetGachaRelic()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var targetRelicType = PlayerOption.GachaConfig.TargetRelicType;
            if (targetRelicType == GachaRelicType.None) return;

            var listResponse = await GetResponse<GetListRequest, GetListResponse>(new GetListRequest());
            if (!listResponse.IsFreeChangeRelicGacha) return;

            var gachaCaseInfo = listResponse.GachaCaseInfoList.Find(d => d.GachaGroupType == GachaGroupType.HolyAngel);
            if (gachaCaseInfo == null) return;

            if (gachaCaseInfo.GachaRelicType == targetRelicType) return;
            if (gachaCaseInfo.GachaBonusDrawCount > 0) return;

            await GetResponse<ChangeGachaRelicRequest, ChangeGachaRelicResponse>(new ChangeGachaRelicRequest {GachaRelicType = targetRelicType});

            log($"{TextResourceTable.Get("[GachaRelicChangeTitle]")} {targetRelicType.GetName()} {ResourceStrings.Success}");
        });
    }

    public async Task DrawGachaRelic()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            if (!PlayerOption.GachaConfig.AutoGachaRelic) return;

            var listResponse = await GetResponse<GetListRequest, GetListResponse>(new GetListRequest());
            var gachaCaseInfo = listResponse.GachaCaseInfoList.Find(d => d.GachaGroupType == GachaGroupType.HolyAngel);
            if (gachaCaseInfo == null) return;

            if (gachaCaseInfo.GachaBonusDrawCount >= 10) return;

            var gachaCaseMb = GachaCaseTable.GetById(gachaCaseInfo.GachaCaseId);
            log(gachaCaseMb.GachaRelicType.GetName());

            for (var i = 0; i < 3; i++)
            {
                // free diamonds only: the server spends free diamonds first, so free >= cost never touches paid ones
                var currency = UserSyncData.GetUserItemCount(ItemType.CurrencyFree);
                var gachaButtonInfo = gachaCaseInfo.GachaButtonInfoList.Find(d => d.ConsumeUserItem?.ItemType == ItemType.CurrencyFree && d.ConsumeUserItem.ItemCount == 300);
                if (gachaButtonInfo == null) break;

                if (gachaCaseInfo.GachaBonusDrawCount >= 10 || currency < gachaButtonInfo.ConsumeUserItem.ItemCount) break;

                var drawResponse = await GetResponse<DrawRequest, DrawResponse>(new DrawRequest {GachaButtonId = gachaButtonInfo.GachaButtonId});
                drawResponse.GachaRewardItemList.PrintUserItems(log);
                drawResponse.BonusRewardItemList.PrintUserItems(log);
                gachaCaseInfo = drawResponse.GachaCaseInfoList?.Find(d => d.GachaCaseId == gachaCaseInfo.GachaCaseId) ?? gachaCaseInfo;
            }
        });
    }

    public Task<SetSelectListResponse> SetGachaSelectList(long gachaCaseId, GachaSelectListType type, List<long> characterIds, CancellationToken token) => GetResponse<SetSelectListRequest, SetSelectListResponse>(new() { GachaCaseId = gachaCaseId, GachaSelectListType = type, CharacterIdList = characterIds }, token);
}