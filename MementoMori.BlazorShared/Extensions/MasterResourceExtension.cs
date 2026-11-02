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

        // Full N0 below a million so small balances stay exact; above that 万/亿 (zh/ja/ko) or M/B, truncated to one decimal
        // so balances are never overstated. A million is where 7+ digit numbers stop fitting the home tiles.
        public static string Compact(long n)
        {
            var cjk = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "zh" or "ja" or "ko";
            var (large, medium) = cjk ? (100_000_000L, 10_000L) : (1_000_000_000L, 1_000_000L);
            string Scaled(long unit, string suffix) => (Math.Truncate(n * 10.0 / unit) / 10).ToString("0.#") + suffix;
            return Math.Abs(n) >= large ? Scaled(large, ResourceStrings.NumberUnitLarge)
                : Math.Abs(n) >= 1_000_000 ? Scaled(medium, ResourceStrings.NumberUnitMedium)
                : n.ToString("N0");
        }
    }
}
