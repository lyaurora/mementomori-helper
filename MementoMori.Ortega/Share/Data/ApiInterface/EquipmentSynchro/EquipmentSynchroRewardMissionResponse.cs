using MementoMori.Ortega.Share.Data.Item;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.EquipmentSynchro
{
    [MessagePackObject(true)]
    public class EquipmentSynchroRewardMissionResponse : ApiResponseBase, IUserSyncApiResponse
    {
        public UserItem RewardItem { get; set; }

        public UserSyncData UserSyncData { get; set; }
    }
}
