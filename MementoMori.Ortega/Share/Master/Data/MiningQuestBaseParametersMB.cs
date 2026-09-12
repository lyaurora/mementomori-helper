using MementoMori.Ortega.Share.Data.MiningQuest;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class MiningQuestBaseParametersMB : MasterBookBase
{
    public int BaseLimitTimeSeconds { get; }

    public int BaseBlockGenerateLimit { get; }

    public int BaseAttackPower { get; }

    public int BaseAttackRange { get; }

    public int BaseAttackFrequency { get; }

    public int BaseAttackSpeed { get; }

    public int ContactBlockAttackSpeedRatePercent { get; }

    public int RespawnBlockIntervalMilliseconds { get; }

    public IReadOnlyList<MiningQuestBlockRespawn> BlockRespawnList { get; }

    public IReadOnlyList<MiningQuestSpecialAttack> SpecialAttackList { get; }

    public IReadOnlyList<MiningQuestBlock> BlockList { get; }

    public IReadOnlyList<int> VirtualStickSizeList { get; }

    public IReadOnlyList<int> LayerBlockSizePercentList { get; }

    public IReadOnlyList<int> LayerBlockCapacityWeightList { get; }

    public int BaseFixedRewardCount { get; }

    [SerializationConstructor]
    public MiningQuestBaseParametersMB(long id, bool? isIgnore, string memo, int baseLimitTimeSeconds, int baseBlockGenerateLimit, int baseAttackPower, int baseAttackRange, int baseAttackFrequency, int baseAttackSpeed, int contactBlockAttackSpeedRatePercent, int respawnBlockIntervalMilliseconds, IReadOnlyList<MiningQuestBlockRespawn> blockRespawnList, IReadOnlyList<MiningQuestSpecialAttack> specialAttackList, IReadOnlyList<MiningQuestBlock> blockList, IReadOnlyList<int> virtualStickSizeList, IReadOnlyList<int> layerBlockSizePercentList, IReadOnlyList<int> layerBlockCapacityWeightList, int baseFixedRewardCount) : base(id, isIgnore, memo)
    {
        BaseLimitTimeSeconds = baseLimitTimeSeconds;
        BaseBlockGenerateLimit = baseBlockGenerateLimit;
        BaseAttackPower = baseAttackPower;
        BaseAttackRange = baseAttackRange;
        BaseAttackFrequency = baseAttackFrequency;
        BaseAttackSpeed = baseAttackSpeed;
        ContactBlockAttackSpeedRatePercent = contactBlockAttackSpeedRatePercent;
        RespawnBlockIntervalMilliseconds = respawnBlockIntervalMilliseconds;
        BlockRespawnList = blockRespawnList;
        SpecialAttackList = specialAttackList;
        BlockList = blockList;
        VirtualStickSizeList = virtualStickSizeList;
        LayerBlockSizePercentList = layerBlockSizePercentList;
        LayerBlockCapacityWeightList = layerBlockCapacityWeightList;
        BaseFixedRewardCount = baseFixedRewardCount;
    }
}
