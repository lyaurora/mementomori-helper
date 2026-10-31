using System.Globalization;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;
using MementoMori.Ortega.Share.Utils;

namespace MementoMori.Ortega.Share
{
	public class OrtegaTimeManager : ILocalTime
	{
		public OrtegaTimeManager()
		{
			this.DifferenceFromUtc = 0L;
		}

		public OrtegaTimeManager(TimeServerMB timeServerMB)
		{
			TimeSpan timeSpan = TimeSpan.Parse(timeServerMB.DifferenceDateTimeFromUtc);
			this.DifferenceFromUtc = (long) timeSpan.TotalMilliseconds;
		}

		public long DifferenceFromUtc { get; private set; }

		public void SetDifferenceFromUtc(TimeServerMB timeServerMB)
		{
			if (!string.IsNullOrEmpty(timeServerMB.DifferenceDateTimeFromUtc))
			{
				TimeSpan timeSpan = TimeSpan.Parse(timeServerMB.DifferenceDateTimeFromUtc);
				this.DifferenceFromUtc = (long) timeSpan.TotalMilliseconds;
			}
		}

		public long GetLocalTimestamp()
		{
			return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + this.DifferenceFromUtc;
		}

		public DateTime GetLocalDateTime()
		{
			return TimeUtil.UtcEpoch.AddMilliseconds(this.GetLocalTimestamp());
		}

		public long GetNDaysLaterChangeDayTimeStamp(long nDay, long timestamp = -1L)
		{
			if (timestamp == -1L)
			{
				timestamp = this.GetLocalTimestamp();
			}
			return ToChangeDayTimeStamp(this.GetLocalGameDate(timestamp), nDay);
		}

		public long GetLocalYesterdayChangeDayTimeStamp()
		{
			return ToChangeDayTimeStamp(this.GetLocalGameDate(), -1.0);
		}

		public long GetLocalTomorrowChangeDayTimeStamp()
		{
			return ToChangeDayTimeStamp(this.GetLocalGameDate(), 1.0);
		}

		public long GetLocalTodayChangeDayTimeStamp()
		{
			return ToChangeDayTimeStamp(this.GetLocalGameDate(), 0.0);
		}

		public long GetLocalNextChangeDayTime(long localTime)
		{
			long changeDayMilliseconds = (long) TimeUtil.ChangeDayTime.TotalMilliseconds;
			return ToChangeDayTimeStamp(TimeUtil.UtcEpoch.AddMilliseconds(localTime - changeDayMilliseconds).Date, 1.0);
		}

		public long GetLocalLastMondayTimeStamp()
		{
			return GetLastDayOfWeekTimeStamp(this.GetLocalGameDate(), DayOfWeek.Monday);
		}

		// Grand Battle ranks and matches on Tuesday (in-game help: ranking at Tuesday 4:00 server time).
		public long GetLocalLastGrandBattleMatchingTimeStamp()
		{
			return GetLastDayOfWeekTimeStamp(this.GetLocalGameDate(), DayOfWeek.Tuesday);
		}

		public long GetLocalLastMondayTimeStamp(long timestamp)
		{
			return GetLastDayOfWeekTimeStamp(this.GetLocalGameDate(timestamp), DayOfWeek.Monday);
		}

		public long GetLocalNextMondayTimeStamp()
		{
			return GetNextDayOfWeekTimeStamp(this.GetLocalGameDate(), DayOfWeek.Monday);
		}

		public long GetLocalNextDayOfWeekTimeStamp(DayOfWeek dayOfWeek)
		{
			return GetNextDayOfWeekTimeStamp(this.GetLocalGameDate(), dayOfWeek);
		}

		public long GetLocalNextMondayTimeStamp(long timestamp)
		{
			return GetNextDayOfWeekTimeStamp(this.GetLocalGameDate(timestamp), DayOfWeek.Monday);
		}

		public long GetLocalNextMonthFirstDayTimeStamp()
		{
			DateTime dateTime = this.GetLocalTodayDateTime();
			return ToChangeDayTimeStamp(new DateTime(dateTime.Year, dateTime.Month, 1).AddMonths(1), 0.0);
		}

		public DayOfWeek GetDayOfWeek()
		{
			return this.GetLocalTodayDateTime().DayOfWeek;
		}

		public DateTime GetLocalTodayDateTime()
		{
			return this.GetLocalDateTime() - TimeUtil.ChangeDayTime;
		}

		public DateTime GetLocalGameDate()
		{
			return this.GetLocalTodayDateTime().Date;
		}

		public DateTime GetLocalGameDate(long localTimeStamp)
		{
			return (TimeUtil.UtcEpoch.AddMilliseconds(localTimeStamp) - TimeUtil.ChangeDayTime).Date;
		}

		public DateTime ConvertUtcTimeStampToLocalDateTime(long utcTimeStamp)
		{
			return TimeUtil.ConvertUtcTimeStampToDateTime(this.ConvertUtcTimeStampToLocalTimeStamp(utcTimeStamp));
		}

		public bool IsInTime(IHasStartEndTime data)
		{
			DateTime localDateTime = this.GetLocalDateTime();
			return ParseDateTime(data.StartTime) <= localDateTime && localDateTime <= ParseDateTime(data.EndTime);
		}

		// Each bound is compared with JST now unless the type makes that bound local.
		public bool IsInTime(IHasStartEndTimeZone data)
		{
			DateTime localDateTime = this.GetLocalDateTime();
			StartEndTimeZoneType type = data.StartEndTimeZoneType;
			DateTime now = type != StartEndTimeZoneType.LocalStartLocalEnd && type != StartEndTimeZoneType.LocalStartJstEnd ? TimeUtil.JstNowDateTime : localDateTime;
			if (ParseDateTime(data.StartTime) > now)
			{
				return false;
			}
			now = type != StartEndTimeZoneType.LocalStartLocalEnd && type != StartEndTimeZoneType.JstStartLocalEnd ? TimeUtil.JstNowDateTime : localDateTime;
			return now <= ParseDateTime(data.EndTime);
		}

		public bool IsInEventTime(IHasEventStartEndTime data)
		{
			DateTime localDateTime = this.GetLocalDateTime();
			return !string.IsNullOrEmpty(data.EventStartTime) && !string.IsNullOrEmpty(data.EventEndTime)
				&& ParseDateTime(data.EventStartTime) <= localDateTime && localDateTime <= ParseDateTime(data.EventEndTime);
		}

		public IHasStartEndTime GetInTimeData(IReadOnlyList<IHasStartEndTime> datas)
		{
			foreach (IHasStartEndTime data in datas)
			{
				if (this.IsInTime(data))
				{
					return data;
				}
			}
			return null;
		}

		public IHasJstStartEndTime GetInTimeData(IReadOnlyList<IHasJstStartEndTime> datas)
		{
			foreach (IHasJstStartEndTime data in datas)
			{
				if (this.IsInTime(data))
				{
					return data;
				}
			}
			return null;
		}

		public bool IsInTime(DateTime startTime, DateTime endTime)
		{
			DateTime localDateTime = this.GetLocalDateTime();
			return localDateTime >= startTime && localDateTime <= endTime;
		}

		public bool IsInTime(IHasJstStartEndTime data)
		{
			DateTime jstNowDateTime = TimeUtil.JstNowDateTime;
			return ParseDateTime(data.StartTimeFixJST) <= jstNowDateTime && jstNowDateTime <= ParseDateTime(data.EndTimeFixJST);
		}

		public bool IsInTimeByHourAndMinuteAndSecond(long startTime, long endTime, long timestamp)
		{
			int time = DateUtil.GetHoursMinutesAndSeconds(timestamp);
			return startTime <= time && time <= endTime;
		}

		public bool IsInTimeByHourAndMinuteAndSecond(long startTime, long endTime)
		{
			return this.IsInTimeByHourAndMinuteAndSecond(startTime, endTime, this.GetLocalTimestamp());
		}
//
// 		public long GetAddTime(DateAddTimeType type, long value, long utcTimeStamp = 0L)
// 		{
// 			DateTime UtcEpoch = TimeUtil.UtcEpoch;
// 			DateTime UtcEpoch2 = TimeUtil.UtcEpoch;
// 			long localTimestamp = this.GetLocalTimestamp();
// 			if (type <= DateAddTimeType.Years)
// 			{
// 				DateTime UtcEpoch3 = TimeUtil.UtcEpoch;
// 				DateTime dateTime;
// 				TimeSpan timeSpan = dateTime - UtcEpoch3;
// 			}
// 			throw new NullReferenceException();
// 		}
//
		public long ConvertUtcTimeStampToLocalTimeStamp(long utcTimeStamp)
		{
			return this.DifferenceFromUtc + utcTimeStamp;
		}
//
// 		public long ConvertLocalTimeStampToUtcTimeStamp(long localTimeStamp)
// 		{
// 			return localTimeStamp;
// 		}
//
		// JST = UTC+9, no DST.
		public long ConvertJstTimeStampToLocalTimeStamp(DateTime jstDateTime)
		{
			return TimeUtil.ConvertDateTimeToTimeStamp(jstDateTime.AddHours(-9.0)) + this.DifferenceFromUtc;
		}

		public DateTime ConvertJstDateTImeToLocalDateTime(DateTime jstDateTime)
		{
			TimeSpan timeSpan = jstDateTime - TimeUtil.JstNowDateTime;
			return this.GetLocalDateTime() + timeSpan;
		}
//
// 		public long GetLocalTodayUpdateLegendLeagueTimeStamp()
// 		{
// 			/*
// An exception occurred when decompiling this method
//
// ICSharpCode.Decompiler.DecompilerException: Error decompiling System.Int64 Ortega.Share.OrtegaTimeManager::GetLocalTodayUpdateLegendLeagueTimeStamp()
//
//  ---> System.Exception: Basic block has to end with unconditional control flow. 
// {
// 	Block_0:
// 	stloc:DateTime(var_0_06, call:DateTime(OrtegaTimeManager::GetLocalDateTime, ldloc:OrtegaTimeManager(this)))
// 	stloc:DateTime(var_6_11, ldsfld:DateTime(TimeUtil::UtcEpoch))
// 	stloc:TimeSpan(var_7_1C, call:TimeSpan(DateTime::op_Subtraction, ldloc:DateTime(var_5), ldloc:DateTime(var_6_11)))
// }
//
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.FlattenBasicBlocks(ILNode node) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 1813
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.Optimize(DecompilerContext context, ILBlock method, AutoPropertyProvider autoPropertyProvider, StateMachineKind& stateMachineKind, MethodDef& inlinedMethod, AsyncMethodDebugInfo& asyncInfo, ILAstOptimizationStep abortBeforeStep) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 347
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(IEnumerable`1 parameters, MethodDebugInfoBuilder& builder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 123
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    --- End of inner exception stack trace ---
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    at ICSharpCode.Decompiler.Ast.AstBuilder.AddMethodBody(EntityDeclaration methodNode, EntityDeclaration& updatedNode, MethodDef method, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, MethodKind methodKind) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstBuilder.cs:line 1627
// */;
// 		}
//
// 		public long GetLocalLastUpdateLegendLeagueTimeStamp()
// 		{
// 			long localTimestamp = this.GetLocalTimestamp();
// 			long localTodayUpdateLegendLeagueTimeStamp = this.GetLocalTodayUpdateLegendLeagueTimeStamp();
// 			TimeSpan timeSpan;
// 			return localTodayUpdateLegendLeagueTimeStamp - timeSpan;
// 		}
//
// 		public int GetLegendLeagueDayOfWeek()
// 		{
// 			/*
// An exception occurred when decompiling this method
//
// ICSharpCode.Decompiler.DecompilerException: Error decompiling System.Int32 Ortega.Share.OrtegaTimeManager::GetLegendLeagueDayOfWeek()
//
//  ---> System.Exception: Basic block has to end with unconditional control flow. 
// {
// 	Block_0:
// 	stloc:DateTime(var_0_06, call:DateTime(OrtegaTimeManager::GetLocalDateTime, ldloc:OrtegaTimeManager(this)))
// 	stloc:int64(var_1_0D, call:int64(OrtegaTimeManager::GetLocalTimestamp, ldloc:OrtegaTimeManager(this)))
// 	stloc:int64(var_2_14, call:int64(OrtegaTimeManager::GetLocalTodayUpdateLegendLeagueTimeStamp, ldloc:OrtegaTimeManager(this)))
// }
//
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.FlattenBasicBlocks(ILNode node) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 1813
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.Optimize(DecompilerContext context, ILBlock method, AutoPropertyProvider autoPropertyProvider, StateMachineKind& stateMachineKind, MethodDef& inlinedMethod, AsyncMethodDebugInfo& asyncInfo, ILAstOptimizationStep abortBeforeStep) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 347
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(IEnumerable`1 parameters, MethodDebugInfoBuilder& builder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 123
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    --- End of inner exception stack trace ---
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    at ICSharpCode.Decompiler.Ast.AstBuilder.AddMethodBody(EntityDeclaration methodNode, EntityDeclaration& updatedNode, MethodDef method, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, MethodKind methodKind) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstBuilder.cs:line 1627
// */;
// 		}
//
// 		public int GetYesterdayLegendLeagueDayOfWeek()
// 		{
// 			/*
// An exception occurred when decompiling this method
//
// ICSharpCode.Decompiler.DecompilerException: Error decompiling System.Int32 Ortega.Share.OrtegaTimeManager::GetYesterdayLegendLeagueDayOfWeek()
//
//  ---> System.Exception: Basic block has to end with unconditional control flow. 
// {
// 	Block_0:
// 	stloc:DateTime(var_0_06, call:DateTime(OrtegaTimeManager::GetLocalDateTime, ldloc:OrtegaTimeManager(this)))
// 	stloc:int64(var_1_0D, call:int64(OrtegaTimeManager::GetLocalTimestamp, ldloc:OrtegaTimeManager(this)))
// 	stloc:int64(var_2_14, call:int64(OrtegaTimeManager::GetLocalTodayUpdateLegendLeagueTimeStamp, ldloc:OrtegaTimeManager(this)))
// }
//
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.FlattenBasicBlocks(ILNode node) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 1813
//    at ICSharpCode.Decompiler.ILAst.ILAstOptimizer.Optimize(DecompilerContext context, ILBlock method, AutoPropertyProvider autoPropertyProvider, StateMachineKind& stateMachineKind, MethodDef& inlinedMethod, AsyncMethodDebugInfo& asyncInfo, ILAstOptimizationStep abortBeforeStep) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\ILAst\ILAstOptimizer.cs:line 347
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(IEnumerable`1 parameters, MethodDebugInfoBuilder& builder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 123
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    --- End of inner exception stack trace ---
//    at ICSharpCode.Decompiler.Ast.AstMethodBodyBuilder.CreateMethodBody(MethodDef methodDef, DecompilerContext context, AutoPropertyProvider autoPropertyProvider, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, StringBuilder sb, MethodDebugInfoBuilder& stmtsBuilder) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstMethodBodyBuilder.cs:line 99
//    at ICSharpCode.Decompiler.Ast.AstBuilder.AddMethodBody(EntityDeclaration methodNode, EntityDeclaration& updatedNode, MethodDef method, IEnumerable`1 parameters, Boolean valueParameterIsKeyword, MethodKind methodKind) in D:\a\dnSpy\dnSpy\Extensions\ILSpy.Decompiler\ICSharpCode.Decompiler\ICSharpCode.Decompiler\Ast\AstBuilder.cs:line 1627
// */;
// 		}
//
		public bool IsChangeDayByChangeDayTime(long timestamp)
		{
			return this.GetLocalGameDate(timestamp) < this.GetLocalGameDate();
		}
//
// 		public DateTime GetChangeDayLocalDateTime()
// 		{
// 			DateTime localDateTime = this.GetLocalDateTime();
// 			TimeSpan ChangeDayTime = TimeUtil.ChangeDayTime;
// 			DateTime dateTime = localDateTime - ChangeDayTime;
// 			TimeSpan ChangeDayTime2 = TimeUtil.ChangeDayTime;
// 			DateTime dateTime2;
// 			return dateTime2;
// 		}
//
		public int GetDateIntYearMonthDay()
		{
			long localTimestamp = this.GetLocalTimestamp();
			DateTime dateData = TimeUtil.UtcEpoch.AddMilliseconds(localTimestamp)- TimeUtil.ChangeDayTime;
			return (dateData.Year * 100 + dateData.Month) * 100 + dateData.Day;
		}

		public int GetDateIntYearMonthDay(long timeStamp)
		{
			return DateUtil.GetDateIntYearMonthDay(timeStamp);
		}

		private static DateTime ParseDateTime(string dateTime)
		{
			return DateTime.Parse(dateTime, CultureInfo.InvariantCulture);
		}

		// The change-day time (04:00) of gameDate + days, as a timestamp.
		private static long ToChangeDayTimeStamp(DateTime gameDate, double days)
		{
			return TimeUtil.ConvertDateTimeToTimeStamp(gameDate.AddDays(days) + TimeUtil.ChangeDayTime);
		}

		// Latest dayOfWeek on or before gameDate.
		private static long GetLastDayOfWeekTimeStamp(DateTime gameDate, DayOfWeek dayOfWeek)
		{
			int days = dayOfWeek - gameDate.DayOfWeek;
			if (days > 0)
			{
				days -= 7;
			}
			return ToChangeDayTimeStamp(gameDate, days);
		}

		// First dayOfWeek after gameDate.
		private static long GetNextDayOfWeekTimeStamp(DateTime gameDate, DayOfWeek dayOfWeek)
		{
			int days = dayOfWeek - gameDate.DayOfWeek;
			if (days <= 0)
			{
				days += 7;
			}
			return ToChangeDayTimeStamp(gameDate, days);
		}
	}
}
