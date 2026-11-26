using MessagePack;

namespace MementoMori.Ortega.Share.Data;

[MessagePackObject(true)]
public class BookSortSyncData
{
    public int CurrentFloor { get; set; }

    public List<UnlockedGridCellInfo> UnlockedGridCellInfoList { get; set; }

    public bool? IsExistReceivableMission { get; set; }

    public int SelectedBonusFloorRewardIndex { get; set; }

    public List<BookSortFloorHistory> FloorHistoryList { get; set; }

    public int? LastUnlockGridCellDateIntYearMonthDay { get; set; }

    public bool IsUnlockedWinGridCell() => UnlockedGridCellInfoList?.Any(info => info.IsWin) == true;

    public bool IsUnlockedKeyGridCell() => UnlockedGridCellInfoList?.Any(info => info.IsKey) == true;

    public bool IsUnlockedWinAndKeyGridCell() => IsUnlockedWinGridCell() && IsUnlockedKeyGridCell();
}
