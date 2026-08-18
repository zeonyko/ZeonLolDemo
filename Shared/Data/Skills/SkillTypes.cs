namespace Shared
{
    /// <summary>技能怎么放：瞬发、引导、蓄力、被动。JSON 存 int，数值不要改。</summary>
    public enum SkillType
    {
        /// <summary>普通技能，默认可边走边放。</summary>
        Direct = 0,
        /// <summary>引导期间锁移动。</summary>
        Channeling = 1,
        /// <summary>蓄力期间锁移动，松开可取消。</summary>
        Charge = 2,
        /// <summary>不走放技能流程。</summary>
        Passive = 3
    }

    /// <summary>技能瞄什么：自己、单体、落点、方向。</summary>
    public enum TargetType
    {
        Self = 0,
        SingleTarget = 1,
        Point = 2,
        Direction = 3
    }

    /// <summary>圈外按技能时：立刻放、走进射程再放、还是限制到最远距离立刻放。</summary>
    public enum CastApproachPolicy
    {
        None = 0,
        WalkIntoRange = 1,
        ClampInstant = 2
    }

    /// <summary>时间轴推出来怎么出手（近战按形状找人 / 弹道 / 位移）。两端必须同一套。2 号空着别占用，旧数据会对不上。</summary>
    public enum ESkillResolveMode
    {
        None = 0,
        MeleeShape = 1,
        SkillshotLine = 3,
        LockedBolt = 4,
        Blink = 5,
        Dash = 6,
        Jump = 7,
    }

    /// <summary>判定形状。</summary>
    public enum HitShapeType
    {
        Box = 0,
        Sphere = 1,
        Sector = 2
    }

    /// <summary>判定/生成从哪取锚点。</summary>
    public enum TargetSelectorType
    {
        CasterSelf = 0,
        /// <summary>半径内按阵营动态搜人，每人一个锚点。</summary>
        DynamicTargets = 1,
        /// <summary>施法落点等固定世界坐标。</summary>
        FixedWorldPos = 2,
        /// <summary>友方按剩余血量从低到高取前 N。</summary>
        LowestHpAllies = 3
    }

    /// <summary>相对施法者：自己 / 友方 / 敌方。</summary>
    public enum TargetRelation
    {
        Self = 0,
        Ally = 1,
        Enemy = 2
    }

    /// <summary>位移类型。</summary>
    public enum ESkillMotionClipType
    {
        None = 0,
        Blink = 1,
        LinearDash = 2,
        Jump = 3,
    }

    /// <summary>位移朝哪：输入方向、锁定目标、还是固定偏移。</summary>
    public enum ESkillMotionTargetType
    {
        InputDirection = 0,
        LockTarget = 1,
        FixedOffset = 2,
    }

    /// <summary>位移撞人时：穿过还是停。</summary>
    public enum ESkillMotionCollisionPolicy
    {
        PassThrough = 0,
        StopOnHitEnemy = 1,
    }
}
