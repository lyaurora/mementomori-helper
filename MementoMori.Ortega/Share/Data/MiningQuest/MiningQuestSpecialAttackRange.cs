using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestSpecialAttackRange
{
    public int Step { get; set; }

    public int Range { get; set; }

    public int UnlockStage { get; set; }

}
