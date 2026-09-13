using MementoMori.Ortega.Share.Data.GuildSurvey;
using MementoMori.Ortega.Share.Data.Player;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.GuildSurvey;

[MessagePackObject(true), OrtegaApi("guildSurvey/getGuildSurveyList")]
public class GetGuildSurveyListRequest : ApiRequestBase { }

[MessagePackObject(true)]
public class GetGuildSurveyListResponse : ApiResponseBase
{
    public List<GuildSurveyInfo> SurveyList { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildSurvey/getGuildSurveyDetailInfo")]
public class GetGuildSurveyDetailInfoRequest : ApiRequestBase
{
    public string SurveyGuid { get; set; }
}

[MessagePackObject(true)]
public class GetGuildSurveyDetailInfoResponse : ApiResponseBase
{
    public Dictionary<GuildSurveyChoiceType, List<PlayerInfo>> VotedPlayerInfoListByChoiceType { get; set; }
}

[MessagePackObject(true), OrtegaApi("guildSurvey/voteGuildSurvey")]
public class VoteGuildSurveyRequest : ApiRequestBase
{
    public string SurveyGuid { get; set; }
    public GuildSurveyChoiceType SelectedChoiceType { get; set; }
}

[MessagePackObject(true)]
public class VoteGuildSurveyResponse : ApiResponseBase
{
    public List<GuildSurveyInfo> SurveyList { get; set; }
}
