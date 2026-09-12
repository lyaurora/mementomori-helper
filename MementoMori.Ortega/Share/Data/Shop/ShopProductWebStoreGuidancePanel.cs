using MessagePack;

namespace MementoMori.Ortega.Share.Data.Shop;

[MessagePackObject(true)]
public class ShopProductWebStoreGuidancePanel
{
    public bool IsDisplayCopyButton { get; set; }

    public long NoticeGroupId { get; set; }

}
