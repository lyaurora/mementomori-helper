using System.ComponentModel;
using MementoMori.Ortega.Share.Data.Battle;
using MementoMori.Ortega.Share.Data.Character;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
    [MessagePackObject(true)]
    [NotUseOnAuth]
    [NotUseOnBatch]
    [NotUseOnSerialCodeInput]
    [Description("放置バトル敵データ")]
	public class AutoBattleEnemyMB : MasterBookBase, IBattleEnemy
	{
		[PropertyOrder(12)]
		[Description("アクティブスキルIDのリスト")]
		public IReadOnlyList<long> ActiveSkillIds { get; }

		[Nest(true, 1)]
		[PropertyOrder(9)]
		[Description("ベースパラメータ")]
		public BaseParameter BaseParameter { get; }

		[PropertyOrder(16)]
		[Description("敵キャラクターID")]
		public long BattleEnemyCharacterId { get; }

		[PropertyOrder(10)]
		[Nest(true, 1)]
		[Description("バトルパラメータ")]
		public BattleParameter BattleParameter { get; }

		[PropertyOrder(5)]
		[Description("戦闘力")]
		public long BattlePower { get; }

		[PropertyOrder(8)]
		[Description("レアリティ")]
		public CharacterRarityFlags CharacterRarityFlags { get; }

		[PropertyOrder(7)]
		[Description("属性")]
		public ElementType ElementType { get; }

		[PropertyOrder(19)]
		[Description("敵調整ID")]
		public long EnemyAdjustId { get; }

		[PropertyOrder(17)]
		[Description("敵武具ID")]
		public long EnemyEquipmentId { get; }

        [PropertyOrder(18)]
        [Description("専用武器レアリティ")]
        public EquipmentRarityFlags ExclusiveEquipmentRarityFlags { get; }

		[PropertyOrder(4)]
		[Description("敵のランク")]
		public long EnemyRank { get; }

		[Description("職業")]
		[PropertyOrder(6)]
		public JobFlags JobFlags { get; }

		[Description("名称キー")]
		[PropertyOrder(3)]
		public string NameKey { get; }

		[Description("通常攻撃として使ってくるスキルID")]
		[PropertyOrder(11)]
		public long NormalSkillId { get; }

		[PropertyOrder(13)]
		[Description("パッシブスキルIDのリスト")]
		public IReadOnlyList<long> PassiveSkillIds { get; }

		[PropertyOrder(14)]
		[Description("ドロップするキャラクター経験値")]
		public int RewardCharacterExp { get; }

		[PropertyOrder(15)]
		[Description("ドロップするプレイヤー経験値")]
		public int RewardPlayerExp { get; }

		[Description("ユニットアイコンID")]
		[PropertyOrder(2)]
		public long UnitIconId { get; }

		[PropertyOrder(1)]
		[Description("ユニットアイコンタイプ")]
		public UnitIconType UnitIconType { get; }

		[SerializationConstructor]
		public AutoBattleEnemyMB(long id, bool? isIgnore, string memo, IReadOnlyList<long> activeSkillIds, BaseParameter baseParameter, UnitIconType unitIconType, long unitIconId, BattleParameter battleParameter, long enemyRank, long battlePower, JobFlags jobFlags, ElementType elementType, CharacterRarityFlags characterRarityFlags, string nameKey, long normalSkillId, IReadOnlyList<long> passiveSkillIds, int rewardCharacterExp, int rewardPlayerExp, long battleEnemyCharacterId, long enemyEquipmentId, EquipmentRarityFlags exclusiveEquipmentRarityFlags, long enemyAdjustId)
			: base(id, isIgnore, memo)
		{
			this.ActiveSkillIds = activeSkillIds;
			this.BaseParameter = baseParameter;
			this.UnitIconType = unitIconType;
			this.UnitIconId = unitIconId;
			this.BattleParameter = battleParameter;
			this.EnemyRank = enemyRank;
			this.BattlePower = battlePower;
			this.JobFlags = jobFlags;
			this.ElementType = elementType;
			this.CharacterRarityFlags = characterRarityFlags;
			this.NameKey = nameKey;
			this.NormalSkillId = normalSkillId;
			this.PassiveSkillIds = passiveSkillIds;
			this.RewardCharacterExp = rewardCharacterExp;
			this.RewardPlayerExp = rewardPlayerExp;
			this.BattleEnemyCharacterId = battleEnemyCharacterId;
			this.EnemyEquipmentId = enemyEquipmentId;
            this.ExclusiveEquipmentRarityFlags = exclusiveEquipmentRarityFlags;
			this.EnemyAdjustId = enemyAdjustId;

		}

		public AutoBattleEnemyMB() : base(0L, null, null)
		{
		}
	}
}