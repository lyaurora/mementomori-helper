using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class MediaMB : MasterBookBase
{
    public MediaType MediaType { get; }

    public int SortOrder { get; }

    public long MovieId { get; }

    public string MediaTitleKey { get; }

    public string UrlKey { get; }

    public long MissionId { get; }

    public IReadOnlyList<int> DisplayLanguageTypeList { get; }

    [SerializationConstructor]
    public MediaMB(long id, bool? isIgnore, string memo, MediaType mediaType, int sortOrder, long movieId, string mediaTitleKey, string urlKey, long missionId, IReadOnlyList<int> displayLanguageTypeList) : base(id, isIgnore, memo)
    {
        MediaType = mediaType;
        SortOrder = sortOrder;
        MovieId = movieId;
        MediaTitleKey = mediaTitleKey;
        UrlKey = urlKey;
        MissionId = missionId;
        DisplayLanguageTypeList = displayLanguageTypeList;
    }
}
