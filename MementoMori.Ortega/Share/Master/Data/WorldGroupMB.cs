using System.ComponentModel;
using MementoMori.Ortega.Share.Data.Time;
using MementoMori.Ortega.Share.Master.Attributes;
using MementoMori.Ortega.Share.Utils;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[MessagePackObject(true)]
	[Description("ワールドグループ")]
	public class WorldGroupMB : MasterBookBase, IHasStartEndTime
	{
        [PropertyOrder(5)]
        [Description("終了日時")]
        public string EndTime { get; }

        [DateTimeString]
        [PropertyOrder(9)]
        [Description("レジェンドリーグ終了日時")]
        public string EndLegendLeagueDateTime { get; }

        [PropertyOrder(1)]
        [Description("タイムサーバー")]
        public long TimeServerId { get; }

        [PropertyOrder(4)]
        [Description("開始日時")]
        public string StartTime { get; }

        [Nest(false, 0)]
        [PropertyOrder(6)]
        [Description("グランドバトル開始日時")]
        public IReadOnlyList<StartEndTime> GrandBattleDateTimeList { get; }

        [DateTimeString]
        [PropertyOrder(8)]
        [Description("レジェンドリーグ開始日時")]
        public string StartLegendLeagueDateTime { get; }

        [PropertyOrder(3)]
        [Description("WorldIdのリスト")]
        public IReadOnlyList<long> WorldIdList { get; }

        [PropertyOrder(10)]
        [Description("グランドバトル開催頻度")]
        public int MonthlyOpenCount { get; }

		[SerializationConstructor]
        public WorldGroupMB(long id, bool? isIgnore, string memo, string endTime, long timeServerId, string startTime, IReadOnlyList<long> worldIdList, IReadOnlyList<StartEndTime> grandBattleDateTimeList, string startLegendLeagueDateTime, string endLegendLeagueDateTime, int monthlyOpenCount)
		    :base( id, isIgnore, memo)
		{
            this.EndTime = endTime;
            this.TimeServerId = timeServerId;
            this.StartTime = startTime;
            this.WorldIdList = worldIdList;
            this.GrandBattleDateTimeList = grandBattleDateTimeList;
            this.StartLegendLeagueDateTime = startLegendLeagueDateTime;
            this.EndLegendLeagueDateTime = endLegendLeagueDateTime;
            this.MonthlyOpenCount = monthlyOpenCount;
		}

		public WorldGroupMB() :base(0L, false, ""){}

		public bool IsActiveGroup(DateTime localDateTime)
		{
			if (string.IsNullOrWhiteSpace(this.StartTime) || string.IsNullOrWhiteSpace(this.EndTime))
			{
				return false;
			}
			bool flag = this.StartTime.ToDateTime() <= localDateTime;
			if (!flag)
			{
				return flag;
			}
			DateTime dateTime = this.EndTime.ToDateTime();
			return localDateTime < dateTime;
		}

		public bool IsOpenedGrandBattle(DateTime localDateTime)
		{
			if (this.GrandBattleDateTimeList == null)
			{
				return false;
			}
			foreach (var range in this.GrandBattleDateTimeList)
			{
				if (range == null || string.IsNullOrWhiteSpace(range.StartTime) || string.IsNullOrWhiteSpace(range.EndTime))
				{
					continue;
				}
				var start = range.StartTime.ToDateTime();
				var end = range.EndTime.ToDateTime();
				if (start <= localDateTime && localDateTime <= end)
				{
					return true;
				}
			}
			return false;
		}

		public bool IsOpenedLegendLeague(DateTime localDateTime)
		{
			bool flag = this.StartLegendLeagueDateTime.ToDateTime() <= localDateTime;
			if (!flag)
			{
				return flag;
			}
			DateTime dateTime = this.EndLegendLeagueDateTime.ToDateTime();
			return localDateTime < dateTime;
		}

        public bool IsAfterStartLegendLeague(DateTime localDateTime)
		{
			return this.StartLegendLeagueDateTime.ToDateTime() <= localDateTime;
		}

        public bool IsAfterStartGrandBattle(DateTime localDateTime)
		{
			if (this.GrandBattleDateTimeList == null)
			{
				return false;
			}
			foreach (var range in this.GrandBattleDateTimeList)
			{
				if (range == null || string.IsNullOrWhiteSpace(range.StartTime))
				{
					continue;
				}
				if (range.StartTime.ToDateTime() <= localDateTime)
				{
					return true;
				}
			}
			return false;
		}
	}
}
