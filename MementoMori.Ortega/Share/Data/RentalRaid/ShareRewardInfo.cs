using MementoMori.Ortega.Share.Data.Item;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.RentalRaid
{
	[MessagePackObject(true)]
	public class ShareRewardInfo
	{
		public int RequiredSharePoint { get; set; }

		public List<UserItem> RewardItems { get; set; }

		public bool IsReceived { get; set; }
	}
}
