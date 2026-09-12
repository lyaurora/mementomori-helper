using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestSpecialAttack
{
    public long SpecialAttackId { get; set; }

    public MiningQuestSpecialAttackType Type { get; set; }

    public int InitialCoolTimeMilliseconds { get; set; }

    public int BaseCoolTimeMilliseconds { get; set; }

    public int AttackPowerRate { get; set; }

    public IReadOnlyList<MiningQuestSpecialAttackRange> AttackRangeList { get; set; }

    public int RangeExpansionIntervalMilliseconds { get; set; }

    public int BaseSimultaneousCount { get; set; }

}
