using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.User;

[MessagePackObject(true)]
[OrtegaApi("user/getPlayerInfo", true, false)]
public class GetPlayerInfoRequest : ApiRequestBase
{
    public List<long> PlayerIds { get; set; }

    public DeckUseContentType DeckUseContentType { get; set; }

    public bool IsGlobal { get; set; }
}
