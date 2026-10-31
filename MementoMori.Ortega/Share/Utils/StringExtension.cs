using System.Text;

namespace MementoMori.Ortega.Share.Utils
{
	public static class StringExtension
	{
		private static Encoding ByteConvertEncoding
		{
			get;
		}

		public static byte[] ToBytes(this string target)
		{
			if (target.Length != 0)
			{
				return ByteConvertEncoding.GetBytes(target);
			}
			return Array.Empty<byte>();
		}

		public static byte[] ToHexBytes(this string target)
		{
			// bool flag;
			// if (flag)
			// {
			// }
			// if (target != 0)
			// {
			// }
			// InvalidCastException ex = new InvalidCastException("invalid hex string: " + target);
			// throw new NullReferenceException();
			throw new NotImplementedException();
		}

		public static string ToHexString(this byte[] target)
		{
			// Func<byte, string> <>9__5_ = StringExtension.<>c.<>9__5_0;
			// if (<>9__5_ == 0)
			// {
			// 	Func<byte, string> func;
			// 	StringExtension.<>c.<>9__5_0 = func;
			// }
			// return string.Concat(Enumerable.Select<byte, string>(target, <>9__5_));
			throw new NotImplementedException();
		}

		public static string ToLowerFirst(this string target)
		{
			if (!string.IsNullOrEmpty(target))
			{
				char c = target[0];
				if (char.IsUpper(c))
				{
					if (target.Length == 1)
					{
						return target.ToLower();
					}
					c = char.ToLower(c);
					string text = target.Substring(1);
					return string.Format("{0}{1}", c, text);
				}
			}
			return target;
		}

		public static string ToUpperFirst(this string target)
		{
			if (!string.IsNullOrEmpty(target))
			{
				char c = target[0];
				if (char.IsLower(c))
				{
					if (target.Length != 1)
					{
						c = char.ToUpper(c);
						string text = target.Substring(1);
						return string.Format("{0}{1}", c, text);
					}
					return target.ToUpper();
				}
			}
			return target;
		}

		public static string ToUTF8String(this byte[] target)
		{
			if (target.Length != 0)
			{
				return ByteConvertEncoding.GetString(target);
			}
			return string.Empty;
		}

		public static int ToInt(this string target)
		{
			int.TryParse(target, out int num);
			return num;
		}

		public static long ToLong(this string target)
		{
			long.TryParse(target, out long num);
			return num;
		}

		public static float ToFloat(this string target)
		{
			float.TryParse(target, out float num);
			return num;
		}

		public static DateTime ToDateTime(this string str)
		{
			return DateTime.Parse(str);
		}

		public static TimeSpan ToTimeSpan(this string str)
		{
			return TimeSpan.Parse(str);
		}

		// Note: this type is marked as 'beforefieldinit'.
		static StringExtension()
		{
			ByteConvertEncoding = Encoding.UTF8;
		}
	}
}
