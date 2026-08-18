using System;

namespace Shared
{
    /// <summary>命中时播哪个受击/施法反馈（字符串键，客户端去查表）。</summary>
    [Serializable]
    public class HitFeedbackSignal
    {
        public string AttackType = "";
        public string CasterFeedbackType = "";
    }

    /// <summary>命中后对目标做什么：伤害、套 Buff、击退。</summary>
    [Serializable]
    public class HitEffectPayload
    {
        public DamagePayload Damage;
        public BuffApplySpec[] TargetBuffs = Array.Empty<BuffApplySpec>();
        /// <summary>水平击退距离（米）；0 = 不击退。</summary>
        public float KnockbackDistance;

        public DamagePayload ResolveDamage() =>
            Damage ?? DamagePayload.DefaultInstant();
    }

    /// <summary>一条「尝试套 Buff」规则。能不能套上由过滤器判断，概率由外面掷。</summary>
    [Serializable]
    public class BuffApplySpec
    {
        public int BuffId;
        /// <summary>0~1，调用方自己掷骰。</summary>
        public float Chance = 1f;
        /// <summary>相对施法者：自己 / 友方 / 敌方。</summary>
        public int Relation = (int)TargetRelation.Enemy;
        /// <summary>目标必须已有这些状态；0 = 不额外要求。配表编号，不是运行时状态。</summary>
        public int RequireStatusFlags;
        /// <summary>覆盖持续时间（秒）；0 = 用 Buff 模板默认。</summary>
        public float OverrideDuration;

        public TargetRelation TargetRelation
        {
            get => (TargetRelation)Relation;
            set => Relation = (int)value;
        }
    }
}
