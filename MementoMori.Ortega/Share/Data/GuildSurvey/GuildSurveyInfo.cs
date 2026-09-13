using MementoMori.Ortega.Share.Data.Player;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.GuildSurvey;

[MessagePackObject(true)]
public class GuildSurveyInfo
{
    public string SurveyGuid { get; set; }
    public long VotingEndLocalTimestamp { get; set; }
    public string Content { get; set; }
    public Dictionary<GuildSurveyChoiceType, string> ChoiceMap { get; set; }
    public Dictionary<GuildSurveyChoiceType, int> VoteCountMap { get; set; }
    public PlayerInfo CreateSurveyPlayerInfo { get; set; }
    public long CreateLocalTimestamp { get; set; }
    public bool IsVoted { get; set; }
}
