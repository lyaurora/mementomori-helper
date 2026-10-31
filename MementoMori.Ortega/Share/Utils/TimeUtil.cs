using MementoMori.Common;
using Ortega.Common.Manager;

namespace MementoMori.Ortega.Share.Utils
{
	public static class TimeUtil
	{
		public static TimeSpan ChangeDayTime { get; } = TimeSpan.FromHours(4);

		public static DateTime UtcEpoch { get; } = new DateTime(1970, 1, 1, 0, 0, 0, 1, DateTimeKind.Unspecified);

		public static DateTime UtcNowDateTime
		{
			get
			{
				DateTime dateTime = TimeUtil.UtcEpoch;
				long utcNowTimeStamp = TimeUtil.UtcNowTimeStamp;
				return dateTime.AddMilliseconds(utcNowTimeStamp);
			}
		}

		// JST = UTC+9, no DST.
		public static DateTime JstNowDateTime => UtcNowDateTime.AddHours(9.0);

		// The client adds NetworkManager.DiffServerTimeMilliSeconds (its server clock correction); nothing here
		// sets it, so this is the machine clock, the same value OrtegaTimeManager.GetLocalTimestamp uses.
		public static long UtcNowTimeStamp => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		public static long UtcTodayMidnightTimeStamp => ConvertDateTimeToTimeStamp(UtcNowDateTime.Date);

		public static long UtcTomorrowMidnightTimeStamp => ConvertDateTimeToTimeStamp(UtcNowDateTime.Date.AddDays(1.0));

		public static long GetLocalTimestamp(ILocalTime local)
		{
			return UtcNowTimeStamp + local.DifferenceFromUtc;
		}

		public static DateTime GetLocalDateTime(ILocalTime local)
		{
			return ConvertUtcTimeStampToDateTime(GetLocalTimestamp(local));
		}

		public static DateTime ConvertUtcTimeStampToDateTime(long utcTimeStamp)
		{
			return UtcEpoch.AddMilliseconds(utcTimeStamp);
		}

		public static long ConvertDateTimeToTimeStamp(DateTime dateTime)
		{
			return (long) (dateTime - UtcEpoch).TotalMilliseconds;
		}

		[Obsolete("時差考慮する")]
		public static long AddDaysTimeStamp(int day)
		{
			return UtcTimeCalculation(UtcNowDateTime.AddDays(day));
		}
//
// 		[Obsolete("時差考慮する")]
// 		public static long GetAddTime(long utcTimeStamp, DateAddTimeType type, long value)
// 		{
// 			DateTime dateTime = TimeUtil.UtcEpoch;
// 			if (type <= DateAddTimeType.Years)
// 			{
// 				DateTime dateTime2;
// 				return TimeUtil.UtcTimeCalculation(dateTime2);
// 			}
// 			throw new NullReferenceException();
// 		}
//
		public static long UtcTimeCalculation(DateTime dateTime)
		{
			return (long) (dateTime - UtcEpoch).TotalMilliseconds;
		}

		public static DateTime GetGameDate(long utcTimeStamp)
		{
			return (ConvertUtcTimeStampToDateTime(utcTimeStamp) - ChangeDayTime).Date;
		}

		public static DateTime GetGameDate(DateTime dateTime)
		{
			return (dateTime - ChangeDayTime).Date;
		}

	}
}
