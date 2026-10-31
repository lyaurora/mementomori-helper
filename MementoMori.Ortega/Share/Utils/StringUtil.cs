using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MementoMori.Ortega.Share.Utils
{
	public static class StringUtil
	{
		public static Regex RegexNewLine
		{
			get;
		} = new Regex("\n");

		private static Encoding ByteConvertEncoding
		{
			get;
		} = Encoding.UTF8;

		public static string CreateGuid()
		{
			Guid guid = Guid.NewGuid();
			return guid.ToString( "N" );
		}

		public static string FromGzip(Stream archiveStream, [Optional] Encoding encoding, bool seekFirst = true)
		{
			if (!archiveStream.CanRead)
			{
				throw new ArgumentException("cannot read stream", "archiveStream");
			}
			_ = archiveStream.Length; // read by the client (throws on non-seekable streams)
			encoding ??= ByteConvertEncoding;
			if (seekFirst && archiveStream.CanSeek)
			{
				archiveStream.Seek(0L, SeekOrigin.Begin);
			}
			using GZipStream gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress, true);
			using StreamReader streamReader = new StreamReader(gzipStream, encoding);
			return streamReader.ReadToEnd();
		}

		public static string FromGzip(byte[] archiveBinary, [Optional] Encoding encoding)
		{
			if (archiveBinary.Length == 0)
			{
				return string.Empty;
			}
			using MemoryStream memoryStream = new MemoryStream(archiveBinary);
			return FromGzip(memoryStream, ByteConvertEncoding, false);
		}

		public static string RemoveSpace(string str)
		{
			if (!string.IsNullOrEmpty(str))
			{
				return Regex.Replace(str, "\\s", "");
			}
			return string.Empty;
		}

		public static bool TryParseHexBytes(string hexString, [Out] byte[] bytes)
		{
			// string hexString2 = hexString;
			// if (hexString2._stringLength != 0)
			// {
			// 	if (hexString2._stringLength == 1)
			// 	{
			// 		Func<char, bool> <>9__10_ = StringUtil.<>c.<>9__10_0;
			// 		if (<>9__10_ == 0)
			// 		{
			// 			Func<char, bool> func;
			// 			StringUtil.<>c.<>9__10_0 = func;
			// 		}
			// 		if (!Enumerable.Any<char>(hexString2, <>9__10_))
			// 		{
			// 			int num = hexString._stringLength;
			// 			int num2 = 0;
			// 			num -= <>9__10_;
			// 			IEnumerable<int> enumerable = Enumerable.Range(num2, num);
			// 			Func<int, byte> func2 = (int i) => Convert.ToByte(hexString.Substring(i, 2), 16);
			// 			byte[] array = Enumerable.ToArray<byte>(Enumerable.Select<int, byte>(enumerable, func2));
			// 		}
			// 	}
			// }
			// bytes.m_value = hexString2;
			// throw new NullReferenceException();
			throw new NotImplementedException();
		}

		public static int CountNewLine(string text)
		{
			return StringUtil.RegexNewLine.Matches(text).Count;
		}

		// Note: this type is marked as 'beforefieldinit'.
		static StringUtil()
		{
			// throw new NullReferenceException();
		}
	}
}
