using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Data.PopularityVote;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.PopularityVote
{
    [MessagePackObject(true)]
    public class RewardVoteItemResponse : ApiResponseBase, IUserSyncApiResponse
    {
        public List<PopularityVoteRewardInfo> PopularityVoteRewardInfoList { get; set; }

        public List<UserItem> RewardItemList { get; set; }

        public UserSyncData UserSyncData { get; set; }
    }
}
