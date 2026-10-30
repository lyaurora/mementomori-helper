using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.RentalRaid
{
	[MessagePackObject(true)]
	[OrtegaApi("rentalRaid/receiveShareReward", true, false)]
	public class ReceiveShareRewardRequest : ApiRequestBase
	{
		public int SharePoint { get; set; }
	}
}
