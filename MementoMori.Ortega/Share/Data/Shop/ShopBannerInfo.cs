using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Data;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Shop;

[MessagePackObject(true)]
public class ShopBannerInfo
{
    public ShopBannerDisplayType ShopBannerDisplayType { get; set; }

    public string DisplayTextKey { get; set; }

    public CustomTextSimpleLayoutInfo CustomTextSimpleLayoutInfo { get; set; }

    public int TextAlignment { get; set; }

    public TransferSpotType TransferSpotType { get; set; }

    public string TransferSpotInfo { get; set; }

    public long NoticeGroupId { get; set; }

    public bool IsDisplayCopyButton { get; set; }

}
