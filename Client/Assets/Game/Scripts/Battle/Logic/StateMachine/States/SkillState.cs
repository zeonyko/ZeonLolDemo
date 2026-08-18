using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>技能行为态：进入时开时间轴；后摇有走位则切回移动，否则播完再回落 Idle/Walk。</summary>
    public sealed class SkillState : EntityFsmStateBase
    {
        #region 施法参数（Configure 写入，生命周期回调读取）

        /// <summary>进入施法态时带上的参数：技能、目标、方向。</summary>
        private sealed class CastParams
        {
            public int SkillId;
            public Vector3 Dir;
            public bool AllowLogical;
            public CastPresentationTarget PresentationTarget;
        }

        private readonly CastParams _cast = new CastParams();

        #endregion

        public override EEntityFsmState Type => EEntityFsmState.Skill;

        const float FallbackLockDuration = 0.35f;
        const float HitLockPad = 0.15f;
        const float MinLockDuration = 0.05f;

        /// <summary>切入前由外部写入本次施法参数。</summary>
        public void Configure(
            int skillId,
            Vector3 dir,
            bool allowLogical,
            CastPresentationTarget presentationTarget = default)
        {
            _cast.SkillId = skillId;
            _cast.Dir = dir;
            _cast.AllowLogical = allowLogical;
            _cast.PresentationTarget = presentationTarget;
        }

        #region FSM 生命周期

        public override void OnEnter()
        {
            var skillComp = SkillComp;
            if (skillComp == null)
            {
                Machine.ChangeState(EEntityFsmState.Idle);
                return;
            }

            AbortResidualCast(skillComp);
            ApplyCastingTag();
            StartTimeline(skillComp);
        }

        // 推进 Timeline；施法结束则清 Tag 并回落到移动/待机
        public override void OnUpdate(float dt)
        {
            var skillComp = SkillComp;
            if (skillComp == null)
            {
                ClearCastingTag();
                Machine.ResolveLocomotion();
                return;
            }

            skillComp.TickCast(dt);

            if (!skillComp.IsCasting)
            {
                ClearCastingTag();
                Machine.ResolveLocomotion();
                return;
            }

            // 后摇且锁步已结束：有走位则切出技能态。Move.OnEnter 播 Walk。
            // 不要只用 !LocksMovementNow：没位移的 Direct 技边走边放时锁步一直是 false。
            if (skillComp.IsInRecovery
                && !skillComp.LocksMovementNow
                && Machine.HasMoveIntent)
            {
                ClearCastingTag();
                Machine.ResolveLocomotion();
            }
        }

        // 被强制切走时中断残留施法并清 Tag
        public override void OnExit()
        {
            var skillComp = SkillComp;
            if (skillComp != null && skillComp.IsCasting)
                skillComp.AbortCast();

            ClearCastingTag();
        }

        #endregion

        #region OnEnter 步骤

        private static void AbortResidualCast(SkillCastComponent skillComp)
        {
            if (skillComp.IsCasting)
                skillComp.AbortCast();
        }

        // 有明确锁定时长用计时 Tag，否则常驻到施法结束再清
        private void ApplyCastingTag()
        {
            float lockDuration = ResolveCastLockDuration(_cast.SkillId);
            if (lockDuration > 0.001f)
                Machine.AddTimedState(EntityStateTag.Casting, lockDuration);
            else
                Machine.AddState(EntityStateTag.Casting);
        }

        private void StartTimeline(SkillCastComponent skillComp)
        {
            skillComp.BeginTimeline(_cast.SkillId, _cast.Dir, _cast.AllowLogical, _cast.PresentationTarget);
        }

        #endregion

        #region 内部工具

        private void ClearCastingTag()
        {
            Machine.ForceClearState(EntityStateTag.Casting);
        }

        private static float ResolveCastLockDuration(int skillId)
        {
            var def = SkillCatalog.Get(skillId);
            if (def == null) return FallbackLockDuration;

            // 多段技能锁到最后一段 Hit，避免第一刀后就卸 Casting
            float lastHit = SkillClipUtil.TryGetLastHitPayloadTime(def.Clips, out float t)
                ? t
                : SkillRules.GetEffectiveHitDelay(def);

            return Mathf.Max(MinLockDuration, lastHit + HitLockPad);
        }

        #endregion
    }
}
