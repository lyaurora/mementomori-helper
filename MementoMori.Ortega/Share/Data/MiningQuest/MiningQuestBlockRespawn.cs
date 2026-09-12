using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestBlockRespawn
{
    public long BlockRespawnId { get; set; }

    public int BaseLotteryProbability { get; set; }

    public int BaseBlockSpawnCount { get; set; }

}
