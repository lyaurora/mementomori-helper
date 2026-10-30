using MessagePack;

namespace MementoMori.Ortega.Share.Data.LimitedEvent;

[MessagePackObject(true)]
public class TargetInfo
{
    public long Value1 { get; set; }
    public long Value2 { get; set; }
}
