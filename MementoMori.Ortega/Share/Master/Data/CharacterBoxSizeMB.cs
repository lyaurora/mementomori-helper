using System.ComponentModel;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[Description("キャラクターボックスサイズ")]
	[MessagePackObject(true)]
	public class CharacterBoxSizeMB : MasterBookBase
	{
		[Description("倉庫所持上限")]
		[PropertyOrder(2)]
		public long BoxSize
		{
			get;
		}

		[PropertyOrder(1)]
		[Description("レベル")]
		public long Lv
		{
			get;
		}

		[Description("必要仮想通貨数")]
		[PropertyOrder(3)]
		public long RequiredCurrency
		{
			get;
		}

		public CharacterBoxSizeMB(long id, bool? isIgnore, string memo, long boxSize, long lv, long requiredCurrency)
			:base(id, isIgnore, memo)
		{
			BoxSize = boxSize;
			Lv = lv;
			RequiredCurrency = requiredCurrency;
		}

		public CharacterBoxSizeMB():base(0L, null, null)
		{
		}
	}
}
