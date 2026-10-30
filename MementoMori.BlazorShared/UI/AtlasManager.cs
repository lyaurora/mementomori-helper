using AutoCtor;
using Injectio.Attributes;
using MementoMori.Option;
using MementoMori.Ortega.Common;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;

namespace MementoMori.WebUI.UI
{
    [RegisterSingleton]
    [AutoConstruct]
    public partial class AtlasManager
    {
        private readonly IWritableOptions<GameConfig> _config;
        /// <summary>Set by the WebUI host to its /assets disk-cache proxy; null (MAUI) = load straight from the resource server.</summary>
        public static string? LocalAssetsUrl { get; set; }
        public string AssetsUrl => LocalAssetsUrl ?? _config.Value.AssetsUrl.TrimEnd('/');

        public string GetCharacterIcon(long characterId)
        {
            return $"{AssetsUrl}/AddressableConvertAssets/CharacterIcon/CHR_{characterId:000000}/CHR_{characterId:000000}_00_s.png";
        }

        public string? GetPlayerIcon(long iconId)
        {
            if (iconId == 0) return null;
            if (iconId > 0) return GetCharacterIcon(iconId);
            // PlayerIconUtil uses the sign bit to distinguish SpecialIconItemMB IDs.
            var special = MementoMori.Ortega.Share.Masters.SpecialIconItemTable.GetById(iconId & long.MaxValue);
            return special == null ? null : $"{AssetsUrl}/AddressableConvertAssets/CharacterIcon/CHR_{special.CharacterId:000000}/CHR_{special.CharacterId:000000}_00_em_{special.IconId:000}.png";
        }

        public string GetCharacterFrame(CharacterRarityFlags characterRarityFlags)
        {
            if (characterRarityFlags > CharacterRarityFlags.LR)
            {
                characterRarityFlags = CharacterRarityFlags.LR;
            }

            return characterRarityFlags switch
            {
                CharacterRarityFlags.None => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_watercolor.png",
                < CharacterRarityFlags.LR => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_slice.png",
                _ => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_lr_slice.png"
            };
        }

        public string GetItemFrame(ItemRarityFlags itemRarityFlags)
        {
            if (itemRarityFlags > ItemRarityFlags.LR)
            {
                itemRarityFlags = ItemRarityFlags.LR;
            }

            return itemRarityFlags switch
            {
                ItemRarityFlags.None => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_watercolor.png",
                < ItemRarityFlags.LR => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_slice.png",
                _ => $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_common_lr_slice.png"
            };
        }

        public string GetCharacterFrameDecoration(CharacterRarityFlags characterRarityFlags)
        {
            if ((ClientConst.Icon.NeedFrameDecorationCharacterRarityFlags & characterRarityFlags) == characterRarityFlags)
            {
                return $"{AssetsUrl}/AddressableLocalAssets/Atlas/frame_decoration_{characterRarityFlags.ToString().ToLower()}.png";
            }

            return null;
        }

        public string GetIconCharacterElement(ElementType elementType)
        {
            return $"{AssetsUrl}/AddressableLocalAssets/Atlas/icon_element_{(int) elementType}.png";
        }

        public string? GetEquipmentIcon(EquipmentMB? mb) => mb == null ? null : $"{AssetsUrl}/AddressableConvertAssets/Icon/Equipment/EQP_{mb.IconId:000000}.png";

        // Paths verified with HEAD requests (2026-10-29); EquipmentSetMaterial has no known icon path, so it stays null.
        public string? GetItemIcon(ItemType type, long itemId) => type switch
        {
            ItemType.CharacterFragment => GetCharacterIcon(itemId),
            ItemType.Equipment => GetEquipmentIcon(MementoMori.Ortega.Share.Masters.EquipmentTable.GetById(itemId)),
            ItemType.EquipmentFragment => MementoMori.Ortega.Share.Masters.EquipmentCompositeTable.GetById(itemId) is { } composite
                ? GetEquipmentIcon(MementoMori.Ortega.Share.Masters.EquipmentTable.GetById(composite.EquipmentId)) : null,
            // Unidentified spheres, sealed chests, witch letters/heart shards and gift boxes are all TreasureChest.
            ItemType.TreasureChest => MementoMori.Ortega.Share.Masters.TreasureChestTable.GetById(itemId) is { IconId: > 0 } chest
                ? $"{AssetsUrl}/AddressableConvertAssets/Icon/Item/Item_{chest.IconId:0000}.png" : null,
            ItemType.EquipmentSetMaterialBox => MementoMori.Ortega.Share.Masters.EquipmentSetMaterialBoxTable.GetById(itemId) is { IconId: > 0 } box
                ? $"{AssetsUrl}/AddressableConvertAssets/Icon/Item/Item_{box.IconId:0000}.png" : null,
            _ => MementoMori.Ortega.Share.Masters.ItemTable.GetByItemTypeAndItemId(type, itemId) is { IconId: > 0 } mb
                ? $"{AssetsUrl}/AddressableConvertAssets/Icon/Item/Item_{mb.IconId:0000}.png" : null
        };

        // Banner 1 only exists under LocalAssets (ConvertAssets returns 500); all other numbers are the reverse.
        public string GetGachaBanner(int n) => n == 1
            ? $"{AssetsUrl}/AddressableLocalAssets/Banner/Gacha/GACHA_BANNER_000001.png"
            : $"{AssetsUrl}/AddressableConvertAssets/Banner/Gacha/GACHA_BANNER_{n:000000}.png";

        public string GetMypageIcon(long imageId) => $"{AssetsUrl}/AddressableConvertAssets/Icon/MyPage/MYPAGE_ICON_{imageId:000000}.png";

        public string GetSphereIcon(SphereMB sphereMb, bool isMini = false)
        {
            var filename = $"SPH_{sphereMb.CategoryId:00}{(int) (isMini ? SphereType.EquipmentIcon : sphereMb.SphereType):00}";
            return $"{AssetsUrl}/AddressableLocalAssets/Icon/Sphere/{filename}.png";
        }
    }
}
