namespace Shared
{
    /// <summary>Buff 特效挂在身上哪。</summary>
    public enum BuffAttachPoint
    {
        Root = 0,
        Foot = 1,
        Head = 2,
        Chest = 3
    }

    /// <summary>Buff 干什么。瞬时伤害不走 Buff，走伤害数据。</summary>
    public enum BuffEffectKind
    {
        None = 0,
        PeriodDamage = 2,
        Status = 3
    }

    /// <summary>增益 / 减益 / 中性。</summary>
    public enum BuffType
    {
        Debuff = 0,
        Buff = 1,
        Neutral = 2
    }

    /// <summary>同编号再挂一次时怎么叠。</summary>
    public enum BuffStackType
    {
        /// <summary>刷新持续时间，不叠层。</summary>
        OverrideDuration = 0,
        /// <summary>叠层并刷新。</summary>
        AddStack = 1,
        /// <summary>未实现，不要配。同编号目前永远走刷新。</summary>
        Independent = 2
    }

    /// <summary>可被 Buff 改的属性。</summary>
    public enum AttributeType
    {
        MoveSpeed = 0,
        Attack = 1,
        Defense = 2
    }

    /// <summary>属性怎么改：加算或百分比加算。</summary>
    public enum ModifyType
    {
        Add = 0,
        PercentAdd = 1
    }

    /// <summary>配表里的状态编号。和运行时不是同一套，要用转换函数。</summary>
    public static class SkillEffectFlags
    {
        public const int None = 0;
        public const int Root = 1 << 0;
        public const int Stun = 1 << 1;
        public const int Silence = 1 << 2;
        public const int Slow = 1 << 3;
        public const int Disarm = 1 << 4;
        public const int Airborne = 1 << 5;
        public const int Unstoppable = 1 << 6;
    }
}
