using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.PopularityVote
{
    [MessagePackObject(true)]
    [OrtegaApi("popularityVote/rewardVoteItem", true, false)]
    public class RewardVoteItemRequest : ApiRequestBase
    {
        public bool IsAllReward { get; set; }

        public long TargetRewardVoteCount { get; set; }
    }
}
