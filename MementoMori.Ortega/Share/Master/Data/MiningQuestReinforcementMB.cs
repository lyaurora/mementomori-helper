using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Data.MiningQuest;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class MiningQuestReinforcementMB : MasterBookBase
{
    public string NameTextKey { get; }

    public string DescriptionTextKey { get; }

    public MiningQuestReinforcementDisplayType DisplayType { get; }

    public MiningQuestReinforcementParameterDisplayType ParameterDisplayType { get; }

    public long IconImageId { get; }

    public IReadOnlyList<MiningQuestReinforcementInfo> ReinforcementInfoList { get; }

    [SerializationConstructor]
    public MiningQuestReinforcementMB(long id, bool? isIgnore, string memo, string nameTextKey, string descriptionTextKey, MiningQuestReinforcementDisplayType displayType, MiningQuestReinforcementParameterDisplayType parameterDisplayType, long iconImageId, IReadOnlyList<MiningQuestReinforcementInfo> reinforcementInfoList) : base(id, isIgnore, memo)
    {
        NameTextKey = nameTextKey;
        DescriptionTextKey = descriptionTextKey;
        DisplayType = displayType;
        ParameterDisplayType = parameterDisplayType;
        IconImageId = iconImageId;
        ReinforcementInfoList = reinforcementInfoList;
    }
}
