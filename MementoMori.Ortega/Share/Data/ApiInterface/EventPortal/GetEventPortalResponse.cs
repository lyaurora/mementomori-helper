using MementoMori.Ortega.Share.Data.EventPortal;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.EventPortal;

[MessagePackObject(true)]
public class GetEventPortalResponse : ApiResponseBase
{
    public long CharacterId { get; set; }

    public long EventPortalId { get; set; }

    public EventPortalBannerInfo GachaEventPortalBannerInfo { get; set; }

    public List<EventPortalBannerInfo> InsideEventPortalBannerInfo { get; set; }

    public long NoticeEndTimeStamp { get; set; }

    public long NoticeGroupId { get; set; }

    public List<OutsideEventPortalBannerInfo> OutsideEventPortalBannerInfo { get; set; }

    public string TitleKey { get; set; }

    public EventPortalVideoInfo EventPortalVideoInfo { get; set; }

    public int PlayVideoTotalCount { get; set; }

    public bool IsDisplayBadgePlayVideo { get; set; }
}
