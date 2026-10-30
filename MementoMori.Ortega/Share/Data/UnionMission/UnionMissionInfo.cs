using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.UnionMission;

[MessagePackObject(true)]
public class UnionMissionPanelInfo
{
    public long MissionPanelId { get; set; }
    public long MissionId { get; set; }
    public UnionMissionType MissionType { get; set; }
    public UnionMissionPanelStatus Status { get; set; }
    public long ProgressCount { get; set; }
    public int BonusRate { get; set; }
    public long AcceptedPlayerId { get; set; }
    public long AcceptedTime { get; set; }
    public long LimitTime { get; set; }
    public long CoolTimeEndTime { get; set; }
    public bool IsCoolTimeResettable { get; set; }
}

[MessagePackObject(true)]
public class UnionMissionGuildRankingEntry
{
    public long GuildId { get; set; }
    public string GuildName { get; set; }
    public long Point { get; set; }
    public long Rank { get; set; }
    public int GuildMemberCount { get; set; }
    public long NumberOfPossibleJoinPeople { get; set; }
    public LegendLeagueClassType LegendLeagueClass { get; set; }
    public long MainCharacterIconId { get; set; }
    public long MainCharacterIconEffectId { get; set; }
}

[MessagePackObject(true)]
public class UnionMissionPersonalRankingEntry
{
    public long Point { get; set; }
    public int AcceptedCount { get; set; }
    public int AchievedCount { get; set; }
    public long PlayerId { get; set; }
    public string PlayerName { get; set; }
    public long Rank { get; set; }
    public LegendLeagueClassType LegendLeagueClass { get; set; }
    public long MainCharacterIconId { get; set; }
    public long MainCharacterIconEffectId { get; set; }
}
