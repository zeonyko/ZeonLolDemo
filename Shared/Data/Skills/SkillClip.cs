using System;

namespace Shared
{
    /// <summary>技能时间轴上的一条：什么时候触发、走哪条轨、可选带命中或位移。</summary>
    [Serializable]
    public class SkillClip
    {
        /// <summary>触发时刻（秒）。</summary>
        public float Time;
        /// <summary>持续时长（秒）；没填就用默认。</summary>
        public float Duration = SkillTrackId.DefaultClipDuration;
        /// <summary>轨道编号，见 SkillTrackId。</summary>
        public int Track;
        /// <summary>事件名，如 Windup / Hit / Recovery。</summary>
        public string Key = "";
        /// <summary>附加参数，客户端解释。</summary>
        public string Param = "";
        /// <summary>命中判定；没有内容时为 null。</summary>
        public SkillHitPayload Hit;
        /// <summary>施法者位移；没有内容时为 null。</summary>
        public SkillMotionPayload Motion;
    }

    /// <summary>这一下：按形状找人、出弹、铺区域、给自己套 Buff，可以同时有。</summary>
    [Serializable]
    public class SkillHitPayload
    {
        public string AttackType = "";
        public string CasterFeedbackType = "";
        public SkillShapeHitPayload[] ShapeHits = Array.Empty<SkillShapeHitPayload>();
        public SkillProjectilePayload[] ProjectileSpawns = Array.Empty<SkillProjectilePayload>();
        public SkillAreaPayload[] AreaSpawns = Array.Empty<SkillAreaPayload>();
        public BuffApplySpec[] CasterBuffs = Array.Empty<BuffApplySpec>();
        /// <summary>给施法者的瞬时治疗；0 = 不治疗。</summary>
        public float Heal;

        public bool HasContent
        {
            get
            {
                if (ShapeHits != null && ShapeHits.Length > 0) return true;
                if (ProjectileSpawns != null && ProjectileSpawns.Length > 0) return true;
                if (CasterBuffs != null && CasterBuffs.Length > 0) return true;
                if (HasAreaSpawns) return true;
                if (Heal > 0.05f) return true;
                return false;
            }
        }

        public bool HasShapeHits => ShapeHits != null && ShapeHits.Length > 0;
        public bool HasProjectileSpawns => ProjectileSpawns != null && ProjectileSpawns.Length > 0;

        public bool HasAreaSpawns
        {
            get
            {
                if (AreaSpawns == null) return false;
                for (int i = 0; i < AreaSpawns.Length; i++)
                {
                    if (AreaSpawns[i] != null && AreaSpawns[i].HasContent)
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>地上持续区域：每隔一段时间按形状找人并算效果。</summary>
    [Serializable]
    public class SkillAreaPayload
    {
        public string Name = "";
        public TargetSelectorData Anchor;
        public float Duration = 6f;
        public float TickInterval = 0.5f;
        /// <summary>同施法者同技能已有区域时替换。</summary>
        public bool ReplaceSame = true;
        public SkillShapePayload Shape;
        public HitEffectPayload TickEffect;
        public HitFeedbackSignal TickFeedback;

        public bool HasContent =>
            Duration > 0.05f
            && TickInterval > 0.05f
            && Shape != null
            && Shape.SizeX > 0.05f;
    }

    /// <summary>按形状找人：从哪扫、扫多大、打中做什么。</summary>
    [Serializable]
    public class SkillShapeHitPayload
    {
        public string Name = "";
        public TargetSelectorData Anchor;
        public SkillShapePayload Shape;
        public HitEffectPayload Effect;
        public HitFeedbackSignal Feedback;

        public bool HasShape => Shape != null;

        public DamagePayload ResolveDamage() =>
            Effect != null ? Effect.ResolveDamage() : DamagePayload.DefaultInstant();

        public BuffApplySpec[] ResolveTargetBuffs() =>
            Effect?.TargetBuffs ?? Array.Empty<BuffApplySpec>();

        public float ResolveKnockbackDistance() =>
            Effect != null && Effect.KnockbackDistance > 0f ? Effect.KnockbackDistance : 0f;
    }

    /// <summary>判定形状：盒子 / 球 / 扇形。</summary>
    [Serializable]
    public class SkillShapePayload
    {
        public int ShapeType;
        /// <summary>球半径 / 盒半宽等。</summary>
        public float SizeX = 1f;
        public float SizeY = 1f;
        public float SizeZ;
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;

        public HitShapeType Type
        {
            get => (HitShapeType)ShapeType;
            set => ShapeType = (int)value;
        }
    }

    /// <summary>生成一条弹道。</summary>
    [Serializable]
    public class SkillProjectilePayload
    {
        public int ProjectileId;
        public TargetSelectorData Anchor;
        public float AngleOffsetX;
        public float AngleOffsetY;
        public float AngleOffsetZ;
    }

    /// <summary>施法者位移：闪现 / 冲刺 / 跳跃。</summary>
    [Serializable]
    public class SkillMotionPayload
    {
        public int MotionType;
        public float Distance;
        public int TargetType;
        public int CollisionPolicy;

        public ESkillMotionClipType Type
        {
            get => (ESkillMotionClipType)MotionType;
            set => MotionType = (int)value;
        }

        public bool HasContent => Type != ESkillMotionClipType.None && Distance > 0f;
    }
}
