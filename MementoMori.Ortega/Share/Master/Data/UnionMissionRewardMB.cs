using MementoMori.Ortega.Share.Data.Shop;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Utils;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

// 5.1.1 Union Mission masters; property names follow the official client metadata.
[MessagePackObject(true)]
public class UnionMissionMB : MasterBookBase, IHasStartEndTime
{
    public string DescriptionKey { get; }
    public int Difficulty { get; }
    public MissionAchievementType AchievementType { get; }
    public long RequireValue { get; }
    public long GetPoint { get; }
    public int LimitDays { get; }
    public bool IsSelectableByTicket { get; }
    public string StartTime { get; }
    public string EndTime { get; }

    [SerializationConstructor]
    public UnionMissionMB(long id, bool? isIgnore, string memo, string descriptionKey, int difficulty, MissionAchievementType achievementType,
        long requireValue, long getPoint, int limitDays, bool isSelectableByTicket, string startTime, string endTime) : base(id, isIgnore, memo)
    {
        DescriptionKey = descriptionKey;
        Difficulty = difficulty;
        AchievementType = achievementType;
        RequireValue = requireValue;
        GetPoint = getPoint;
        LimitDays = limitDays;
        IsSelectableByTicket = isSelectableByTicket;
        StartTime = startTime;
        EndTime = endTime;
    }

    public UnionMissionMB() : base(0, null, null) { }
}

[MessagePackObject(true)]
public class UnionMissionRankingRewardMB : MasterBookBase
{
    public UnionMissionClassType ClassType { get; }
    public UnionMissionRankingType RankingType { get; }
    public long Rank { get; }
    public IReadOnlyList<RarityItem> RewardItemList { get; }

    [SerializationConstructor]
    public UnionMissionRankingRewardMB(long id, bool? isIgnore, string memo, UnionMissionClassType classType, UnionMissionRankingType rankingType,
        long rank, IReadOnlyList<RarityItem> rewardItemList) : base(id, isIgnore, memo)
    {
        ClassType = classType;
        RankingType = rankingType;
        Rank = rank;
        RewardItemList = rewardItemList;
    }

    public UnionMissionRankingRewardMB() : base(0, null, null) { }
}

[MessagePackObject(true)]
public class UnionMissionRepeatPersonalRewardMB : MasterBookBase
{
    public UnionMissionClassType ClassType { get; }
    public long RequirePersonalPoint { get; }
    public IReadOnlyList<RarityItem> RewardItemList { get; }

    [SerializationConstructor]
    public UnionMissionRepeatPersonalRewardMB(long id, bool? isIgnore, string memo, UnionMissionClassType classType, long requirePersonalPoint,
        IReadOnlyList<RarityItem> rewardItemList) : base(id, isIgnore, memo)
    {
        ClassType = classType;
        RequirePersonalPoint = requirePersonalPoint;
        RewardItemList = rewardItemList;
    }

    public UnionMissionRepeatPersonalRewardMB() : base(0, null, null) { }
}

[MessagePackObject(true)]
public class UnionMissionTotalPointRewardMB : MasterBookBase
{
    public UnionMissionClassType ClassType { get; }
    public long RequireGuildPoint { get; }
    public long RequirePersonalPoint { get; }
    public IReadOnlyList<RarityItem> GuildPointRewardItemList { get; }
    public IReadOnlyList<RarityItem> PersonalPointRewardItemList { get; }

    [SerializationConstructor]
    public UnionMissionTotalPointRewardMB(long id, bool? isIgnore, string memo, UnionMissionClassType classType, long requireGuildPoint,
        long requirePersonalPoint, IReadOnlyList<RarityItem> guildPointRewardItemList, IReadOnlyList<RarityItem> personalPointRewardItemList)
        : base(id, isIgnore, memo)
    {
        ClassType = classType;
        RequireGuildPoint = requireGuildPoint;
        RequirePersonalPoint = requirePersonalPoint;
        GuildPointRewardItemList = guildPointRewardItemList;
        PersonalPointRewardItemList = personalPointRewardItemList;
    }

    public UnionMissionTotalPointRewardMB() : base(0, null, null) { }
}
