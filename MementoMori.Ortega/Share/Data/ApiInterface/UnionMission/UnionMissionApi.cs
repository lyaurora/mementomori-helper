using MessagePack;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Data.UnionMission;
using MementoMori.Ortega.Share.Enums;

namespace MementoMori.Ortega.Share.Data.ApiInterface.UnionMission;

[MessagePackObject(true), OrtegaApi("unionMission/entryUnionMission")]
public class EntryUnionMissionRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class EntryUnionMissionResponse : ApiResponseBase, IGuildSyncApiResponse
{
    public GuildSyncData GuildSyncData { get; set; }
}

// 5.1.1 Union Mission event APIs. Names, routes and field sets follow the official client metadata.
[MessagePackObject(true), OrtegaApi("unionMission/getInfo")]
public class UnionMissionGetInfoRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class UnionMissionGetInfoResponse : ApiResponseBase, IUserSyncApiResponse
{
    public UnionMissionClassType ClassType { get; set; }
    public UnionMissionClassType PrevClassType { get; set; }
    public bool ExistReceivableTotalPointReward { get; set; }
    public long GuildPoint { get; set; }
    public long GuildRank { get; set; }
    public int BlockGuildCount { get; set; }
    public List<UnionMissionPanelInfo> SharedMissionPanelList { get; set; }
    public List<UnionMissionPanelInfo> PersonalMissionPanelList { get; set; }
    public long PersonalAchievedCount { get; set; }
    public long PersonalPoint { get; set; }
    public long PersonalRank { get; set; }
    public long CumulativePersonalPoint { get; set; }
    public long ReceivedGuildPointRewardThreshold { get; set; }
    public long ReceivedPersonalPointRewardThreshold { get; set; }
    public int RemainingAcceptCount { get; set; }
    public int BuyAcceptCountToday { get; set; }
    public bool IsOpen { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/getRanking")]
public class UnionMissionGetRankingRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class UnionMissionGetRankingResponse : ApiResponseBase
{
    public long GuildRank { get; set; }
    public long TodayGuildRank { get; set; }
    public long GuildPoint { get; set; }
    public List<UnionMissionGuildRankingEntry> GuildRankingList { get; set; }
    public long PersonalRank { get; set; }
    public long TodayPersonalRank { get; set; }
    public long PersonalPoint { get; set; }
    public int PersonalAcceptedCount { get; set; }
    public int PersonalAchievedCount { get; set; }
    public List<UnionMissionPersonalRankingEntry> PersonalRankingList { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/buyAcceptCount")]
public class UnionMissionBuyAcceptCountRequest : ApiRequestBase
{
    public int BuyCount { get; set; }
}

[MessagePackObject(true)]
public class UnionMissionBuyAcceptCountResponse : ApiResponseBase, IUserSyncApiResponse
{
    public int RemainingAcceptCount { get; set; }
    public int BuyAcceptCountToday { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

public abstract class UnionMissionPanelRequest : ApiRequestBase
{
    public long MissionPanelId { get; set; }
}

public abstract class UnionMissionPanelListResponse : ApiResponseBase
{
    public List<UnionMissionPanelInfo> SharedMissionPanelList { get; set; }
    public List<UnionMissionPanelInfo> PersonalMissionPanelList { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/acceptMission")]
public class UnionMissionAcceptMissionRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionAcceptMissionResponse : UnionMissionPanelListResponse
{
    public int RemainingAcceptCount { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/retireMission")]
public class UnionMissionRetireMissionRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionRetireMissionResponse : UnionMissionPanelListResponse { }

[MessagePackObject(true), OrtegaApi("unionMission/refreshPersonalMission")]
public class UnionMissionRefreshPersonalMissionRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionRefreshPersonalMissionResponse : UnionMissionPanelListResponse { }

[MessagePackObject(true), OrtegaApi("unionMission/refreshSharedMission")]
public class UnionMissionRefreshSharedMissionRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionRefreshSharedMissionResponse : UnionMissionPanelListResponse { }

[MessagePackObject(true), OrtegaApi("unionMission/resetCoolTimeMission")]
public class UnionMissionResetCoolTimeMissionRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionResetCoolTimeMissionResponse : UnionMissionPanelListResponse, IUserSyncApiResponse
{
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/selectMission")]
public class UnionMissionSelectMissionRequest : UnionMissionPanelRequest
{
    public long MissionId { get; set; }
}

[MessagePackObject(true)]
public class UnionMissionSelectMissionResponse : UnionMissionPanelListResponse, IUserSyncApiResponse
{
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/receiveMissionReward")]
public class UnionMissionReceiveMissionRewardRequest : UnionMissionPanelRequest { }

[MessagePackObject(true)]
public class UnionMissionReceiveMissionRewardResponse : UnionMissionPanelListResponse, IUserSyncApiResponse
{
    public long GainedPoint { get; set; }
    public long GuildPoint { get; set; }
    public long GuildRank { get; set; }
    public long PersonalAchievedCount { get; set; }
    public long PersonalPoint { get; set; }
    public long PersonalRank { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/receiveTotalPointReward")]
public class UnionMissionReceiveTotalPointRewardRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class UnionMissionReceiveTotalPointRewardResponse : ApiResponseBase, IUserSyncApiResponse
{
    public List<UserItem> RewardItemList { get; set; }
    public long ReceivedGuildPointRewardThreshold { get; set; }
    public long ReceivedPersonalPointRewardThreshold { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("unionMission/receiveIndividualTotalPointReward")]
public class UnionMissionReceiveIndividualTotalPointRewardRequest : ApiRequestBase
{
    public long RewardMBId { get; set; }
    public UnionMissionTotalPointRewardType RewardType { get; set; }
    public bool IsRepeatPersonalReward { get; set; }
}

[MessagePackObject(true)]
public class UnionMissionReceiveIndividualTotalPointRewardResponse : UnionMissionReceiveTotalPointRewardResponse { }
