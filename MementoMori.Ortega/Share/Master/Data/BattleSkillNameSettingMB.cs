using System.ComponentModel;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
    [Description("バトルスキル演出設定")]
    [MessagePackObject(true)]
    public class BattleSkillNameSettingMB : MasterBookBase
    {
        [PropertyOrder(1)]
        [Description("ルートスキルID")]
        public long RootActiveSkillId { get; }

        [PropertyOrder(2)]
        [Description("改行位置設定 JP")]
        public int NewLineIndexJP { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 US")]
        public int NewLineIndexUS { get; }

        [PropertyOrder(2)]
        [Description("改行位置設定 KR")]
        public int NewLineIndexKR { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 TW")]
        public int NewLineIndexTW { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 FR")]
        public int NewLineIndexFR { get; }

        [Description("改行位置設定 CN")]
        [PropertyOrder(3)]
        public int NewLineIndexCN { get; }

        [Description("改行位置設定 MX")]
        [PropertyOrder(3)]
        public int NewLineIndexMX { get; }

        [Description("改行位置設定 BR")]
        [PropertyOrder(3)]
        public int NewLineIndexBR { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 TH")]
        public int NewLineIndexTH { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 ID")]
        public int NewLineIndexID { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 VN")]
        public int NewLineIndexVN { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 RU")]
        public int NewLineIndexRU { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 DE")]
        public int NewLineIndexDE { get; }

        [PropertyOrder(3)]
        [Description("改行位置設定 EG")]
        public int NewLineIndexEG { get; }

        [SerializationConstructor]
        public BattleSkillNameSettingMB(long id, bool? isIgnore, string memo, long rootActiveSkillId, int newLineIndexJP, int newLineIndexUS, int newLineIndexKR, int newLineIndexTW, int newLineIndexFR, int newLineIndexCN, int newLineIndexMX, int newLineIndexBR, int newLineIndexTH, int newLineIndexID, int newLineIndexVN, int newLineIndexRU, int newLineIndexDE, int newLineIndexEG)
            : base(id, isIgnore, memo)
        {
            RootActiveSkillId = rootActiveSkillId;
            NewLineIndexJP = newLineIndexJP;
            NewLineIndexUS = newLineIndexUS;
            NewLineIndexKR = newLineIndexKR;
            NewLineIndexTW = newLineIndexTW;
            NewLineIndexFR = newLineIndexFR;
            NewLineIndexCN = newLineIndexCN;
            NewLineIndexMX = newLineIndexMX;
            NewLineIndexBR = newLineIndexBR;
            NewLineIndexTH = newLineIndexTH;
            NewLineIndexID = newLineIndexID;
            NewLineIndexVN = newLineIndexVN;
            NewLineIndexRU = newLineIndexRU;
            NewLineIndexDE = newLineIndexDE;
            NewLineIndexEG = newLineIndexEG;
        }

        public BattleSkillNameSettingMB() : base(0L, null, null)
        {
        }

        public int GetNewLineIndex(LanguageType languageType)
        {
            if (languageType == LanguageType.jaJP)
            {
                return this.NewLineIndexTW;
            }

            return this.NewLineIndexUS;
        }
    }
}