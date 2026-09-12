using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class ShopPointRateMB : MasterBookBase
{
    public int SavePointRate { get; }

    public ShopPayType ShopPayType { get; }

    public string StartTime { get; }

    public string EndTime { get; }

    [SerializationConstructor]
    public ShopPointRateMB(long id, bool? isIgnore, string memo, int savePointRate, ShopPayType shopPayType, string startTime, string endTime) : base(id, isIgnore, memo)
    {
        SavePointRate = savePointRate;
        ShopPayType = shopPayType;
        StartTime = startTime;
        EndTime = endTime;
    }
}
