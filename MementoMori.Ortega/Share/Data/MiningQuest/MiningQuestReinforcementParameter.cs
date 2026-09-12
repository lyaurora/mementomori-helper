using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.MiningQuest;

[MessagePackObject(true)]
public class MiningQuestReinforcementParameter
{
    public MiningQuestReinforcementParameterType ParameterType { get; set; }

    public long ParameterId { get; set; }

    public int Value { get; set; }

    public int SortOrder { get; set; }

}
