using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using MementoMori.Common.Localization;

namespace MementoMori.Maui;

/// <summary>
/// 前台保活服务：在后台和锁屏状态下维持定时任务与网络连接不被系统轻易回收，
/// 常驻通知提供“停止”按钮用于彻底退出进程。
/// </summary>
[Register("io.github.lyaurora.mementomorihelper.KeepAliveService")]
public sealed class KeepAliveService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "mementomori_helper_keepalive";
    private const string ActionStop = "io.github.lyaurora.mementomorihelper.action.STOP";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            Java.Lang.JavaSystem.Exit(0);
            return StartCommandResult.NotSticky;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = GetSystemService(NotificationService) as NotificationManager;
            var channel = new NotificationChannel(ChannelId, ResourceStrings.GameAssistant, NotificationImportance.Low);
            manager?.CreateNotificationChannel(channel);
        }

        var openIntent = new Intent(this, typeof(MainActivity));
        openIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var openPending = PendingIntent.GetActivity(this, 0, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var stopIntent = new Intent(this, typeof(KeepAliveService)).SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 1, stopIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var iconRes = ApplicationInfo?.Icon is > 0 ? ApplicationInfo.Icon : Android.Resource.Drawable.IcDialogInfo;

#pragma warning disable CA1422, CS0618
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
#pragma warning restore CA1422, CS0618

        var notification = builder
            .SetContentTitle(ResourceStrings.AndroidKeepAliveTitle)
            .SetContentText(ResourceStrings.AndroidKeepAliveText)
            .SetSmallIcon(iconRes)
            .SetContentIntent(openPending)
            .SetOngoing(true)
            .AddAction(new Notification.Action.Builder(null, ResourceStrings.AndroidKeepAliveStop, stopPending).Build())
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        return StartCommandResult.Sticky;
    }
}
