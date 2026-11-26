using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.EquipmentSynchro
{
    [MessagePackObject(true)]
    [OrtegaApi("equipmentSynchro/rewardMission", true, false)]
    public class EquipmentSynchroRewardMissionRequest : ApiRequestBase
    {
        public bool IsBulk { get; set; }
    }
}
