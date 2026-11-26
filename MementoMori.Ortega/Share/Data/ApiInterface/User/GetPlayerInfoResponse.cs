using MementoMori.Ortega.Share.Data.Player;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.User;

[MessagePackObject(true)]
public class GetPlayerInfoResponse : ApiResponseBase
{
    public List<PlayerInfo> PlayerInfos { get; set; }
}
