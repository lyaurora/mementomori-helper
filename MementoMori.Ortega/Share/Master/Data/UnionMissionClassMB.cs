using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class UnionMissionClassMB : MasterBookBase
{
    public UnionMissionClassType ClassType { get; }
    public string ClassNameKey { get; }
    public string ClassDescriptionKey { get; }
    public int GuildCountPerBlock { get; }
    public int NpcCountPerBlock { get; }
    public long PromotionRank { get; }
    public long RelegationRank { get; }

    [SerializationConstructor]
    public UnionMissionClassMB(long id, bool? isIgnore, string memo, UnionMissionClassType classType, string classNameKey, string classDescriptionKey,
        int guildCountPerBlock, int npcCountPerBlock, long promotionRank, long relegationRank) : base(id, isIgnore, memo)
    {
        ClassType = classType;
        ClassNameKey = classNameKey;
        ClassDescriptionKey = classDescriptionKey;
        GuildCountPerBlock = guildCountPerBlock;
        NpcCountPerBlock = npcCountPerBlock;
        PromotionRank = promotionRank;
        RelegationRank = relegationRank;
    }

    public UnionMissionClassMB() : base(0, null, null) { }
}
