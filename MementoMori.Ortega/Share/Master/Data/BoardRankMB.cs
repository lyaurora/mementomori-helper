using System.ComponentModel;
using MementoMori.Ortega.Share.Data.BountyQuest;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[Description("祈りの泉\u3000ボードランク")]
	[MessagePackObject(true)]
	public class BoardRankMB : MasterBookBase
	{
		[Description("必要条件リスト")]
		[Nest(false, 0)]
		[PropertyOrder(2)]
		public IReadOnlyList<BoardRankConditionInfo> BoardRankConditionInfos
		{
			get;
		}

		[Description("開放クエスト最大レベル")]
		[PropertyOrder(3)]
		public BountyQuestRarityFlags OpenBountyQuestMaxRarity
		{
			get;
		}

		[PropertyOrder(4)]
		[Description("開放クエスト最低レベル")]
		public BountyQuestRarityFlags OpenBountyQuestMinRarity
		{
			get;
		}

		[PropertyOrder(5)]
		[Description("クエスト抽選リスト詳細ID")]
		public long QuestLotteryInfoListId
		{
			get;
		}

		[Description("ランク")]
		[PropertyOrder(1)]
		public long Rank
		{
			get;
		}

		[SerializationConstructor]
		public BoardRankMB(long id, bool? isIgnore, string memo, long rank, IReadOnlyList<BoardRankConditionInfo> boardRankConditionInfos, BountyQuestRarityFlags openBountyQuestMaxRarity, BountyQuestRarityFlags openBountyQuestMinRarity, long questLotteryInfoListId)
			:base(id, isIgnore, memo)
		{
			Rank = rank;
			BoardRankConditionInfos = boardRankConditionInfos;
			OpenBountyQuestMaxRarity = openBountyQuestMaxRarity;
			OpenBountyQuestMinRarity = openBountyQuestMinRarity;
			QuestLotteryInfoListId = questLotteryInfoListId;
		}

		public BoardRankMB():base(0L, null, null)
		{
		}
	}
}
