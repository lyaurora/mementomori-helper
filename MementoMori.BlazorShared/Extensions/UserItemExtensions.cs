using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;

namespace MementoMori.WebUI.Extensions;

public static class UserItemExtensions
{
    public class EquipmentInfo
    {
        public long Count { get; set; }
        public EquipmentMB EquipmentMb { get; set; }
        public string Name { get; set; }
        public UserEquipmentDtoInfo Info { get; set; }
    }

    public static EquipmentInfo ToEquipmentInfo(this IUserItem userItem)
    {
        var equipmentMb = Masters.EquipmentTable.GetById(userItem.ItemId);
        return new EquipmentInfo
        {
            Count = userItem.ItemCount,
            EquipmentMb = equipmentMb,
            Name = ItemUtil.GetItemName(ItemType.Equipment, userItem.ItemId)
        };
    }

    public static EquipmentInfo ToEquipmentInfo(this UserEquipmentDtoInfo userItem)
    {
        var equipmentMb = Masters.EquipmentTable.GetById(userItem.EquipmentId);
        return new EquipmentInfo
        {
            Count = 1,
            EquipmentMb = equipmentMb,
            Name = ItemUtil.GetItemName(ItemType.Equipment, userItem.EquipmentId),
            Info = userItem
        };
    }

    public class UserItemInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public long Count { get; set; }
        public ItemRarityFlags ItemRarityFlags { get; set; }
        public long MaxItemCount { get; set; }
        public IUserItem Item { get; set; }
    }

    public static UserItemInfo ToUserItemInfo(this IUserItem userItem)
    {
        // Rows can be missing until the local master refreshes (new item after a game update).
        UserItemInfo Missing() => new() {Name = ItemUtil.GetItemName(userItem), Count = userItem.ItemCount, Item = userItem};

        if (userItem.ItemType == ItemType.TreasureChest)
        {
            var treasureChestMb = Masters.TreasureChestTable.GetById(userItem.ItemId);
            if (treasureChestMb == null) return Missing();
            return new UserItemInfo
            {
                Name = Masters.TextResourceTable.Get(treasureChestMb.NameKey),
                Description = Masters.TextResourceTable.Get(treasureChestMb.DescriptionKey),
                Count = userItem.ItemCount,
                ItemRarityFlags = treasureChestMb.ItemRarityFlags,
                MaxItemCount = treasureChestMb.MaxItemCount,
                Item = userItem
            };
        }
        else if (userItem.ItemType == ItemType.CharacterFragment)
        {
            var characterMb = Masters.CharacterTable.GetById(userItem.ItemId);
            if (characterMb == null) return Missing();
            var characterName = Masters.TextResourceTable.Get(characterMb.NameKey);
            return new UserItemInfo()
            {
                Name = Masters.TextResourceTable.Get("[CommonItemCharacterFragment]", characterName),
                Description = Masters.TextResourceTable.Get("[ItemTypeCharacterFragmentDescription]", characterName, 60),
                Count = userItem.ItemCount,
                ItemRarityFlags = characterMb.ItemRarityFlags,
                MaxItemCount = 0,
                Item = userItem
            };
        }
        else if (userItem.ItemType == ItemType.EquipmentSetMaterial)
        {
            var equipmentSetMaterialMb = Masters.EquipmentSetMaterialTable.GetById(userItem.ItemId);
            if (equipmentSetMaterialMb == null) return Missing();
            var name = $"{Masters.TextResourceTable.Get(equipmentSetMaterialMb.NameKey)} Lv{equipmentSetMaterialMb.Lv}";
            return new UserItemInfo()
            {
                Name = name,
                Description = Masters.TextResourceTable.Get(equipmentSetMaterialMb.DescriptionKey),
                Count = userItem.ItemCount,
                ItemRarityFlags = equipmentSetMaterialMb.ItemRarityFlags,
                MaxItemCount = 0,
                Item = userItem
            };
        }
        else if (userItem.ItemType == ItemType.EquipmentFragment)
        {
            var equipmentCompositeMb = Masters.EquipmentCompositeTable.GetById(userItem.ItemId);
            var equipmentMb = equipmentCompositeMb == null ? null : Masters.EquipmentTable.GetById(equipmentCompositeMb.EquipmentId);
            if (equipmentMb == null) return Missing();
            var equipmentName = Masters.TextResourceTable.Get(equipmentMb.NameKey);
            return new UserItemInfo()
            {
                Name = Masters.TextResourceTable.Get("[CommonItemEquipmentFragmentFormat]", equipmentName),
                Description = Masters.TextResourceTable.Get("[ItemTypeEquipmentFragmentDescription]", equipmentName, equipmentCompositeMb!.RequiredFragmentCount),
                Count = userItem.ItemCount,
                ItemRarityFlags = (ItemRarityFlags) equipmentMb.RarityFlags,
                MaxItemCount = 0,
                Item = userItem
            };
        }
        else
        {
            var itemMb = Masters.ItemTable.GetByItemTypeAndItemId(userItem.ItemType, userItem.ItemId);
            if (itemMb == null) return Missing();
            return new UserItemInfo
            {
                Name = Masters.TextResourceTable.Get(itemMb.NameKey),
                Description = Masters.TextResourceTable.Get(itemMb.DescriptionKey),
                Count = userItem.ItemCount,
                ItemRarityFlags = itemMb.ItemRarityFlags,
                MaxItemCount = itemMb.MaxItemCount,
                Item = userItem
            };
        }
    }

    public class SphereInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public long Count { get; set; }
        public SphereMB SphereMB { get; set; }
        public long MaxItemCount { get; set; }
    }

    public static SphereInfo ToSphereInfo(this IUserItem userItem)
    {
        var sphereMb = Masters.SphereTable.GetById(userItem.ItemId);
        return new SphereInfo
        {
            Name = ItemUtil.GetItemName(ItemType.Sphere, userItem.ItemId),
            Description = Masters.TextResourceTable.Get(sphereMb?.DescriptionKey),
            Count = userItem.ItemCount,
            SphereMB = sphereMb,
            MaxItemCount = 0
        };
    }


    // Rows can be missing until the local master refreshes (new item after a game update); ItemUtil returns "type:id" for those.
    public static string GetItemName(this IUserItem item)
    {
        if (item.ItemType == ItemType.Equipment)
        {
            var equipmentMb = Masters.EquipmentTable.GetById(item.ItemId);
            if (equipmentMb == null) return ItemUtil.GetItemName(item);
            return $"{Masters.TextResourceTable.Get(equipmentMb.NameKey)} Lv{equipmentMb.EquipmentLv}";
        }
        if (item.ItemType == ItemType.TreasureChest)
        {
            return ItemUtil.GetItemName(item);
        }

        if (item.ItemType == ItemType.CharacterFragment)
        {
            var characterMb = Masters.CharacterTable.GetById(item.ItemId);
            if (characterMb == null) return ItemUtil.GetItemName(item);
            var characterName = Masters.TextResourceTable.Get(characterMb.NameKey);
            return Masters.TextResourceTable.Get("[CommonItemCharacterFragment]", characterName);
        }

        if (item.ItemType == ItemType.EquipmentFragment)
        {
            return ItemUtil.GetItemName(item);
        }

        if (item.ItemType == ItemType.EquipmentSetMaterial)
        {
            var equipmentSetMaterialMb = Masters.EquipmentSetMaterialTable.GetById(item.ItemId);
            if (equipmentSetMaterialMb == null) return ItemUtil.GetItemName(item);
            var name = $"{Masters.TextResourceTable.Get(equipmentSetMaterialMb.NameKey)} Lv{equipmentSetMaterialMb.Lv}";
            return name;
        }

        if (item.ItemType == ItemType.Sphere)
        {
            var sphereMb = Masters.SphereTable.GetById(item.ItemId);
            if (sphereMb == null) return ItemUtil.GetItemName(item);
            return $"{Masters.TextResourceTable.Get(sphereMb.NameKey)} Lv{sphereMb.Lv}";
        }

        if (item.ItemType == ItemType.Character)
        {
            var characterMb = Masters.CharacterTable.GetById(item.ItemId);
            if (characterMb == null) return ItemUtil.GetItemName(item);
            return Masters.TextResourceTable.Get(characterMb.NameKey);
        }

        var itemMb = Masters.ItemTable.GetByItemTypeAndItemId(item.ItemType, item.ItemId);
        if (itemMb == null) return ItemUtil.GetItemName(item);
        return Masters.TextResourceTable.Get(itemMb.NameKey);
    }
}