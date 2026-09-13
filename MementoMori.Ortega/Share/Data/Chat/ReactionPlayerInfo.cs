using MementoMori.Ortega.Share.Data.Interface;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Chat;

[MessagePackObject(true)]
public class ReactionPlayerInfo : IPlayerIconInfo
{
    public string PlayerName { get; set; }
    public long IconId { get; set; }
    public long IconEffectId { get; set; }
    public LegendLeagueClassType LegendLeagueClassType { get; set; }

    long IPlayerIconInfo.GetIconId() => IconId;
    long IPlayerIconInfo.GetIconEffectId() => IconEffectId;
    LegendLeagueClassType IPlayerIconInfo.GetLegendLeagueClass() => LegendLeagueClassType;
}
