using System.ComponentModel;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[MessagePackObject(true)]
	[Description("章")]
	public class ChapterMB : MasterBookBase
	{
		[Description("名称キー")]
		[PropertyOrder(2)]
		public string NameKey
		{
			get;
		}

		[MasterBookId(typeof(StateMB))]
		[PropertyOrder(1)]
		[Description("国ID")]
		public long StateId
		{
			get;
		}

		[PropertyOrder(3)]
		[Description("テキストキー")]
		public string TextKey
		{
			get;
		}

		[PropertyOrder(4)]
		[Description("領地名")]
		public string TerritoryNameKey
		{
			get;
		}

		[SerializationConstructor]
		public ChapterMB(long id, bool? isIgnore, string memo, string nameKey, long stateId, string textKey, string territoryNameKey)
			:base(id, isIgnore, memo)
		{ 
			NameKey = nameKey;
			StateId = stateId;
			TextKey = textKey;
			TerritoryNameKey = territoryNameKey;
		}

		public ChapterMB():base(0L, null, null)
		{
		}
	}
}
