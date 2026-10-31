using System.ComponentModel;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[MessagePackObject(true)]
	[Description("バトルスキル演出設定")]
	public class BattleSkillCharacterSettingMB : MasterBookBase
	{
		[PropertyOrder(1)]
		[Description("キャラID (CharacterMB)")]
		public long CharacterId
		{
			get;
		}

		[PropertyOrder(2)]
		[Description("文字背景色タイプ")]
		public CharacterColorType CharacterColorType
		{
			get;
		}

		[SerializationConstructor]
		public BattleSkillCharacterSettingMB(long id, bool? isIgnore, string memo, long characterId, CharacterColorType characterColorType)
			:base(id, isIgnore, memo)
		{
			this.CharacterId = characterId;
			this.CharacterColorType = characterColorType;
		}

		public BattleSkillCharacterSettingMB():base(0L, null, null)
		{
		}
	}
}
