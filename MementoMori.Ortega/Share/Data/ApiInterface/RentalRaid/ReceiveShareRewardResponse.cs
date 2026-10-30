using MementoMori.Ortega.Share.Data.Item;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.RentalRaid
{
	[MessagePackObject(true)]
	public class ReceiveShareRewardResponse : ApiResponseBase, IUserSyncApiResponse
	{
		public List<UserItem> ReceiveItems { get; set; }

		public UserSyncData UserSyncData { get; set; }
	}
}
