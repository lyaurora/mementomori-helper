using System.ComponentModel;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[MessagePackObject(true)]
	[Description("アイテム変換情報")]
	public class ChangeItemMB : MasterBookBase
	{
		[PropertyOrder(5)]
		[Nest(false, 0)]
		[Description("変換されるアイテムリスト")]
		public IReadOnlyList<UserItem> ChangeItems
		{
			get;
		}

		[Description("アイテム変換タイプ")]
		[PropertyOrder(1)]
		public ChangeItemType ChangeItemType
		{
			get;
		}

		[Description("アイテムID")]
		[PropertyOrder(3)]
		public long ItemId
		{
			get;
		}

		[Description("アイテム種別")]
		[PropertyOrder(2)]
		public ItemType ItemType
		{
			get;
		}

		[PropertyOrder(4)]
		[Description("変換に必要な数")]
		public int NeedCount
		{
			get;
		}

		[SerializationConstructor]
		public ChangeItemMB(long id, bool? isIgnore, string memo, IReadOnlyList<UserItem> changeItems, ChangeItemType changeItemType, long itemId, ItemType itemType, int needCount)
			:base(id, isIgnore, memo)
		{
			ChangeItems = changeItems;
			ChangeItemType = changeItemType;
			ItemId = itemId;
			ItemType = itemType;
			NeedCount = needCount;
		}

		public ChangeItemMB():base(0L, null, null)
		{
		}
	}
}
