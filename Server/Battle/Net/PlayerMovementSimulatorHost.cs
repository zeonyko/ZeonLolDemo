using System;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>处理玩家移动输入，按服务器为准推进，再把位置对齐回去。</summary>
    public sealed class PlayerMovementSimulatorHost
    {
        public void OnPlayerInputCmd(ClientSession session, C2S_Battle_InputCmd req)
        {
            if (session.PlayerEntityId == 0 || session.PlayerEntityId != req.EntityId)
                return;

            var scene = WorldManager.Instance?.GetDefaultScene();
            var player = scene?.GetEntity(req.EntityId);
            if (player == null) return;

            if (req.LatestClientTick <= player.LastProcessedInputSeq)
                return;

            var frames = req.HistoryInputs;
            if (frames == null || frames.Length == 0)
                return;

            Array.Sort(frames, (a, b) =>
            {
                uint ta = a != null ? a.Tick : 0u;
                uint tb = b != null ? b.Tick : 0u;
                return ta.CompareTo(tb);
            });

            // 输入缺口补不上：强制拉回位置，禁止悄悄跳过确认
            if (TryForceRelocateOnInputGap(session, player, req, frames))
                return;

            for (int i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                if (frame == null) continue;
                if (frame.Tick <= player.LastProcessedInputSeq)
                    continue;
                if (frame.Tick > req.LatestClientTick)
                    continue;
                if (IsTickQueuedOrProcessed(player, frame.Tick))
                    continue;

                float dt = frame.DeltaTime;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f)
                    dt = GameConstants.ServerTickDt;
                if (dt > GameConstants.MaxInputDeltaTime)
                    dt = GameConstants.MaxInputDeltaTime;

                bool isMotion = (frame.InputFlags & SyncContract.InputFlags.MotionCommit) != 0;
                float moveX = frame.MoveIntent != null ? frame.MoveIntent.X : 0f;
                float moveZ = frame.MoveIntent != null ? frame.MoveIntent.Z : 0f;

                var pending = new Entity.PendingSimInput
                {
                    Tick = frame.Tick,
                    MoveX = moveX,
                    MoveZ = moveZ,
                    Jump = (frame.InputFlags & SyncContract.InputFlags.Jump) != 0,
                    DeltaTime = dt,
                    IsMotionCommit = isMotion,
                    MotionDirX = moveX,
                    MotionDirZ = moveZ,
                    MotionDistance = frame.MotionDistance
                };

                EnqueueInput(player, pending);
            }

            // 跟服务器对齐位置。位移提交要立刻对齐；技能还在飞时别用起点把人拉回去。
            bool sawMotionCommit = ProcessInputQueue(session, player, flushAll: true);
            if (sawMotionCommit && !HasPendingCasterMotion(player.Id))
                SendReconcile(session, player);
        }

        /// <summary>每帧按排队的输入推进移动，再把位置对齐回去。</summary>
        public void TickMovement(float dt)
        {
            var net = BattleNetPublisher.Instance;
            if (net == null) return;

            float serverTime = BattleSystem.Instance?.ServerTime ?? 0f;

            net.ForEachSession(session =>
            {
                if (session == null || !session.IsAlive || session.PlayerEntityId == 0)
                    return;

                var scene = WorldManager.Instance?.GetDefaultScene();
                var player = scene?.GetEntity(session.PlayerEntityId);
                if (player == null) return;

                // 真实过了多少时间，才允许模拟多少时间（防刷输入加速）
                player.InputSimBudget += dt;
                if (player.InputSimBudget > GameConstants.MaxInputSimBudget)
                    player.InputSimBudget = GameConstants.MaxInputSimBudget;

                uint ackBefore = player.LastProcessedInputSeq;
                ProcessInputQueue(session, player, flushAll: true);
                player.RecordPosHistory(serverTime);

                bool hadInput = player.LastProcessedInputSeq != ackBefore;
                bool inAir = !player.MoveState.IsGrounded
                             || player.MoveState.VelY > 0.01f
                             || player.MoveState.VelY < -0.01f;
                // 输入可能在收包时就被处理掉了，不能只靠 hadInput；用位置有没有变来判定走动。
                bool moved = player.HasBroadcastPos
                    && ((player.PosX - player.LastBroadcastPosX) * (player.PosX - player.LastBroadcastPosX)
                        + (player.PosY - player.LastBroadcastPosY) * (player.PosY - player.LastBroadcastPosY)
                        + (player.PosZ - player.LastBroadcastPosZ) * (player.PosZ - player.LastBroadcastPosZ) > 0.0001f);
                bool active = hadInput || inAir || moved || !player.HasBroadcastPos;

                if (active)
                    SendReconcile(session, player);

                // 走动每帧发给别人；真站着最多 0.5 秒一次。
                if (active
                    || serverTime - player.LastIdleSnapshotTime >= GameConstants.IdleSnapshotInterval)
                {
                    BroadcastSnapshotExcept(session, player);
                    player.LastIdleSnapshotTime = serverTime;
                    player.LastBroadcastPosX = player.PosX;
                    player.LastBroadcastPosY = player.PosY;
                    player.LastBroadcastPosZ = player.PosZ;
                    player.HasBroadcastPos = true;
                }
            });
        }

        /// <summary>输入缺了一大截：清队列、跳到最新帧，并强制拉回。小缺口（放技能会偷几帧）放过。</summary>
        private static bool TryForceRelocateOnInputGap(
            ClientSession session,
            Entity player,
            C2S_Battle_InputCmd req,
            SingleFrameInput[] frames)
        {
            const uint GapRelocateThreshold = 8;

            uint last = player.LastProcessedInputSeq;
            uint minNew = 0;
            bool anyNew = false;
            for (int i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                if (frame == null) continue;
                if (frame.Tick <= last) continue;
                if (frame.Tick > req.LatestClientTick) continue;
                if (!anyNew || frame.Tick < minNew)
                    minNew = frame.Tick;
                anyNew = true;
            }

            if (!anyNew || minNew <= last + 1)
                return false;

            uint gap = minNew - last - 1;
            if (gap < GapRelocateThreshold)
                return false;

            player.InputQueue.Clear();
            player.LastProcessedInputSeq = req.LatestClientTick;
            SkillResultService.BroadcastForceRelocate(player, EBattle_RelocateReason.ForcePull);
            SendReconcile(session, player);
            return true;
        }

        private static bool IsTickQueuedOrProcessed(Entity player, uint tick)
        {
            if (tick <= player.LastProcessedInputSeq)
                return true;
            for (int i = 0; i < player.InputQueue.Count; i++)
            {
                if (player.InputQueue[i].Tick == tick)
                    return true;
            }
            return false;
        }

        private static void EnqueueInput(Entity player, Entity.PendingSimInput input)
        {
            var q = player.InputQueue;
            int insertAt = q.Count;
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i].Tick > input.Tick)
                {
                    insertAt = i;
                    break;
                }
            }
            q.Insert(insertAt, input);

            // 队列满了：先模拟最旧的再丢掉，别丢帧却不往前走
            while (q.Count > SyncContract.MaxPendingInputs)
            {
                var oldest = q[0];
                q.RemoveAt(0);
                if (oldest.Tick <= player.LastProcessedInputSeq)
                    continue;
                SimulateOneInput(player, oldest);
                player.LastProcessedInputSeq = oldest.Tick;
            }
        }

        internal static void ClearInputQueue(Entity player)
        {
            // 丢掉闪现前的移动；队列里已有位移提交时，确认帧号跟到那一帧
            uint ack = player.LastProcessedInputSeq;
            for (int i = 0; i < player.InputQueue.Count; i++)
            {
                var inp = player.InputQueue[i];
                if (inp.IsMotionCommit && inp.Tick > ack)
                    ack = inp.Tick;
            }
            player.InputQueue.Clear();
            if (ack > player.LastProcessedInputSeq)
                player.LastProcessedInputSeq = ack;
        }

        /// <summary>按帧号消费输入。模拟时间不够就留着下次。位移提交要立刻对齐位置。</summary>
        private bool ProcessInputQueue(ClientSession session, Entity player, bool flushAll)
        {
            int processed = 0;
            const int maxPerCall = 128;
            bool sawMotionCommit = false;

            while (player.InputQueue.Count > 0 && processed < maxPerCall)
            {
                var input = player.InputQueue[0];
                if (input.Tick <= player.LastProcessedInputSeq)
                {
                    player.InputQueue.RemoveAt(0);
                    continue;
                }

                float cost = input.DeltaTime;
                if (cost <= 0f) cost = GameConstants.ServerTickDt;
                if (player.InputSimBudget < cost)
                    break;

                if (input.IsMotionCommit)
                    sawMotionCommit = true;

                player.InputSimBudget -= cost;
                SimulateOneInput(player, input);
                player.LastProcessedInputSeq = input.Tick;
                player.InputQueue.RemoveAt(0);
                processed++;

                if (!flushAll)
                    break;
            }

            return sawMotionCommit;
        }

        private static void SimulateOneInput(Entity player, Entity.PendingSimInput input)
        {
            if (input.IsMotionCommit)
            {
                // 只记「收到了」。真正位移由冲刺/闪现算一次，这里再算会走两段。
                return;
            }

            float moveX = input.MoveX;
            float moveZ = input.MoveZ;
            bool jump = input.Jump;

            if (player.Hp <= 0f)
            {
                moveX = 0f;
                moveZ = 0f;
                jump = false;
            }

            float moveIntentSq = moveX * moveX + moveZ * moveZ;
            if (PendingCastService.Instance.TryGetActive(player.Id, out var pendingMove))
            {
                var pendingDef = SkillCatalog.Get(pendingMove.SkillId);
                if (pendingDef != null && moveIntentSq > 0.0001f && pendingDef.CanMoveInterrupt)
                {
                    PendingCastService.Instance.Cancel(
                        player.Id, broadcast: true, EBattle_SkillCancelNotifyReason.ActiveCancel);
                }
                else if (pendingDef != null && pendingDef.LocksMovementWhileCasting)
                {
                    // 引导/蓄力/位移技：pending 期间忽略走路，避免和冲刺叠成两段。
                    moveX = 0f;
                    moveZ = 0f;
                }
            }

            if (!player.State.CanMove)
            {
                moveX = 0f;
                moveZ = 0f;
                jump = false;
            }

            // 摇杆只给方向；减速倍率用服务器状态，防客户端不管减速
            player.MoveState = MovementSimulator.Simulate(player.MoveState, new MoveInput
            {
                MoveX = moveX,
                MoveZ = moveZ,
                Jump = jump,
                DeltaTime = input.DeltaTime,
                SpeedMultiplier = player.State.MoveSpeedMultiplier
            });
        }

        internal static bool HasPendingCasterMotion(long casterId)
        {
            if (!PendingCastService.Instance.TryGetActive(casterId, out var pending) || pending == null)
                return false;
            var def = SkillCatalog.Get(pending.SkillId);
            return def != null && def.HasCasterMotion;
        }

        /// <summary>冲刺/闪现：先回到起手点再位移，避免和提交叠成走两段。</summary>
        internal static void ApplyMotionResolveOrigin(Entity caster, PendingCastService.PendingCastState cast)
        {
            if (caster == null || cast == null) return;
            caster.MoveState = MovementState.FromPosition(cast.OriginX, cast.OriginY, cast.OriginZ);
        }

        internal static void SendReconcile(ClientSession session, Entity player, EBattle_ReconcileReason reason = EBattle_ReconcileReason.PredictionError)
        {
            if (session == null || player == null) return;
            session.Send(EOpCode.S2C_Battle_ReconcilePacket, new S2C_Battle_ReconcilePacket
            {
                AckClientTick = player.LastProcessedInputSeq,
                CorrectTf = TransformData.Of(player.PosX, player.PosY, player.PosZ),
                Velocity = Vector3Data.Of(0f, player.MoveState.VelY, 0f),
                ReconcileReason = (uint)reason,
                IsGrounded = player.MoveState.IsGrounded ? 1 : 0
            });
        }

        internal static void BroadcastSnapshotExcept(ClientSession _, Entity player, bool hardSnap = false)
        {
            SnapshotOutbox.Enqueue(player, hardSnap);
        }
    }
}
