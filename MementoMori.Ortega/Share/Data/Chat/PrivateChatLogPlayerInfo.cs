using MementoMori.Ortega.Share.Data.Player;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Chat;

[MessagePackObject(false)]
public class PrivateChatLogPlayerInfo
{
    [Key(0)] public bool ExistUnread { get; set; }
    [Key(1)] public PlayerInfo PlayerInfo { get; set; }
    [Key(2)] public long LocalTimestamp { get; set; }
}
