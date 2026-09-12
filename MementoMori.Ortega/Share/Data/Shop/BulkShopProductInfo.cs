using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Shop;

[MessagePackObject(true)]
public class BulkShopProductInfo
{
    public long MbId { get; set; }

    public string ProductId { get; set; }

    public ShopProductType ShopProductType { get; set; }

    public int Count { get; set; }

}
