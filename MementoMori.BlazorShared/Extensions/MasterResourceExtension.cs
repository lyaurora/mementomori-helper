using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MementoMori.Common.Localization;
using MementoMori.Ortega.Share;

namespace MementoMori.BlazorShared.Extensions
{
    public static class MasterResourceExtension
    {
        public static string Tr(string? key)
        {
            return Masters.TextResourceTable.Get(key);
        }
        public static string Tr(string? key, params object[] param)
        {
            return Masters.TextResourceTable.Get(key, param);
        }
        public static string Tr<T>(T enumValue) where T : Enum
        {
            return Masters.TextResourceTable.Get(enumValue);
        }
        public static string Tr<T>(T enumValue, params object[] param) where T : Enum
        {
            return Masters.TextResourceTable.Get(enumValue, param);
        }

        // Full N0 below 1e8 (zh/ja/ko) or 1e9 (others); above that truncate to one decimal so balances are never overstated.
        public static string Compact(long n)
        {
            var unit = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "zh" or "ja" or "ko" ? 100_000_000L : 1_000_000_000L;
            return Math.Abs(n) < unit ? n.ToString("N0")
                : (Math.Truncate(n * 10.0 / unit) / 10).ToString("0.#") + ResourceStrings.NumberUnitLarge;
        }
    }
}
