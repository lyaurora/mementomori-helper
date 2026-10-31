using System.ComponentModel;
using MementoMori.Ortega.Share.Data.Character;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[MessagePackObject(true)]
    [NotUseOnBatch]
	[Description("キャラクター視聴可能ボイス")]
	public class CharacterDetailVoiceMB : MasterBookBase
	{
		[PropertyOrder(1)]
		[Description("キャラクターId")]
		public long CharacterId
		{
			get;
		}

		[PropertyOrder(4)]
		[Description("キャラクターボイス分類")]
		public CharacterVoiceCategory CharacterVoiceCategory
		{
			get;
		}

		[Description("Path")]
		[PropertyOrder(7)]
		[Nest(false, 0)]
		public CharacterVoicePath Path
		{
			get;
		}

		[Description("スクロールビューでの表示順（昇順）")]
		[PropertyOrder(8)]
		public int SortOrder
		{
			get;
		}

		[Description("ボイス再生時の字幕キー")]
		[PropertyOrder(3)]
		public string SubtitleKey
		{
			get;
		}

		[PropertyOrder(5)]
		[Description("解放条件")]
		public UnlockCharacterDetailVoiceType UnlockCondition
		{
			get;
		}

		[PropertyOrder(2)]
		[Description("ボタン表示用テキスト（解放済）キー")]
		public string UnlockedVoiceButtonTextKey
		{
			get;
		}

		[PropertyOrder(6)]
		[Description("クエストID")]
		public long UnlockQuestId
		{
			get;
		}

		[SerializationConstructor]
		public CharacterDetailVoiceMB(long id, bool? isIgnore, string memo, long characterId, string unlockedVoiceButtonTextKey, string subtitleKey, CharacterVoiceCategory characterVoiceCategory, UnlockCharacterDetailVoiceType unlockCondition, long unlockQuestId, CharacterVoicePath path, int sortOrder)
			:base(id, isIgnore, memo)
		{
			CharacterId = characterId;
			UnlockedVoiceButtonTextKey = unlockedVoiceButtonTextKey;
			SubtitleKey = subtitleKey;
			CharacterVoiceCategory = characterVoiceCategory;
			UnlockCondition = unlockCondition;
			UnlockQuestId = unlockQuestId;
			Path = path;
			SortOrder = sortOrder;
		}

		public CharacterDetailVoiceMB():base(0L, null, null)
		{
		}
	}
}
