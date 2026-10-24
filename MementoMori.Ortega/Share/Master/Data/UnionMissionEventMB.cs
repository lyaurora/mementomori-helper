using MementoMori.Ortega.Share.Utils;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data;

[MessagePackObject(true)]
public class UnionMissionEventMB : MasterBookBase, IHasStartEndTime
{
    public IReadOnlyList<long> MissionLotteryIdList { get; }
    public string ReadyStartTime { get; }
    public string StartTime { get; }
    public string EndTime { get; }
    public string DisplayBannerEndTime { get; }

    [SerializationConstructor]
    public UnionMissionEventMB(long id, bool? isIgnore, string memo, IReadOnlyList<long> missionLotteryIdList, string readyStartTime,
        string startTime, string endTime, string displayBannerEndTime) : base(id, isIgnore, memo)
    {
        MissionLotteryIdList = missionLotteryIdList;
        ReadyStartTime = readyStartTime;
        StartTime = startTime;
        EndTime = endTime;
        DisplayBannerEndTime = displayBannerEndTime;
    }

    public UnionMissionEventMB() : base(0, null, null) { }
}
