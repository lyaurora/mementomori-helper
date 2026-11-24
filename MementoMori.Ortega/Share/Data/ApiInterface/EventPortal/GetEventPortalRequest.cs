using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.EventPortal;

[OrtegaApi("eventPortal/getEventPortal", true, false)]
[MessagePackObject(true)]
public class GetEventPortalRequest : ApiRequestBase
{
    public TransferSpotType TransferSpotType { get; set; }
}
