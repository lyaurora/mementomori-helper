using Injectio.Attributes;

namespace MementoMori.BlazorShared.UI;

/// <summary>
/// 将前端实际生效的明暗主题通知给宿主平台（如 Android 状态栏与系统栏底色同步）。
/// </summary>
[RegisterSingleton]
public sealed class PlatformThemeSync
{
    public string Mode { get; private set; } = "system";
    public bool? IsDark { get; private set; }

    public event Action<bool>? ThemeChanged;

    public void NotifyTheme(string mode, bool isDark)
    {
        Mode = mode is "light" or "dark" ? mode : "system";
        if (IsDark == isDark) return;
        IsDark = isDark;
        ThemeChanged?.Invoke(isDark);
    }
}
