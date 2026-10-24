using MementoMori.Ortega.Share.Data.Chat;
using MementoMori.Ortega.Share.Data.GuildNote;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.GuildNote;

[MessagePackObject(true), OrtegaApi("guildNote/createGuildNote")]
public class CreateGuildNoteRequest : ApiRequestBase
{
    public string Title { get; set; }
    public string Content { get; set; }
    public GuildNoteCategoryType GuildNoteCategoryType { get; set; }
}

[MessagePackObject(true)]
public class CreateGuildNoteResponse : ApiResponseBase, IUserSyncApiResponse
{
    public bool IsNgWordReplaced { get; set; }
    public List<GuildNoteInfo> GuildNoteInfoList { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildNote/getGuildNoteList")]
public class GetGuildNoteListRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class GetGuildNoteListResponse : ApiResponseBase, IUserSyncApiResponse
{
    public List<GuildNoteInfo> GuildNoteInfoList { get; set; }
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildNote/getReactionDetailInfo")]
public class GetReactionDetailInfoRequest : ApiRequestBase
{
    public long GuildNoteId { get; set; }
}

[MessagePackObject(true)]
public class GetReactionDetailInfoResponse : ApiResponseBase
{
    public Dictionary<ChatReactionType, List<ReactionPlayerInfo>> PlayerInfoListByReactionType { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildNote/reactGuildNote")]
public class ReactGuildNoteRequest : ApiRequestBase
{
    public long GuildNoteId { get; set; }
    public ChatReactionType ChatReactionType { get; set; }
}

[MessagePackObject(true)]
public class ReactGuildNoteResponse : ApiResponseBase { }

[MessagePackObject(true), OrtegaApi("guildNote/shareGuildNote")]
public class ShareGuildNoteRequest : ApiRequestBase
{
    public long GuildNoteId { get; set; }
}

[MessagePackObject(true)]
public class ShareGuildNoteResponse : ApiResponseBase, IUserSyncApiResponse
{
    public UserSyncData UserSyncData { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildNote/updateGuildNote")]
public class UpdateGuildNoteRequest : ApiRequestBase
{
    public long GuildNoteId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public GuildNoteCategoryType GuildNoteCategoryType { get; set; }
}

[MessagePackObject(true)]
public class UpdateGuildNoteResponse : ApiResponseBase
{
    public bool IsNgWordReplaced { get; set; }
    public List<GuildNoteInfo> GuildNoteInfoList { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildNote/updateGuildNoteList")]
public class UpdateGuildNoteListRequest : ApiRequestBase
{
    public List<long> BeforeUpdateGuildNoteIdList { get; set; }
    public List<long> AfterUpdateGuildNoteIdList { get; set; }
}

[MessagePackObject(true)]
public class UpdateGuildNoteListResponse : ApiResponseBase, IUserSyncApiResponse
{
    public List<GuildNoteInfo> GuildNoteInfoList { get; set; }
    public UserSyncData UserSyncData { get; set; }
}
