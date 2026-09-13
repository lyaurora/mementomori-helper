using MementoMori.Ortega.Share.Data.Battle.Result;
using MementoMori.Ortega.Share.Data.Chat;
using MementoMori.Ortega.Share.Data.GuildSurvey;
using MementoMori.Ortega.Share.Data.Player;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.Chat;

// Official client 4.22.0. HTTP messages use named keys; hub messages use numeric keys.
[MessagePackObject(true), OrtegaApi("chat/addPrivateChatLogPlayer")]
public class AddPrivateChatLogPlayerRequest : ApiRequestBase
{
    public long TargetPlayerId { get; set; }
}

[MessagePackObject(true)]
public class AddPrivateChatLogPlayerResponse : ApiResponseBase
{
    public List<PrivateChatLogPlayerInfo> PrivateChatLogPlayerInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/deleteAnnounceChat")]
public class DeleteAnnounceChatRequest : ApiRequestBase
{
    public List<ChatIdentityInfo> ChatIdentityInfoList { get; set; }
}

[MessagePackObject(true)]
public class DeleteAnnounceChatResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/deletePrivateChatLogPlayer")]
public class DeletePrivateChatLogPlayerRequest : ApiRequestBase
{
    public long TargetPlayerId { get; set; }
}

[MessagePackObject(true)]
public class DeletePrivateChatLogPlayerResponse : ApiResponseBase
{
    public List<PrivateChatLogPlayerInfo> PrivateChatLogPlayerInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getAnnounceChat")]
public class GetAnnounceChatRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class GetAnnounceChatResponse : ApiResponseBase
{
    public List<AnnounceChatInfo> GuildChatInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getChatBattleLog")]
public class GetChatBattleLogRequest : ApiRequestBase
{
    public long TargetPlayerId { get; set; }
    public ChatBattlePropertyInfo ChatBattlePropertyInfo { get; set; }
}

[MessagePackObject(true)]
public class GetChatBattleLogResponse : ApiResponseBase
{
    public BattleSimulationResult BattleSimulationResult { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getChatReactionDetailInfo")]
public class GetChatReactionDetailInfoRequest : ApiRequestBase
{
    public ChatIdentityInfo ChatIdentityInfo { get; set; }
}

[MessagePackObject(true)]
public class GetChatReactionDetailInfoResponse : ApiResponseBase
{
    public Dictionary<ChatReactionType, List<ReactionPlayerInfo>> PlayerInfoListByReactionType { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getGuildChatTabInfo")]
public class GetGuildChatTabInfoRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class GetGuildChatTabInfoResponse : ApiResponseBase, IUserSyncApiResponse
{
    public List<GuildSurveyInfo> UnreadFinishedSurveyList { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getPlayer")]
public class GetPlayerRequest : ApiRequestBase
{
    public List<long> WorldPlayerIdList { get; set; }
}

[MessagePackObject(true)]
public class GetPlayerResponse : ApiResponseBase
{
    public List<PlayerInfo> FriendPlayerInfoList { get; set; }
    public List<PlayerInfo> GuildPlayerInfoList { get; set; }
    public List<PlayerInfo> WorldPlayerInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getPrivateChatLogPlayer")]
public class GetPrivateChatLogPlayerRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class GetPrivateChatLogPlayerResponse : ApiResponseBase
{
    public List<PrivateChatLogPlayerInfo> PrivateChatLogPlayerInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/getPrivateMessage")]
public class GetPrivateMessageRequest : ApiRequestBase
{
    public long LatestTimestamp { get; set; }
    public long OldestTimestamp { get; set; }
    public long TargetPlayerId { get; set; }
}

[MessagePackObject(true)]
public class GetPrivateMessageResponse : ApiResponseBase
{
    public List<ChatInfo> ChatInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("chat/reactChat")]
public class ReactChatRequest : ApiRequestBase
{
    public ChatIdentityInfo ChatIdentityInfo { get; set; }
    public ChatReactionType ChatReactionType { get; set; }
}

[MessagePackObject(true)]
public class ReactChatResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/registerAnnounceChat")]
public class RegisterAnnounceChatRequest : ApiRequestBase
{
    public ChatIdentityInfo ChatIdentityInfo { get; set; }
}

[MessagePackObject(true)]
public class RegisterAnnounceChatResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/sendChatBattleLog")]
public class SendChatBattleLogRequest : ApiRequestBase
{
    public ChatType ChatType { get; set; }
    public long TargetPlayerId { get; set; }
    public ChatBattlePropertyInfo ChatBattlePropertyInfo { get; set; }
}

[MessagePackObject(true)]
public class SendChatBattleLogResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/sendPrivateMessage")]
public class SendPrivateMessageRequest : ApiRequestBase
{
    public string Message { get; set; }
    public long TargetPlayerId { get; set; }
}

[MessagePackObject(true)]
public class SendPrivateMessageResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/switchChatReactionOption")]
public class SwitchChatReactionOptionRequest : ApiRequestBase
{
    public bool CanReact { get; set; }
    public ChatIdentityInfo ChatIdentityInfo { get; set; }
}

[MessagePackObject(true)]
public class SwitchChatReactionOptionResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("chat/updateSettings")]
public class UpdateSettingsRequest : ApiRequestBase
{
    public ChatSettingData ChatSettingData { get; set; }
}

[MessagePackObject(true)]
public class UpdateSettingsResponse : ApiResponseBase, IUserSyncApiResponse
{
    public UserSyncData UserSyncData { get; set; }
}
