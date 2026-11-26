using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data;

[MessagePackObject(true)]
public class BookSortReward
{
    public BookSortRewardType BookSortRewardType { get; set; }

    public double DropRate { get; set; }

    public List<BookSortRewardItemsRate> BookSortRewardItemsRateList { get; set; }
}
