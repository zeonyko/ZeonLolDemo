using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>本地玩家网络同步：自己先动，再跟服务器对齐。技能位移单独提交。</summary>
    public class PredictionMovementComponent : Component
    {
        #region 依赖组件（用到再取）
        private TransformComponent _transformComp;
        private ViewComponent _viewComp;
        private StateComponent _stateComp;
        private SkillCastComponent _skillComp;
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        private ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        private StateComponent StateComp => _stateComp ??= Owner?.GetComponent<StateComponent>();
        private SkillCastComponent SkillComp =>
            _skillComp ??= Owner?.GetComponent<SkillCastComponent>();
        #endregion

        #region 先动队列 / 未发送输入
        private readonly PredictionBuffer _predictionBuffer = new PredictionBuffer(256);
        private readonly List<SingleFrameInput> _unsentInputs = new List<SingleFrameInput>(SyncContract.MaxPendingInputs);
        private uint _sequence;
        private float _lastFlushTime = -999f;
        #endregion

        #region 跳跃防抖（起跳后一小段别被服务器“已落地”拉回去）
        /// <summary>起跳后短时间防抖，避免刚跳就被服务器拉回地面。</summary>
        private sealed class JumpGuardState
        {
            public float GuardUntil = -999f;
            public float MinVelY;

            public void Begin(float jumpForce)
            {
                GuardUntil = Time.time + 0.85f;
                MinVelY = Mathf.Max(1f, jumpForce * 0.35f);
            }
        }

        private readonly JumpGuardState _jumpGuard = new JumpGuardState();
        #endregion

        #region 位移技等服务器确认
        /// <summary>位移类技能提交后，等待服务端 Ack 覆盖本条 MotionCommit Tick 期间的状态。</summary>
        private sealed class MotionAwaitState
        {
            public uint PendingCommitTick;
            public bool Awaiting;

            public void Begin(uint tick)
            {
                PendingCommitTick = tick;
                Awaiting = true;
            }

            public void Clear()
            {
                Awaiting = false;
                PendingCommitTick = 0;
            }
        }

        private readonly MotionAwaitState _motionAwait = new MotionAwaitState();
        #endregion

        #region 状态查询
        public uint CurrentSequence => _sequence;
        public int PendingCount => _predictionBuffer.Count;
        public bool ShouldPredictLocally => SyncDebugSettings.LocalPrediction;
        #endregion

        #region 公开 API：本地输入采集与发送
        public void RecordAndSendInput(float moveX, float moveZ, bool jump, float deltaTime, bool forceFlush = false)
        {
            if (TransformComp == null || Owner == null) return;

            uint tick = TickClock.Instance.NextClientTick();
            _sequence = tick;

            float dt = deltaTime;
            if (dt <= 0f) dt = Time.deltaTime;
            if (dt > GameConstants.MaxInputDeltaTime) dt = GameConstants.MaxInputDeltaTime;

            _predictionBuffer.Enqueue(new PendingPrediction
            {
                Sequence = tick,
                MoveX = moveX,
                MoveZ = moveZ,
                Jump = jump,
                DeltaTime = dt
            });

            var frame = new SingleFrameInput
            {
                Tick = tick,
                MoveIntent = Vector3Data.Of(moveX, 0f, moveZ),
                InputFlags = jump ? SyncContract.InputFlags.Jump : 0u,
                DeltaTime = dt,
                MotionDistance = 0f
            };
            PushUnsent(frame);
            if (forceFlush
                || Time.time - _lastFlushTime >= GameConstants.InputSendInterval
                || _unsentInputs.Count >= SyncContract.MaxPendingInputs)
                FlushInputCmd();
        }
        #endregion

        #region 公开 API：位移类技能预测（Jump / Motion）
        public void BeginJumpPrediction(float jumpForce)
        {
            _jumpGuard.Begin(jumpForce);
        }

        /// <summary>位移技起手：挡住冲刺前的走路对账。真正提交位移时再改成具体 tick。</summary>
        public void BeginMotionOwnedWindow()
        {
            _predictionBuffer.Clear();
            _motionAwait.Begin(uint.MaxValue);
        }

        /// <summary>位移窗结束（时间轴自然结束 / 销毁）。不改坐标。</summary>
        public void EndMotionOwnedWindow()
        {
            ClearMotionAwait();
        }

        /// <summary>时间轴提交闪现/冲刺：未发出的走路帧一并立刻发出，不丢 tick。</summary>
        public void BeginMotionPrediction(float motionDistance, float dirX, float dirZ)
        {
            _predictionBuffer.Clear();

            uint tick = TickClock.Instance.NextClientTick();
            _sequence = tick;
            _motionAwait.Begin(tick);

            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            var frame = new SingleFrameInput
            {
                Tick = tick,
                MoveIntent = Vector3Data.Of(dirX, 0f, dirZ),
                InputFlags = SyncContract.InputFlags.MotionCommit,
                DeltaTime = 0f,
                MotionDistance = motionDistance
            };
            PushUnsent(frame);
            FlushInputCmd();
        }
        #endregion

        #region 未发送输入（内部）
        private void PushUnsent(SingleFrameInput frame)
        {
            _unsentInputs.Add(frame);
        }

        private void FlushInputCmd()
        {
            if (Owner == null || _unsentInputs.Count == 0) return;

            uint latestTick = _unsentInputs[_unsentInputs.Count - 1].Tick;
            BattleNetHandler.SendInput(new C2S_Battle_InputCmd
            {
                EntityId = Owner.Id,
                LatestClientTick = latestTick,
                HistoryInputs = _unsentInputs.ToArray()
            });
            _unsentInputs.Clear();
            _lastFlushTime = Time.time;
        }

        private void TrimUnsent(uint ackTick)
        {
            while (_unsentInputs.Count > 0 && _unsentInputs[0].Tick <= ackTick)
                _unsentInputs.RemoveAt(0);
        }
        #endregion

        #region 公开 API：服务器硬拉 / 拒绝
        /// <summary>服务器硬拉位置（复活 / 强拉 / 切场景）。技能位移不走这里。</summary>
        public void ApplyForceRelocate(Vector3 pos, EBattle_RelocateReason _)
        {
            ClearMotionAwait();
            FlushInputCmd();
            _predictionBuffer.Clear();
            _unsentInputs.Clear();
            TransformComp?.SnapPosition(pos);
            ViewComp?.SnapToLogic();
        }

        public void OnMotionRejectedByServer(Vector3 authPos)
        {
            ClearMotionAwait();
            FlushInputCmd();
            _predictionBuffer.Clear();
            _unsentInputs.Clear();
            if (TransformComp == null) return;
            TransformComp.SnapPosition(authPos);
            ViewComp?.SnapToLogic();
        }
        #endregion

        #region 跟服务器对齐
        public void OnServerReconcile(S2C_Battle_ReconcilePacket msg)
        {
            if (TransformComp == null || msg == null) return;

            var authPos = msg.CorrectTf?.Position != null
                ? new Vector3(msg.CorrectTf.Position.X, msg.CorrectTf.Position.Y, msg.CorrectTf.Position.Z)
                : TransformComp.Position;

            float velY = msg.Velocity != null ? msg.Velocity.Y : 0f;
            uint ackTick = msg.AckClientTick;

            if (ShouldIgnoreStaleJumpReconcile(msg, velY))
                return;

            // 位移窗：比本次提交更旧的对账丢掉（还是冲刺前的走路位置）。
            // 多段冲刺：对上当前段就改落点，但位移技锁步期间不要关闸，避免后几段被走路包拽回去。
            if (_motionAwait.Awaiting)
            {
                if (ackTick < _motionAwait.PendingCommitTick)
                    return;

                TrimUnsent(ackTick);
                ApplyMotionOwnedReconcile(authPos, velY, msg.IsGrounded != 0, ackTick);

                if (SkillComp != null && SkillComp.LocksMovementNow)
                    return;

                ClearMotionAwait();
                return;
            }

            TrimUnsent(ackTick);

            float authLead = Vector3.Distance(TransformComp.Position, authPos);

            if (!SyncDebugSettings.LocalPrediction)
            {
                ApplyHardReconcile(authPos, velY, msg.IsGrounded != 0, ackTick, authLead);
                return;
            }

            ApplyPredictedReconcile(authPos, velY, msg.IsGrounded != 0, ackTick, authLead);
        }

        /// <summary>位移窗内的对账：只贴权威落点，不重演走路（冲刺不在预测队列里）。</summary>
        private void ApplyMotionOwnedReconcile(Vector3 authPos, float velY, bool isGrounded, uint ackTick)
        {
            _predictionBuffer.Clear();
            float authLead = Vector3.Distance(TransformComp.Position, authPos);
            if (authLead <= GameConstants.ReconcileIgnoreDistance)
            {
                TransformComp.VelY = velY;
                TransformComp.IsGrounded = isGrounded;
                SyncCompareComponent.Instance?.ReportReconcile(
                    authPos, authLead, authLead, 0, ackTick, applied: false, hardSnap: false);
                return;
            }

            bool hardSnap = authLead >= GameConstants.ReconcileSnapDistance;
            SyncCompareComponent.Instance?.ReportReconcile(
                authPos, authLead, 0f, 0, ackTick, applied: true, hardSnap: hardSnap);
            TransformComp.ApplyMovementState(authPos.x, authPos.y, authPos.z, velY, isGrounded);
            ViewComp?.SnapToLogic();
        }

        /// <summary>关掉本地先动时：直接用服务器位置，不重演输入。</summary>
        private void ApplyHardReconcile(Vector3 authPos, float velY, bool isGrounded, uint ackTick, float authLead)
        {
            _predictionBuffer.Clear();
            SyncCompareComponent.Instance?.ReportReconcile(
                authPos, authLead, authLead, 0, ackTick, applied: true, hardSnap: true);
            TransformComp.ApplyMovementState(
                authPos.x, authPos.y, authPos.z,
                velY, isGrounded);
            ViewComp?.SnapToLogic();
        }

        /// <summary>开了本地先动：从服务器位置重演未确认输入，差太多就立刻贴齐。</summary>
        private void ApplyPredictedReconcile(Vector3 authPos, float velY, bool isGrounded, uint ackTick, float authLead)
        {
            var auth = new ClientReconciliation.AuthoritativeState
            {
                LastProcessedSequence = ackTick,
                MoveState = new MovementState
                {
                    PosX = authPos.x,
                    PosY = authPos.y,
                    PosZ = authPos.z,
                    VelY = velY,
                    IsGrounded = isGrounded
                }
            };

            float speedMul = StateComp != null ? StateComp.MoveSpeedMultiplier : 1f;
            bool applied = ClientReconciliation.TryReconcile(
                auth,
                _predictionBuffer,
                TransformComp.Position,
                speedMul,
                out var corrected,
                out bool hardSnap);

            var correctedPos = new Vector3(corrected.PosX, corrected.PosY, corrected.PosZ);
            float errorAfter = Vector3.Distance(TransformComp.Position, correctedPos);
            SyncCompareComponent.Instance?.ReportReconcile(
                authPos, authLead, errorAfter, _predictionBuffer.Count, ackTick, applied, hardSnap);

            if (!applied)
            {
                TransformComp.VelY = corrected.VelY;
                TransformComp.IsGrounded = corrected.IsGrounded;
                return;
            }

            TransformComp.ApplyMovementState(
                corrected.PosX, corrected.PosY, corrected.PosZ,
                corrected.VelY, corrected.IsGrounded);
            if (hardSnap)
                ViewComp?.SnapToLogic();
        }

        private void ClearMotionAwait()
        {
            _motionAwait.Clear();
        }
        #endregion

        #region 跳跃回滚防抖（内部）
        private bool ShouldIgnoreStaleJumpReconcile(S2C_Battle_ReconcilePacket msg, float velY)
        {
            if (Time.time >= _jumpGuard.GuardUntil || TransformComp == null)
                return false;

            bool localAscending = !TransformComp.IsGrounded && TransformComp.VelY > 0.5f;
            if (!localAscending)
                return false;

            return msg.IsGrounded != 0 || velY < _jumpGuard.MinVelY;
        }
        #endregion
    }
}
