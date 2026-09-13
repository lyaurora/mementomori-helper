using MessagePack;

namespace MementoMori.Ortega.Share.Data.Chat;

[MessagePackObject(false)]
public class AnnounceChatInfo
{
    [Key(0)] public GuildChatInfo GuildChatInfo { get; set; }
    [Key(1)] public long RegisterLocalTimestamp { get; set; }
}
