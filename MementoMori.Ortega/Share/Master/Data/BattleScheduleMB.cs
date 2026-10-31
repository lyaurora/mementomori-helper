using System.ComponentModel;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
    [MessagePackObject(true)]
    [Description("バトルスケジュール")]
    public class BattleScheduleMB : MasterBookBase
    {
        [PropertyOrder(1)]
        [Description("バトルタイプ")]
        public BattleScheduleType BattleScheduleType { get; }

        [Description("最大クエストID")]
        [PropertyOrder(2)]
        public long MaxQuestId { get; }

        [Description("開始日時")]
        [DateTimeString]
        [PropertyOrder(3)]
        public string StartTimeFixJST { get; }

        [SerializationConstructor]
        public BattleScheduleMB(long id, bool? isIgnore, string memo, BattleScheduleType battleScheduleType, long maxQuestId, string startTimeFixJST)
            : base(id, isIgnore, memo)
        {
            BattleScheduleType = battleScheduleType;
            MaxQuestId = maxQuestId;
            StartTimeFixJST = startTimeFixJST;
        }

        public BattleScheduleMB() : base(0L, null, null)
        {
        }
    }
}