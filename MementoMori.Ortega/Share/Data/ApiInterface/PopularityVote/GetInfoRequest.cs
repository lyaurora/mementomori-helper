using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.PopularityVote
{
    [MessagePackObject(true)]
    [OrtegaApi("popularityVote/getInfo", true, false)]
    public class GetInfoRequest : ApiRequestBase
    {
    }
}
