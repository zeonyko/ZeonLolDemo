using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>时间轴上的一段时间（接招窗/取消窗），相对技能开始。</summary>
    public struct CastTimeWindow
    {
        public float Start;
        public float End;
        public bool Contains(float t) => t >= Start && t <= End;
    }

    /// <summary>施法表现目标（落点圈/施法特效）。跟着施法一路传下去，不暂存在组件里。</summary>
    public readonly struct CastPresentationTarget
    {
        public readonly long TargetId;
        public readonly Vector3 TargetPos;
        public readonly bool HasTargetPoint;

        public static CastPresentationTarget None => default;

        public CastPresentationTarget(long targetId, Vector3 targetPos, bool hasTargetPoint)
        {
            TargetId = targetId;
            TargetPos = targetPos;
            HasTargetPoint = hasTargetPoint;
        }

        public static CastPresentationTarget Point(Vector3 targetPos, long targetId = 0)
            => new CastPresentationTarget(targetId, targetPos, hasTargetPoint: true);
    }

    /// <summary>施法播放表：表现事件 + 逻辑窗口。</summary>
    public class SkillPlaybackConfig
    {
        public int SkillId;
        public string Name;
        public float CastRange;
        public float Cooldown;
        public int CostMana;
        public SkillPresentationConfig Presentation;
        public PlaybackEvent[] Events;
        public CastTimeWindow[] AcceptWindows;
        public CastTimeWindow[] CancelWindows;
    }

    /// <summary>受击/反馈播放表，从 Reaction JSON 预先整理好。</summary>
    public sealed class SkillReactionPlayConfig
    {
        public string ReactionId;
        public float Duration;
        public PlaybackEvent[] Events;
    }

    /// <summary>一次命中要播的数据（网络/剧情交给命中表现）。</summary>
    public struct SkillHitContext
    {
        public long CasterId;
        public long TargetId;
        public int SkillId;
        public uint HitIndex;
        public bool Accepted;
        public float Damage;
        public UnityEngine.Vector3 CasterPos;
        public uint HitFlags;
        public UnityEngine.Vector3 KnockbackDir;
        public string AttackType;
        public string TargetReactionId;
        public string CasterReactionId;
        /// <summary>表现用伤害类型；没填当物理。</summary>
        public DamageType DamageType;
    }
}
