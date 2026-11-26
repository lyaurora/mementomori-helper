using MementoMori.Ortega.Share.Data.PopularityVote;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.PopularityVote
{
    [MessagePackObject(true)]
    public class GetInfoResponse : ApiResponseBase, IUserSyncApiResponse
    {
        public List<PopularityVoteRewardInfo> PopularityVoteRewardInfoList { get; set; }

        public long TotalVoteCount { get; set; }

        public UserSyncData UserSyncData { get; set; }
    }
}
