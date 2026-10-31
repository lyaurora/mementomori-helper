using System.Collections;
using System.ComponentModel;
using MementoMori.Ortega.Share.Data.BountyQuest;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MementoMori.Ortega.Share.Utils;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
    [Description("祈りの泉\u3000イベント")]
    [MessagePackObject(true)]
    public class BountyQuestEventMB : MasterBookBase, IHasStartEndTime
    {
        [PropertyOrder(2)]
        [Description("イベント説明")]
        public string EventDescriptionKey { get; }

        [Description("イベント名")]
        [PropertyOrder(1)]
        public string EventNameKey { get; }

        [Description("報酬量の増加率(%)")]
        [PropertyOrder(5)]
        public int MultipleNumber { get; }

        [PropertyOrder(7)]
        [Nest(false, 0)]
        [Description("対象となるアイテムリスト")]
        public IReadOnlyList<BountyQuestEventTargetItemInfo> TargetItemList { get; }

        [Description("対象となる祈りの泉クエストタイプ")]
        [PropertyOrder(6)]
        [Nest(false, 0)]
        public IReadOnlyList<BountyQuestEventTargetQuestTypeInfo> TargetQuestTypeList { get; }

        [SerializationConstructor]
        public BountyQuestEventMB(long id, bool? isIgnore, string memo, string eventNameKey, string eventDescriptionKey, string startTime, string endTime, int multipleNumber,
            IReadOnlyList<BountyQuestEventTargetQuestTypeInfo> targetQuestTypeList, IReadOnlyList<BountyQuestEventTargetItemInfo> targetItemList)
            : base(id, isIgnore, memo)
        {
            EventNameKey = eventNameKey;
            EventDescriptionKey = eventDescriptionKey;
            StartTime = startTime;
            EndTime = endTime;
            MultipleNumber = multipleNumber;
            TargetQuestTypeList = targetQuestTypeList;
            TargetItemList = targetItemList;
        }

        public BountyQuestEventMB() : base(0L, null, null)
        {
        }

        [Description("終了時刻")]
        [PropertyOrder(4)]
        public string EndTime { get; }

        [PropertyOrder(3)]
        [Description("開始時刻")]
        public string StartTime { get; }

        public bool IsTargetQuestType(BountyQuestType bountyQuestType)
        {
            // int num;
            // do
            // {
            // 	IReadOnlyList<BountyQuestEventTargetQuestTypeInfo> readOnlyList = this.TargetQuestTypeList;
            // 	num = 0;
            // 	if (typeof(IEnumerator).TypeHandle != 0)
            // 	{
            // 		uint num2;
            // 		if (num >= (int)num2)
            // 		{
            // 			goto IL_23;
            // 		}
            // 		num += num;
            // 		if (num != (int)num2)
            // 		{
            // 			num++;
            // 			goto IL_23;
            // 		}
            // 		IL_26:
            // 		int num3;
            // 		num3 += num3;
            // 		goto IL_2D;
            // 		IL_23:
            // 		num3 = 0;
            // 		goto IL_26;
            // 	}
            // 	IL_2D:
            // 	if ("{il2cpp array field local7->}" != (ulong)0L)
            // 	{
            // 	}
            // }
            // while (num != 0);
            // throw new NullReferenceException();
            throw new NotImplementedException();
        }

        public bool IsTargetItem(IUserItem userItem)
        {
            // int num;
            // do
            // {
            // 	IReadOnlyList<BountyQuestEventTargetItemInfo> readOnlyList = this.TargetItemList;
            // 	num = 0;
            // 	if (typeof(IEnumerator).TypeHandle != 0)
            // 	{
            // 		uint num2;
            // 		if (num < (int)num2)
            // 		{
            // 			num += num;
            // 			if (num == (int)num2)
            // 			{
            // 				goto IL_28;
            // 			}
            // 			num++;
            // 		}
            // 		bool flag;
            // 		while (!flag)
            // 		{
            // 		}
            // 		int num3 = 0;
            // 		IL_28:
            // 		num3 += num3;
            // 	}
            // 	if ("{il2cpp array field local6->}" != (ulong)0L)
            // 	{
            // 	}
            // }
            // while (num != 0);
            // throw new NullReferenceException();
            throw new NotImplementedException();
        }
    }
}