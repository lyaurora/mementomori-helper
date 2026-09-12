using MementoMori.Ortega.Share.Data.Interface;
using MementoMori.Ortega.Share.Enums.Battle.Skill;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Battle
{
    [MessagePackObject(true)]
    public class Effect : IDeepCopy<Effect>
    {
        public long EffectSubValue { get; set; }


        public EffectType EffectType { get; set; }

        public long EffectValue { get; set; }

        public int EffectMaxCount { get; set; }

        public int EffectCount { get; set; }

        public Effect DeepCopy() => (Effect)MemberwiseClone();

        public Effect()
        {
        }
    }
}