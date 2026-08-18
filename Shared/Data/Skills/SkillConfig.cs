using System;

namespace Shared
{
    /// <summary>
    /// 技能逻辑表。JSON 的 Logic 段；出手时刻、近战/弹道等由时间轴推出来，不用在表里再写一遍。
    /// </summary>
    [Serializable]
    public class SkillConfig
    {
        public int SkillId;
        public string Name = "";
        /// <summary>SkillType，JSON 存 int。</summary>
        public int Type;
        /// <summary>TargetType，JSON 存 int。</summary>
        public int AllowedTargets;
        public float CastRange;
        public float Cooldown;
        public int CostMana;
        public bool CanCastInStun;
        public bool OrientToTargetOnCast = true;
        /// <summary>基础伤害；算伤害时再乘各段倍率。</summary>
        public int Damage;
        /// <summary>是否普攻（建筑「只吃普攻」靠这个）。</summary>
        public bool IsAutoAttack;
        public SkillClip[] Clips = Array.Empty<SkillClip>();

        public SkillType SkillType
        {
            get => (SkillType)Type;
            set => Type = (int)value;
        }

        public TargetType TargetMode
        {
            get => (TargetType)AllowedTargets;
            set => AllowedTargets = (int)value;
        }

        public float HitDelay => SkillClipUtil.ResolveHitTime(Clips);
        public float MotionDistance => SkillClipUtil.ResolveMotionDistance(Clips);
        public int ProjectileId => SkillClipUtil.ResolvePrimaryProjectileId(Clips);
        public ESkillResolveMode ResolveMode => SkillClipUtil.ResolveMode(Clips, TargetMode);
        public bool HasCasterMotion => SkillClipUtil.HasMotion(Clips);
        /// <summary>普攻和单体技都必须锁敌人；没目标不能空挥。</summary>
        public bool NeedsLockTarget => IsAutoAttack || TargetMode == TargetType.SingleTarget;
        public bool AllowsCastWhileRooted => ResolveMode == ESkillResolveMode.Blink;
        /// <summary>移动是否取消本技能（普攻前摇、引导）。和「施法期能不能走动」不是一回事。</summary>
        public bool CanMoveInterrupt =>
            IsAutoAttack || SkillType == SkillType.Channeling;
        /// <summary>
        /// 施法期锁步：引导/蓄力全程锁；带位移的技能也锁（冲刺/闪现期间 WASD 不能改坐标）。
        /// </summary>
        public bool LocksMovementWhileCasting =>
            SkillType == SkillType.Channeling
            || SkillType == SkillType.Charge
            || HasCasterMotion;
        public bool IsMelee => ResolveMode == ESkillResolveMode.MeleeShape;
        public bool IsSkillshot => ResolveMode == ESkillResolveMode.SkillshotLine;
        public bool IsLockedBolt => ResolveMode == ESkillResolveMode.LockedBolt;
        public bool IsProjectile => IsSkillshot || IsLockedBolt;
        public bool HasDirectShapeHit => SkillClipUtil.HasDirectShape(Clips);
        /// <summary>近战按形状找人、或冲过去顺带打人，要过攻击检查。</summary>
        public bool RequiresAttackGate =>
            IsMelee || (ResolveMode == ESkillResolveMode.Dash && HasDirectShapeHit);

        public CastApproachPolicy CastApproach => SkillRules.GetCastApproach(this);

        public float ProjectileWidth =>
            ProjectileCatalog.TryGet(ProjectileId, out var p) ? p.Width : 0f;

        public int ProjectileMaxTargets =>
            ProjectileCatalog.TryGet(ProjectileId, out var p) ? p.MaxTargets : 0;

        /// <summary>时间轴事件（不含命中/位移）。</summary>
        public SkillTimelineEventNode[] TimelineEvents => SkillClipUtil.ToTimelineEvents(Clips);

        public SkillHitPayload PrimaryHit => SkillClipUtil.PrimaryHit(Clips);
    }

    /// <summary>运行时技能表。开战加载，战斗中只读。</summary>
    public static class SkillCatalog
    {
        static readonly IdCatalog<SkillConfig> Items = new IdCatalog<SkillConfig>(
            c => c.SkillId,
            SkillClipUtil.Normalize);

        public static void RegisterOrReplace(SkillConfig data) => Items.RegisterOrReplace(data);
        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<SkillConfig> list) =>
            Items.ClearAndLoad(list);
        public static SkillConfig Get(int skillId) => Items.Get(skillId);
        public static bool TryGet(int skillId, out SkillConfig data) => Items.TryGet(skillId, out data);
        public static bool IsKnown(int skillId) => Items.Contains(skillId);
        public static System.Collections.Generic.IReadOnlyDictionary<int, SkillConfig> All => Items.All;
    }
}
