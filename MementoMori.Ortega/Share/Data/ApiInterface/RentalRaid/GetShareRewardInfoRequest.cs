using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.RentalRaid
{
	[MessagePackObject(true)]
	[OrtegaApi("rentalRaid/getShareRewardInfo", true, false)]
	public class GetShareRewardInfoRequest : ApiRequestBase
	{
	}
}
