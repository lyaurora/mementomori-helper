using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Shop;

[MessagePackObject(true)]
public class RarityItem
{
    public CharacterRarityFlags RarityFlags { get; set; }
    public UserItem Item { get; set; }
}
