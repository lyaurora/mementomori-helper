using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestBlock
{
    public long BlockId { get; set; }

    public string NameTextKey { get; set; }

    public long BaseHealth { get; set; }

    public int Size { get; set; }

    public long RewardItemCount { get; set; }

    public int BaseScore { get; set; }

    public int BaseLotteryWeight { get; set; }

    public long ImageId { get; set; }

}
