using System;

namespace Shared
{
    /// <summary>一次命中怎么算伤害：基础值 × 系数。嵌在技能/弹道/区域里，不是独立配表。</summary>
    [Serializable]
    public class DamagePayload
    {
        /// <summary>基础伤害；0 则用所属技能的伤害。</summary>
        public float BaseDamage;
        /// <summary>倍率，多段/碰到人/形状可以各自不同。</summary>
        public float Coefficient = 1f;
        /// <summary>物理 / 魔法 / 真实。JSON 字段名 DamageType。</summary>
        public int DamageType;

        public DamageType Type
        {
            get => (DamageType)DamageType;
            set => DamageType = (int)value;
        }

        /// <summary>最终伤害 = (基础伤害或技能伤害) × 倍率。</summary>
        public float ResolveAmount(float skillDamage)
        {
            float baseDamage = BaseDamage > 0f ? BaseDamage : skillDamage;
            float coefficient = Coefficient > 0f ? Coefficient : 1f;
            return Math.Max(0f, baseDamage * coefficient);
        }

        /// <summary>默认瞬时物理伤害。</summary>
        public static DamagePayload DefaultInstant(float coefficient = 1f)
        {
            return new DamagePayload
            {
                Coefficient = coefficient > 0f ? coefficient : 1f,
                DamageType = (int)Shared.DamageType.Physical
            };
        }
    }
}
