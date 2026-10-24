using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.Debug
{
	[MessagePackObject(true)]
	public class SetVipLevelResponse : ApiResponseBase, IUserSyncApiResponse
	{
		public UserSyncData UserSyncData { get; set; }
	}
}
