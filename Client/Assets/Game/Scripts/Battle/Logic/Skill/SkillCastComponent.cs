using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>一次施法的会话：排队、起手、确认超时、取消。找人走 SkillTargeting。</summary>
    public class SkillCastComponent : Component
    {
        readonly SkillCooldownService _cooldown = new SkillCooldownService();
        readonly SkillInputBuffer _inputBuffer = new SkillInputBuffer();

        SkillAbility _activeCast;
        bool _startingFromBuffer;
        bool _hasMotionPredict;
        bool _inRecovery;

        sealed class PendingCast
        {
            public int SkillId;
            public uint Sequence;
            public bool AwaitingAck;
            public float AckDeadline;
            public bool HasPending => SkillId != 0 || Sequence != 0;

            public void Clear()
            {
                SkillId = 0;
                Sequence = 0;
                AwaitingAck = false;
            }
        }

        readonly PendingCast _pending = new PendingCast();

        sealed class CastFacingState
        {
            public Vector3 Direction;
            public bool Holding;

            public void Clear()
            {
                Holding = false;
                Direction = Vector3.zero;
            }
        }

        readonly CastFacingState _facing = new CastFacingState();

        StateComponent _stateComp;
        TransformComponent _transformComp;
        ViewComponent _viewComp;
        PredictionMovementComponent _syncComp;

        StateComponent StateComp => _stateComp ??= Owner?.GetComponent<StateComponent>();
        TransformComponent TransformComp => _transformComp ??= Owner?.GetComponent<TransformComponent>();
        ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        PredictionMovementComponent SyncComp => _syncComp ??= Owner?.GetComponent<PredictionMovementComponent>();
        AnimationComponent AnimComp => Owner?.GetComponent<AnimationComponent>();

        public bool IsCasting => _activeCast != null && _activeCast.IsPlaying;
        public int CastingSkillId => IsCasting ? _pending.SkillId : 0;
        public int PendingSkillId => _pending.SkillId;
        public string LastDenyReason { get; private set; } = "-";
        public bool IsInRecovery => _inRecovery && IsCasting;

        public bool LocksMovementNow
        {
            get
            {
                if (!IsCasting || _pending.SkillId == 0) return false;
                var def = SkillCatalog.Get(_pending.SkillId);
                if (def == null || !def.LocksMovementWhileCasting) return false;
                if (def.SkillType == SkillType.Channeling || def.SkillType == SkillType.Charge)
                    return true;
                if (!def.HasCasterMotion) return true;
                if (!SkillClipUtil.TryGetLastMotionEndTime(def.Clips, out float motionEnd))
                    return true;
                return _activeCast == null || _activeCast.Time <= motionEnd;
            }
        }

        public bool TryGetCastFacing(out Vector3 faceDir)
        {
            faceDir = _facing.Direction;
            return _facing.Holding && IsCasting && !_inRecovery && faceDir.sqrMagnitude > 0.0001f;
        }

        public float GetCooldownRemaining(int skillId) => _cooldown.GetCooldownRemaining(skillId);

        public bool TryCast(int skillId, SkillTarget target, float pressTime = -1f)
        {
            return TryCast(skillId, target.ToAim(), pressTime);
        }

        public bool TryCast(int skillId, SkillAim aim, float pressTime = -1f)
        {
            var session = BattleSystem.Instance?.Session;
            if (session != null && (session.MatchEnded || session.LocalPlayerDead))
                return false;

            if (SkillCatalog.Get(skillId) == null)
            {
                Debug.LogWarning($"[Skill] 未知技能 {skillId}");
                return false;
            }

            if (aim.Dir.sqrMagnitude < 0.0001f)
                aim.Dir = ResolveAimDir();

            if (IsCasting && skillId == _pending.SkillId)
                return true;

            _inputBuffer.Push(skillId, aim, pressTime);
            FlushInputBuffer();
            return IsCasting;
        }

        public void PlayLocalCastTimeline(
            int skillId,
            Vector3 direction = default,
            CastPresentationTarget presentationTarget = default,
            bool allowLogical = false)
        {
            if (direction.sqrMagnitude < 0.0001f)
                direction = ResolveAimDir();

            ApplyOrientOnCast(skillId, direction);

            if (StateComp != null)
                StateComp.TryEnterSkill(skillId, direction, allowLogical, presentationTarget);
            else
                BeginTimeline(skillId, direction, allowLogical, presentationTarget);
        }

        public void PlayRemoteCast(int skillId, Vector3 targetPos, long targetId = 0)
        {
            Vector3 casterPos = TransformComp != null ? TransformComp.Position : Vector3.zero;
            Vector3 dir = targetPos - casterPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            else dir.Normalize();

            PlayLocalCastTimeline(skillId, dir, CastPresentationTarget.Point(targetPos, targetId));
        }

        public void OnRemoteCancel(uint cancelReason)
        {
            RollbackCast(resetCooldown: false, 0, rejectMotion: false, default, cancelReason);
        }

        public void OnSkillCancel(int skillId, uint sequence, uint cancelReason, Vector3 authCasterPos)
        {
            if (_pending.Sequence != 0 && sequence != 0 && sequence != _pending.Sequence)
                return;
            if (_pending.SkillId != 0 && skillId != _pending.SkillId && sequence == 0)
                return;

            LastDenyReason = $"服务器取消 skill={skillId} {DescribeCancel(cancelReason)}";

            bool resetCd = _pending.HasPending
                           && cancelReason == (uint)EBattle_SkillCancelNotifyReason.Illegal;
            bool rejectMotion = _hasMotionPredict
                                || (SkillCatalog.Get(skillId)?.HasCasterMotion ?? false);
            RollbackCast(resetCd, skillId, rejectMotion, authCasterPos, cancelReason);
        }

        public void OnSkillRejected(int skillId, uint sequence, Vector3 authCasterPos)
        {
            OnSkillCancel(skillId, sequence, (uint)EBattle_SkillCancelNotifyReason.Illegal, authCasterPos);
        }

        public void OnHitAcknowledged(int skillId, uint hitIndex)
        {
            if (_pending.SkillId == 0 || skillId != _pending.SkillId)
                return;
            if (SkillClipUtil.TryGetHitByIndex(skillId, hitIndex + 1, out _))
                return;
            ClearPending();
        }

        public void OnResultConfirmed() => ClearPending();

        public override void OnDestroy()
        {
            AbortCast();
            ClearPending();
            _inputBuffer.Clear();
            _hasMotionPredict = false;
            ClearCastFacing();
            _syncComp?.EndMotionOwnedWindow();
            _stateComp = null;
            _transformComp = null;
            _viewComp = null;
            _syncComp = null;
        }

        public bool TryCancelByMove() =>
            TryCancelCast(EBattle_CancelCastReason.Move, requireMoveInterruptFlag: true);

        public bool TryCancelByInterrupt() =>
            TryCancelCast(EBattle_CancelCastReason.Dodge, requireMoveInterruptFlag: false);

        bool TryCancelCast(EBattle_CancelCastReason reason, bool requireMoveInterruptFlag)
        {
            if (!IsCasting || _pending.SkillId == 0) return false;
            var def = SkillCatalog.Get(_pending.SkillId);
            if (def == null) return false;
            if (requireMoveInterruptFlag && !def.CanMoveInterrupt) return false;

            uint seq = _pending.Sequence;
            int skillId = _pending.SkillId;
            AbortCast();
            ClearPending();
            StateComp?.ResolveLocomotion();
            SkillNetEmitter.SendCancelCmd(skillId, (uint)reason, seq);
            return true;
        }

        public void MarkMotionPredicted() => _hasMotionPredict = true;

        void ClearPending() => _pending.Clear();

        public void OnCastAcknowledged(int skillId)
        {
            if (!_pending.AwaitingAck) return;
            if (_pending.SkillId != 0 && skillId != _pending.SkillId) return;
            _pending.AwaitingAck = false;
        }

        void OnCastAckTimeout()
        {
            if (!_pending.AwaitingAck) return;

            int skillId = _pending.SkillId;
            LastDenyReason = $"CastAck超时 skill={skillId}";
            Debug.LogWarning($"[Skill] CastAck 超时 skill={skillId}，回滚 CD 并打断本地 Timeline");
            bool hadMotion = _hasMotionPredict
                             || (skillId != 0 && (SkillCatalog.Get(skillId)?.HasCasterMotion ?? false));
            Vector3 pos = TransformComp != null ? TransformComp.Position : Vector3.zero;
            RollbackCast(resetCooldown: true, skillId, hadMotion, pos, 0);
        }

        void RollbackCast(
            bool resetCooldown,
            int skillId,
            bool rejectMotion,
            Vector3 authCasterPos,
            uint cancelReason)
        {
            if (resetCooldown && skillId != 0)
                _cooldown.ResetCooldown(skillId);

            ClearPending();
            AbortCast();
            StateComp?.ResolveLocomotion();

            if (cancelReason == (uint)EBattle_SkillCancelNotifyReason.ControlInterrupt)
            {
                Vector3 pos = TransformComp != null ? TransformComp.Position : Vector3.zero;
                SkillHitPresenter.PlayReaction(TargetReactionIds.Interrupt, Owner, pos);
            }

            if (rejectMotion)
            {
                SyncComp?.OnMotionRejectedByServer(authCasterPos);
                _hasMotionPredict = false;
            }
            else
            {
                SyncComp?.EndMotionOwnedWindow();
            }
        }

        public void BeginTimeline(
            int skillId,
            Vector3 dir,
            bool allowLogical,
            CastPresentationTarget presentationTarget = default)
        {
            _inRecovery = false;
            _activeCast = SkillCastPresenter.Start(
                Owner, skillId, dir, allowLogical, presentationTarget);
        }

        public void EnterRecovery()
        {
            if (!IsCasting || _inRecovery) return;
            _inRecovery = true;
            ClearCastFacing();
        }

        public void TickCast(float dt)
        {
            if (_pending.AwaitingAck && Time.realtimeSinceStartup >= _pending.AckDeadline)
                OnCastAckTimeout();

            var cast = _activeCast;
            if (cast != null)
            {
                FlushInputBuffer();
                if (!LocksMovementNow)
                    SyncComp?.EndMotionOwnedWindow();
                if (!cast.IsPlaying)
                    FinishActiveCast();
            }
            else
            {
                FlushInputBuffer();
            }
        }

        public void AbortCast()
        {
            int skillId = _activeCast != null ? _activeCast.SkillId : 0;
            _activeCast?.Abort();
            EndCastSession();
            if (skillId != 0)
                SkillAimAreaView.CancelSkill(skillId);
        }

        void FinishActiveCast()
        {
            _activeCast?.Finish();
            EndCastSession();
            SyncComp?.EndMotionOwnedWindow();
        }

        void EndCastSession()
        {
            _activeCast = null;
            _inRecovery = false;
            ClearCastFacing();
            AnimComp?.ReleaseActionHold();
        }

        public Vector3 ResolveAimDir(Vector3 preferDir = default)
        {
            Vector3 facing = ViewComp != null ? ViewComp.FaceDir : Vector3.forward;
            return AimAssist.Resolve(preferDir, facing);
        }

        void ApplyOrientOnCast(int skillId, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                ClearCastFacing();
                return;
            }
            dir.Normalize();

            var def = SkillCatalog.Get(skillId);
            if (def == null || !def.OrientToTargetOnCast)
            {
                ClearCastFacing();
                return;
            }

            _facing.Direction = dir;
            _facing.Holding = true;
            ViewComp?.SnapFace(dir);
        }

        void ClearCastFacing() => _facing.Clear();

        void FlushInputBuffer()
        {
            if (_startingFromBuffer) return;
            _inputBuffer.PurgeExpired();

            while (_inputBuffer.TryPeek(out var entry))
            {
                if (IsCasting && entry.SkillId == _pending.SkillId)
                {
                    _inputBuffer.TryPop(out _);
                    continue;
                }

                if (IsCasting)
                {
                    bool inCancel = _activeCast.IsInCancelWindow;
                    bool inAccept = _activeCast.IsInAcceptWindow;
                    bool isCancelSkill = SkillRules.IsCancelSkill(entry.SkillId);

                    if (inCancel && isCancelSkill)
                    {
                        if (!CanBeginCast(entry.SkillId, out _))
                        {
                            _inputBuffer.TryPop(out _);
                            continue;
                        }

                        if (!_inputBuffer.TryPop(out entry))
                            return;

                        AbortCast();
                        if (TryBeginFromBuffer(entry))
                            return;
                        continue;
                    }

                    if (!inAccept)
                        return;
                }

                if (!CanBeginCast(entry.SkillId, out _))
                {
                    _inputBuffer.TryPop(out _);
                    continue;
                }

                if (!_inputBuffer.TryPop(out entry))
                    return;

                if (IsCasting)
                    AbortCast();

                if (TryBeginFromBuffer(entry))
                    return;
            }
        }

        bool TryBeginFromBuffer(SkillInputBuffer.Entry entry)
        {
            _startingFromBuffer = true;
            bool ok = BeginCast(entry.SkillId, entry.Aim);
            _startingFromBuffer = false;
            return ok;
        }

        bool CanBeginCast(int skillId, out SkillConfig data)
        {
            long ownerId = Owner != null ? Owner.Id : 0;
            return SkillCastGate.CanBegin(
                skillId, _cooldown, StateComp, TransformComp, ownerId, out data);
        }

        bool BeginCast(int skillId, SkillAim aim)
        {
            if (!CanBeginCast(skillId, out var data))
            {
                LogCastDenied(skillId, data);
                return false;
            }

            long casterId = Owner != null ? Owner.Id : 0;
            if (!SkillTargeting.TryResolve(casterId, skillId, aim, out var target))
            {
                LogCastDenied(skillId, data);
                return false;
            }

            Vector3 dir = target.Dir;
            float dirX = dir.x;
            float dirZ = dir.z;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            dir = new Vector3(dirX, 0f, dirZ);

            ApplyOrientOnCast(skillId, dir);

            _cooldown.MarkCast(skillId);
            _hasMotionPredict = false;
            _inRecovery = false;
            uint clientTick = TickClock.Instance.NextClientTick();
            _pending.SkillId = skillId;
            _pending.Sequence = clientTick;
            _pending.AwaitingAck = false;

            var presentationTarget = target.HasPoint
                ? CastPresentationTarget.Point(target.Point, target.EntityId)
                : target.EntityId != 0
                    ? CastPresentationTarget.Point(target.Point, target.EntityId)
                    : CastPresentationTarget.None;

            var syncComp = SyncComp;
            bool isLocalNet = syncComp != null;
            bool allowLogical = data.HasCasterMotion && (syncComp == null || syncComp.ShouldPredictLocally);

            if (data.HasCasterMotion)
                syncComp?.BeginMotionOwnedWindow();

            if (isLocalNet)
            {
                EmitCastCmd(skillId, dir, clientTick, target);
                _pending.AwaitingAck = true;
                _pending.AckDeadline = Time.realtimeSinceStartup + GameConstants.CastAckTimeoutSeconds;
            }

            if (StateComp != null)
                return StateComp.TryEnterSkill(data.SkillId, dir, allowLogical, presentationTarget);

            BeginTimeline(data.SkillId, dir, allowLogical, presentationTarget);
            return true;
        }

        void LogCastDenied(int skillId, SkillConfig data)
        {
            LastDenyReason = DescribeDeny(skillId, data);
            Debug.LogWarning("[Skill] " + LastDenyReason);
        }

        string DescribeDeny(int skillId, SkillConfig data)
        {
            if (data == null)
                return $"未知技能 {skillId}";
            if (!_cooldown.IsCooldownReady(skillId))
                return $"{data.Name} 冷却中，剩余 {_cooldown.GetCooldownRemaining(skillId):F1}s";
            if (StateComp != null && !StateComp.CanCastSkill(data.IsProjectile, data.AllowsCastWhileRooted))
                return $"{data.Name} 当前状态不可施法";
            if (StateComp != null && !StateComp.CanAttack)
                return $"{data.Name} 当前不可攻击";
            if (TransformComp != null && !TransformComp.IsGrounded)
                return $"{data.Name} 不在地面";
            if (data.NeedsLockTarget)
                return $"{data.Name} 附近没有可攻击目标";
            return $"{data.Name} 无法施放";
        }

        static string DescribeCancel(uint cancelReason)
        {
            switch ((EBattle_SkillCancelNotifyReason)cancelReason)
            {
                case EBattle_SkillCancelNotifyReason.ActiveCancel: return "主动取消";
                case EBattle_SkillCancelNotifyReason.ControlInterrupt: return "受控打断";
                case EBattle_SkillCancelNotifyReason.Illegal: return "非法作废";
                case EBattle_SkillCancelNotifyReason.Rejected: return "服务器拒绝";
                default: return "reason=" + cancelReason;
            }
        }

        void EmitCastCmd(int skillId, Vector3 dir, uint clientTick, SkillTarget target)
        {
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

            Vector3 casterPos = TransformComp != null ? TransformComp.Position : Vector3.zero;
            float rotY = Quaternion.LookRotation(dir).eulerAngles.y;

            var def = SkillCatalog.Get(skillId);
            float range = SkillTargeting.GetRange(def);
            Vector3 targetPos = casterPos + new Vector3(dir.x, 0f, dir.z).normalized * range;

            if (target.HasPoint)
                targetPos = SkillTargeting.ClampPoint(casterPos, target.Point, range);
            else if (SkillRules.IsSelfTarget(def))
                targetPos = casterPos;
            else if (target.EntityId != 0 && SkillTargeting.TryGetWorldPos(target.EntityId, out var lockPos))
                targetPos = lockPos;

            SkillNetEmitter.SendCastCmd(new C2S_Battle_SkillCastCmd
            {
                SkillId = skillId,
                ClientTick = clientTick,
                CasterTransform = TransformData.Of(casterPos.x, casterPos.y, casterPos.z, 0f, rotY, 0f),
                TargetPosition = Vector3Data.Of(targetPos.x, targetPos.y, targetPos.z),
                TargetId = target.EntityId
            });
        }
    }
}
