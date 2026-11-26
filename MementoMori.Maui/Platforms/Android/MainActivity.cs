using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using MementoMori.BlazorShared.UI;
using AndroidColor = Android.Graphics.Color;
using AndroidView = Android.Views.View;

namespace MementoMori.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity, AndroidView.IOnApplyWindowInsetsListener
{
    private const int RequestPostNotifications = 1002;
    private static readonly AndroidColor LightBgColor = AndroidColor.Rgb(0xF3, 0xEE, 0xE7);
    private static readonly AndroidColor DarkBgColor = AndroidColor.Rgb(0x13, 0x13, 0x12);

    private AndroidView? _contentRoot;
    private PlatformThemeSync? _themeSync;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _contentRoot = FindViewById(Android.Resource.Id.Content);
        if (_contentRoot != null)
        {
            // Android 15 (API 35) 强制 edge-to-edge：把系统栏避让加在外层内容容器而非 WebView 本身，
            // 避免 Chromium 忽略 WebView padding 导致的点击坐标偏移。
            _contentRoot.SetOnApplyWindowInsetsListener(this);
            _contentRoot.RequestApplyInsets();
        }

        _themeSync = IPlatformApplication.Current?.Services.GetService<PlatformThemeSync>();
        if (_themeSync != null)
        {
            _themeSync.ThemeChanged += OnThemeChanged;
            ApplySystemBarTheme(_themeSync.IsDark ?? IsSystemDarkMode());
        }
        else
        {
            ApplySystemBarTheme(IsSystemDarkMode());
        }

        EnsureNotificationPermission();
        StartKeepAliveService();
    }

    protected override void OnDestroy()
    {
        if (_themeSync != null) _themeSync.ThemeChanged -= OnThemeChanged;
        base.OnDestroy();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (_themeSync?.Mode is null or "system")
        {
            ApplySystemBarTheme((newConfig.UiMode & UiMode.NightMask) == UiMode.NightYes);
        }
    }

    public WindowInsets? OnApplyWindowInsets(AndroidView? v, WindowInsets? insets)
    {
        if (v == null || insets == null) return insets;

        int left, top, right, bottom;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var i = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
            (left, top, right, bottom) = (i.Left, i.Top, i.Right, i.Bottom);
        }
        else
        {
#pragma warning disable CA1422, CS0618
            (left, top, right, bottom) = (insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CA1422, CS0618
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && insets.DisplayCutout is { } cutout)
            {
                left = Math.Max(left, cutout.SafeInsetLeft);
                top = Math.Max(top, cutout.SafeInsetTop);
                right = Math.Max(right, cutout.SafeInsetRight);
                bottom = Math.Max(bottom, cutout.SafeInsetBottom);
            }
        }

        if (v.PaddingLeft != left || v.PaddingTop != top || v.PaddingRight != right || v.PaddingBottom != bottom)
            v.SetPadding(left, top, right, bottom);

        return insets;
    }

#pragma warning disable CA1422, CS0618
    public override void OnBackPressed()
    {
        // 返回键切到后台而不是销毁进程，保持前台保活服务与定时任务继续运行
        MoveTaskToBack(true);
    }
#pragma warning restore CA1422, CS0618

    private void OnThemeChanged(bool isDark)
    {
        RunOnUiThread(() => ApplySystemBarTheme(isDark));
    }

    private bool IsSystemDarkMode() => (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;

    private void ApplySystemBarTheme(bool isDark)
    {
        var color = isDark ? DarkBgColor : LightBgColor;
        _contentRoot?.SetBackgroundColor(color);

        if (Window == null) return;

        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1422, CS0618
            Window.SetStatusBarColor(color);
            Window.SetNavigationBarColor(color);
#pragma warning restore CA1422, CS0618
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (Window.InsetsController is not { } controller) return;
            var lightMask = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
            controller.SetSystemBarsAppearance(isDark ? 0 : lightMask, lightMask);
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
#pragma warning disable CA1422, CS0618
            var flags = SystemUiFlags.LayoutStable;
            if (!isDark)
            {
                flags |= SystemUiFlags.LightStatusBar;
                if (OperatingSystem.IsAndroidVersionAtLeast(26)) flags |= SystemUiFlags.LightNavigationBar;
            }
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
#pragma warning restore CA1422, CS0618
        }
    }

    private void EnsureNotificationPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted) return;
        RequestPermissions([Android.Manifest.Permission.PostNotifications], RequestPostNotifications);
    }

    private void StartKeepAliveService()
    {
        try
        {
            var intent = new Intent(this, typeof(KeepAliveService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                StartForegroundService(intent);
            else
                StartService(intent);
        }
        catch (Exception)
        {
            // 部分机型限制后台拉起前台服务时不阻塞界面启动
        }
    }
}
