using MementoMori.Ortega.Share.Data.RentalRaid;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.RentalRaid
{
	// 客户端还有 ShareHistoryList（分享记录），用不到未移植
	[MessagePackObject(true)]
	public class GetShareRewardInfoResponse : ApiResponseBase
	{
		public int CurrentSharePoint { get; set; }

		public List<ShareRewardInfo> ShareRewardInfoList { get; set; }
	}
}
