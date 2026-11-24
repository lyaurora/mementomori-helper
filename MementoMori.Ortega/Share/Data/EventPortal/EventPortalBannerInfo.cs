using System.ComponentModel;
using MementoMori.Ortega.Share.Data.MyPage;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Utils;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.EventPortal;

[MessagePackObject(true)]
public class EventPortalBannerInfo
{
    [Description("バッジタイプ")]
    public BadgeType BadgeType { get; set; }

    [Description("バナー画像ID")]
    public long BannerImageId { get; set; }

    [Description("表示終了日時")]
    public long DisplayEndTimeStamp { get; set; }

    [Description("終了日時")]
    public long EndTimeStamp { get; set; }

    [Description("EventBannerMBのID")]
    public long EventBannerId { get; set; }

    [Description("アイコン名")]
    public string IconStringKey { get; set; }

    [Description("バッジを点灯させるか")]
    public bool IsDisplayBadge { get; set; }

    [Description("開放されているか")]
    public bool IsOpen { get; set; }

    [Description("ソート順")]
    public int SortOrder { get; set; }

    [Description("開始日時")]
    public long StartTimeStamp { get; set; }

    [Description("遷移タイプ")]
    public TransferDetailInfo TransferDetailInfo { get; set; }

    [Description("残り日数表示")]
    public bool IsRemainingDaysDisplayed { get; set; }

    [Description("イベント挑戦終了日時")]
    public long EventChallengeEndTimeStamp { get; set; }
}

[MessagePackObject(true)]
public class OutsideEventPortalBannerInfo
{
    [Description("イベントグループID")]
    public long EventGroupId { get; set; }

    [Description("イベントポータルバナー情報")]
    public EventPortalBannerInfo EventPortalBannerInfo { get; set; }

    [Description("タイトル画像ID")]
    public long TitleImageId { get; set; }

    [Description("タイトル名(TextResourceのStringKey)")]
    public string TitleTextKey { get; set; }
}

[MessagePackObject(true)]
public class EventPortalVideoInfo : IHasStartEndTimeZone
{
    [Description("時間タイプ")]
    public StartEndTimeZoneType StartEndTimeZoneType { get; set; }

    [Description("再生回数更新頻度")]
    public int PlaybackUpdateFrequencySeconds { get; set; }

    [Description("動画開始日時")]
    public string StartTime { get; set; }

    [Description("動画終了日時")]
    public string EndTime { get; set; }

    [Description("タイトルテキストキー")]
    public string TitleTextKey { get; set; }
}
