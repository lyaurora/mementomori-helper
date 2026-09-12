using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestReinforcementInfo
{
    public int Level { get; set; }

    public int RequiredItemCount { get; set; }

    public int RequiredCollectionLevel { get; set; }

    public IReadOnlyList<MiningQuestReinforcementParameter> ParameterList { get; set; }

}
