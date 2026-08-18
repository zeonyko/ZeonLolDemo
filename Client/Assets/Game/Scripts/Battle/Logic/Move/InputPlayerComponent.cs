using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>把本帧输入收成走位和放技能。找人走 SkillTargeting。</summary>
    public class InputPlayerComponent : Component
    {
        const float ClickArriveDist = 0.35f;
        const float ClickHoldStopDist = 0.12f;

        struct SkillPress
        {
            public int SkillId;
            public float PressTime;
            public SkillAim Aim;
        }

        sealed class SkillAimState
        {
            public int SkillId;
            public void Clear() => SkillId = 0;
        }

        sealed class ApproachState
        {
            public int SkillId;
            public long TargetId;
            public Vector3 Point;
            public bool HasPoint;
            public bool Sustain;

            public void Clear()
            {
                SkillId = 0;
                TargetId = 0;
                HasPoint = false;
                Sustain = false;
            }
        }

        sealed class ClickMoveState
        {
            public bool HasTarget;
            public Vector3 Target;
        }

        readonly SkillAimState _aim = new SkillAimState();
        readonly ApproachState _approach = new ApproachState();
        readonly ClickMoveState _clickMove = new ClickMoveState();
        readonly Queue<SkillPress> _pending = new Queue<SkillPress>(4);

        SkillCastComponent _skillComp;
        SkillCastComponent SkillComp =>
            _skillComp ??= Owner?.GetComponent<SkillCastComponent>();

        public bool IsAiming => _aim.SkillId != 0;
        public Vector3 WishDir { get; private set; }

        public override void OnPreUpdate(float dt)
        {
            Camera cam = Camera.main;
            PlayerInput.Collect(cam);

            var session = BattleSystem.Instance?.Session;
            if (session != null && (session.MatchEnded || session.LocalPlayerDead))
            {
                ClearSkillAim();
                ClearAttackLock();
                _clickMove.HasTarget = false;
                WishDir = Vector3.zero;
                _pending.Clear();
                return;
            }

            if (SkillTargeting.AttackTargetId != 0
                && !SkillTargeting.IsAliveTarget(SkillTargeting.AttackTargetId))
                SkillTargeting.ClearAttackTarget();

            var frame = PlayerInput.Frame;

            if (frame.Cancel)
            {
                ClearSkillAim();
                ClearAttackLock();
                SkillComp?.TryCancelByInterrupt();
            }

            if (frame.BeginAimSkillId > 0)
                BeginSkillAim(frame.BeginAimSkillId);

            TickAutoAttackSnap(frame);

            if (frame.Confirm && _aim.SkillId != 0)
                ConfirmAimCast(frame);

            WishDir = ResolveWishDir(frame, cam);
            AimAssist.Tick(WishDir);
            HandleWorldTap(frame, cam);
            FlushSkillPresses();
            AttackTargetMark.Ensure();
        }

        public void ClearApproach()
        {
            _approach.Clear();
        }

        void ClearAttackLock()
        {
            _approach.Clear();
            SkillTargeting.ClearAttackTarget();
        }

        public void ClearSkillAim()
        {
            _aim.Clear();
            PlayerInput.ActiveAimSkillId = 0;
            PlayerInput.AimFingerId = -1;
            PlayerInput.SkillAimStick = Vector2.zero;
        }

        void BeginSkillAim(int skillId)
        {
            _aim.SkillId = skillId;
            PlayerInput.ActiveAimSkillId = skillId;
            // 普攻无论有没有吸附目标，都画 CastRange 圈
            if (SkillAimRangeView.ShowIndicator)
                SkillAimRangeView.EnsureDrawer();
        }

        void TickAutoAttackSnap(PlayerInputFrame frame)
        {
            if (_aim.SkillId == 0) return;
            var def = SkillCatalog.Get(_aim.SkillId);
            if (def == null || !def.IsAutoAttack) return;

            long localId = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;
            if (localId == 0) return;

            Vector3 dir = frame.HasAimDir
                ? frame.AimDir
                : (SkillComp != null ? SkillComp.ResolveAimDir() : Vector3.forward);
            bool preferAim = frame.AimStickMag >= SkillTargeting.AutoAttackAimStickSnap
                             || (frame.HasAimDir && !PlayerInput.ShowsMovePad);
            SkillTargeting.TrySnapAutoAttack(localId, _aim.SkillId, dir, preferAim, out _);
        }

        void ConfirmAimCast(PlayerInputFrame frame)
        {
            int skillId = _aim.SkillId;
            Vector3 dir = frame.HasAimDir
                ? frame.AimDir
                : (SkillComp != null ? SkillComp.ResolveAimDir() : Vector3.forward);
            long entityId = 0;
            var def = SkillCatalog.Get(skillId);
            if (def != null && def.IsAutoAttack)
                entityId = SkillTargeting.AttackTargetId;
            ClearSkillAim();
            if (skillId <= 0) return;
            _pending.Enqueue(new SkillPress
            {
                SkillId = skillId,
                PressTime = Time.time,
                Aim = new SkillAim
                {
                    Dir = dir,
                    Point = frame.AimPoint,
                    HasPoint = frame.HasAimPoint,
                    EntityId = entityId,
                    StickMag = frame.AimStickMag
                }
            });
        }

        Vector3 ResolveWishDir(PlayerInputFrame frame, Camera cam)
        {
            Vector3 wishDir = LocalPlayerMoveMath.ToCameraRelativeMove(frame.MoveAxes, cam);
            if (wishDir.sqrMagnitude > 0.0001f)
            {
                _clickMove.HasTarget = false;
                ClearApproach();
            }
            else
                wishDir = TickClickMove(frame);

            return TickApproach(wishDir);
        }

        Vector3 TickClickMove(PlayerInputFrame frame)
        {
            var transformComp = Owner?.GetComponent<TransformComponent>();
            if (transformComp == null)
                return Vector3.zero;

            if (frame.HasClickMove)
            {
                _clickMove.Target = frame.ClickMovePoint;
                _clickMove.HasTarget = true;
                ClearApproach();
            }

            if (!_clickMove.HasTarget)
                return Vector3.zero;

            Vector3 to = _clickMove.Target - transformComp.Position;
            to.y = 0f;
            float stopDist = frame.ClickMoveHeld ? ClickHoldStopDist : ClickArriveDist;
            if (to.sqrMagnitude <= stopDist * stopDist)
            {
                if (!frame.ClickMoveHeld)
                    _clickMove.HasTarget = false;
                return Vector3.zero;
            }

            return to.normalized;
        }

        Vector3 TickApproach(Vector3 wishDir)
        {
            if (_approach.SkillId == 0 || SkillComp == null)
                return wishDir;

            long localId = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;
            var def = SkillCatalog.Get(_approach.SkillId);
            if (def == null)
            {
                ClearApproach();
                return wishDir;
            }

            if (wishDir.sqrMagnitude > 0.0001f)
            {
                ClearApproach();
                return wishDir;
            }

            var aim = new SkillAim
            {
                EntityId = _approach.TargetId,
                Point = _approach.Point,
                HasPoint = _approach.HasPoint,
                Dir = SkillComp.ResolveAimDir()
            };

            if (!SkillTargeting.TryResolve(localId, _approach.SkillId, aim, out var target))
            {
                if (def.IsAutoAttack)
                    SkillTargeting.ClearAttackTarget();
                ClearApproach();
                return wishDir;
            }

            if (_approach.TargetId != 0 && target.EntityId == 0)
            {
                ClearApproach();
                return wishDir;
            }

            if (!target.InRange)
                return target.Dir.sqrMagnitude > 0.0001f ? target.Dir : wishDir;

            if (SkillComp.TryCast(_approach.SkillId, target, Time.time))
            {
                if (!_approach.Sustain || !def.IsAutoAttack)
                    ClearApproach();
            }

            return _approach.HasPoint ? Vector3.zero : wishDir;
        }

        void HandleWorldTap(PlayerInputFrame frame, Camera cam)
        {
            if (_aim.SkillId != 0 || !frame.WorldTap) return;
            var follow = Owner?.GetComponent<CameraFollowComponent>();
            if (follow != null && follow.BlockWorldTap) return;

            long localId = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;
            if (!ScreenPick.TryPickEnemy(cam, localId, frame.WorldTapScreen, out long enemyId))
                return;

            int aaId = ResolveAutoAttackSkillId();
            var aaDef = aaId > 0 ? SkillCatalog.Get(aaId) : null;
            if (aaDef != null && aaDef.IsAutoAttack && aaDef.NeedsLockTarget)
                BeginApproach(aaId, enemyId, default, hasPoint: false, sustain: true);
        }

        void FlushSkillPresses()
        {
            if (SkillComp == null) return;
            long localId = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;

            while (_pending.Count > 0)
            {
                var intent = _pending.Dequeue();
                var def = SkillCatalog.Get(intent.SkillId);
                if (def == null) continue;

                if (intent.Aim.Dir.sqrMagnitude < 0.0001f)
                    intent.Aim.Dir = SkillComp.ResolveAimDir();

                if (!SkillTargeting.TryResolve(localId, intent.SkillId, intent.Aim, out var target))
                {
                    if (def.NeedsLockTarget)
                        Debug.LogWarning($"[Skill] {def.Name} 附近没有可攻击目标");
                    continue;
                }

                if (def.IsAutoAttack && def.NeedsLockTarget && target.EntityId != 0)
                {
                    BeginApproach(intent.SkillId, target.EntityId, default, false, sustain: true);
                    if (target.InRange)
                        SkillComp.TryCast(intent.SkillId, target, intent.PressTime);
                    continue;
                }

                if (def.CastApproach == CastApproachPolicy.WalkIntoRange
                    && !target.InRange
                    && (target.EntityId != 0 || target.HasPoint))
                {
                    BeginApproach(
                        intent.SkillId, target.EntityId, target.Point, target.HasPoint, sustain: false);
                    continue;
                }

                ClearApproach();
                SkillComp.TryCast(intent.SkillId, target, intent.PressTime);
            }
        }

        void BeginApproach(int skillId, long targetId, Vector3 point, bool hasPoint, bool sustain)
        {
            _approach.SkillId = skillId;
            _approach.TargetId = targetId;
            _approach.Point = point;
            _approach.HasPoint = hasPoint;
            _approach.Sustain = sustain;
            _clickMove.HasTarget = false;
            if (targetId != 0)
                SkillTargeting.SetAttackTarget(targetId);
        }

        static int ResolveAutoAttackSkillId()
        {
            int id = UnitCatalog.ResolveAttackSkillId(EEntityType.Player);
            return id > 0 && SkillCatalog.Get(id) != null ? id : 0;
        }
    }
}
