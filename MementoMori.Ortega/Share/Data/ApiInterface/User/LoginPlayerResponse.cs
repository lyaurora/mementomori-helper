using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.Chat;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.User
{
    [MessagePackObject(true)]
    public class LoginPlayerResponse : ApiResponseBase, IUserSyncApiResponse
    {
        public GuildSyncData GuildSyncData { get; set; }


        public string AuthTokenOfMagicOnion { get; set; }

        public BanChatInfo BanChatInfo { get; set; }

        public UserSyncData UserSyncData { get; set; }

        public LoginPlayerResponse()
        {
        }
    }
}