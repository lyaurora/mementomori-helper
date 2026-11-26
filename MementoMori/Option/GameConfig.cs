using MementoMori.Ortega.Share.Data.Item;

namespace MementoMori;

public class GameConfig
{
    public enum AutoUseItemType
    {
        Others,

        /// <summary>
        ///     一袋钻石
        /// </summary>
        DiamondBag,

        /// <summary>
        ///     未鉴定符石
        /// </summary>
        MysteryRune,

        /// <summary>
        ///     魔女的来信 R
        /// </summary>
        WitchLetterR,

        /// <summary>
        ///     魔女的来信 SR
        /// </summary>
        WitchLetterSr,

        /// <summary>
        ///     魔女的心片
        /// </summary>
        WitchShard,

        /// <summary>
        ///     小壶
        /// </summary>
        Pot,

        /// <summary>
        ///     大壶
        /// </summary>
        Amphora,

        /// <summary>
        ///     封印宝箱
        /// </summary>
        SealedChest,

        /// <summary>
        ///     魔女的来信礼盒
        /// </summary>
        WitchLetterGift,

        /// <summary>
        ///     命运盒
        /// </summary>
        ChestOfChance
    }


    public AutoJobModel AutoJob { get; set; } = new();

    [Obsolete("Use config in PlayerOption")]
    public GachaConfigModel GachaConfig { get; set; } = new();

    public DungeonBattleRelicSortInfo[] DungeonBattleRelicSort { get; set; }
    public int AutoRequestDelay { get; set; }
    public bool RecordBattleLog { get; set; } = true;
    public string BattleLogDir { get; set; } = "BattleLogs/";
    public BountyQuestAutoModel BountyQuestAuto { get; set; } = new();
    public DungeonBattleConfig DungeonBattle { get; set; } = new();
    public ShopConfig Shop { get; set; } = new();

    [Obsolete("Use config in PlayerOption")]
    public LocalRaidConfig LocalRaid { get; set; } = new();

    public string AssetsUrl { get; set; } = "https://list.moonheart.dev/d/public/mmtm";
    public string BattleLogViewerUrl { get; set; } = "https://mentemori.icu/battle_log.html";
    public string Language { get; set; } = "";

    public LoginConfig Login { get; set; } = new();
    public ItemsConfig Items { get; set; } = new();

    public class ShopDiscountItem : IUserItem
    {
        public int MinDiscountPercent { get; set; }
        public long ItemCount { get; }
        public long ItemId { get; set; }
        public ItemType ItemType { get; set; }
    }

    public class ShopAutoBuyItem
    {
        public UserItem BuyItem { get; set; }
        public long ShopTabId { get; set; }
        public int MinDiscountPercent { get; set; }
        public UserItem ConsumeItem { get; set; }
    }

    public class DungeonBattleRelicSortInfo
    {
        public long Id { get; set; }
        public string Desc { get; set; }
    }

    public class GachaConfigModel
    {
        public List<UserItem> AutoGachaConsumeUserItems { get; set; } = new();

        public GachaRelicType TargetRelicType { get; set; } = GachaRelicType.None;

        public bool AutoGachaRelic { get; set; }
    }

    public class ShopConfig
    {
        public List<ShopAutoBuyItem> AutoBuyItems { get; set; } = new();
    }

    public class AutoJobModel
    {
        public bool DisableAll { get; set; }
        public bool AutoReinforcementEquipmentOneTime { get; set; }
        public bool AutoPvp { get; set; }
        public bool AutoLegendLeague { get; set; }
        public bool AutoDungeonBattle { get; set; }
        public bool AutoUseItems { get; set; }
        public bool AutoFreeGacha { get; set; }
        public bool AutoRankUpCharacter { get; set; }
        public bool AutoGuildRaid { get; set; } = true;
        public bool AutoOpenGuildRaid { get; set; }
        public bool AutoReadMemories { get; set; } = true;
        public bool AutoBookSort { get; set; }
        public bool AutoLocalRaid { get; set; }
        public bool AutoDeployGuildDefense { get; set; }
        public bool AutoChangeGachaRelic { get; set; }
        public bool AutoDrawGachaRelic { get; set; }

        public string DailyJobCron { get; set; } = "0 50 4 ? * *";
        public string HourlyJobCron { get; set; } = "0 30 0,4,8,12,16,20 ? * *";
        public string PvpJobCron { get; set; } = "0 0 20 ? * *";
        public string LegendLeagueJobCron { get; set; } = "0 10 20 ? * *";
        public string GuildRaidBossReleaseCron { get; set; } = "0 0 * ? * *";
        public string AutoBuyShopItemJobCron { get; set; } = "0 9 9,12,15,18 ? * *";
        public string AutoLocalRaidJobCron { get; set; } = "0 31 12,19 ? * *";
        public string AutoDeployGuildDefenseJobCron { get; set; } = "0 20 19 ? * *";
        public string AutoChangeGachaRelicJobCron { get; set; } = "0 40 4 ? * MON *";
        public string AutoDrawGachaRelicJobCron { get; set; } = "0 0 6 ? * SUN *";
    }

    public class BountyQuestAutoModel
    {
        public List<UserItem> TargetItems { get; set; } = new();
    }

    public class WeightedItem : IUserItem
    {
        public WeightedItem(ItemType itemType, long itemId, double weight)
        {
            ItemId = itemId;
            ItemType = itemType;
            Weight = weight;
        }

        public double Weight { get; set; }

        public long ItemCount { get; set; }
        public long ItemId { get; set; }
        public ItemType ItemType { get; set; }
    }

    public class LocalRaidConfig
    {
        public List<WeightedItem> RewardItems { get; set; } = new();
        // {
        //     new WeightedItem(ItemType.ExchangePlaceItem, 4, 4), // 符石兑换券
        //     new WeightedItem(ItemType.CharacterTrainingMaterial, 2, 3), // 潜能宝珠
        //     new WeightedItem(ItemType.EquipmentReinforcementItem, 2, 2.5), // 强化秘药
        //     new WeightedItem(ItemType.CharacterTrainingMaterial, 1, 2), // 经验珠
        //     new WeightedItem(ItemType.EquipmentReinforcementItem, 1, 1), // 强化水
        // };

        public bool SelfCreateRoom { get; set; }

        public int WaitSeconds { get; set; } = 3;

        /// <summary>加入模式等不到房间时，是否转为自己开房单刷（默认关闭，方便小号等待主号房间）。</summary>
        public bool HostWhenNoRoom { get; set; }

        /// <summary>加入模式找房最长等待分钟数。</summary>
        public int JoinWaitMinutes { get; set; } = 10;

        /// <summary>进房后房主不开战的最长等待分钟数，超时后退房重找。</summary>
        public int RoomWaitMinutes { get; set; } = 3;

        /// <summary>自己开房连续失败次数上限，0 表示不停止。</summary>
        public int MaxSoloLosses { get; set; } = 3;

        /// <summary>整个幻影神殿任务最长执行分钟数。</summary>
        public int MaxTaskMinutes { get; set; } = 20;

        /// <summary>等不到房间或单刷连败时，是否尝试降一级同类型关卡继续。</summary>
        public bool DropToLowerQuest { get; set; }

        /// <summary>加入模式同一关卡等待多少分钟后降一级。</summary>
        public int LowerQuestAfterMinutes { get; set; } = 3;

        /// <summary>手动指定关卡等级（0 = 按奖励权重自动选；&gt;0 = 选不超过该等级的最高级关卡）。</summary>
        public int QuestLevel { get; set; }
    }

    public class DungeonBattleConfig
    {
        public List<ShopDiscountItem> ShopTargetItems { get; set; } = new();

        [Obsolete("Use ShopDiscountItem.MinDiscountPercent")]
        public int ShopDiscountPercent { get; set; } = 0;

        public bool PreferTreasureChest { get; set; }
        public int MaxUseRecoveryItem { get; set; }

        public bool AutoRemoveEquipment { get; set; }
    }

    [Obsolete]
    public class LoginConfig
    {
        [Obsolete]
        public bool AutoLogin { get; set; }
    }

    public class ItemsConfig
    {
        public List<AutoUseItemType> AutoUseItemTypes { get; set; } = new();
    }
}