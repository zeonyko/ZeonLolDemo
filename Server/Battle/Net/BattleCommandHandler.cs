using System;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>战斗命令入口：对时、移动、放技能、取消、断线清理、再战准备。</summary>
    public class BattleCommandHandler
    {
        private readonly PlayerMovementSimulatorHost _movement = new PlayerMovementSimulatorHost();

        /// <summary>按消息类型分给对应处理。解析失败就丢掉这条。</summary>
        public void OnReceive(ClientSession session, EOpCode opCode, string json)
        {
            switch (opCode)
            {
                case EOpCode.C2S_Battle_SyncTimeCmd:
                {
                    var msg = NetJson.Parse<C2S_Battle_SyncTimeCmd>(json);
                    if (msg != null) OnSyncTimeCmd(session, msg);
                    break;
                }
                case EOpCode.C2S_Battle_InputCmd:
                {
                    var msg = NetJson.Parse<C2S_Battle_InputCmd>(json);
                    if (msg != null) _movement.OnPlayerInputCmd(session, msg);
                    break;
                }
                case EOpCode.C2S_Battle_SkillCastCmd:
                {
                    var msg = NetJson.Parse<C2S_Battle_SkillCastCmd>(json);
                    if (msg != null) OnSkillCastCmd(session, msg);
                    break;
                }
                case EOpCode.C2S_Battle_SkillCancelCmd:
                {
                    var msg = NetJson.Parse<C2S_Battle_SkillCancelCmd>(json);
                    if (msg != null) OnSkillCancelCmd(session, msg);
                    break;
                }
                case EOpCode.C2S_Battle_RematchReadyCmd:
                {
                    var msg = NetJson.Parse<C2S_Battle_RematchReadyCmd>(json);
                    if (msg != null) OnRematchReady(session, msg);
                    break;
                }
            }
        }

        /// <summary>处理再战准备。开战中点了不算。</summary>
        private void OnRematchReady(ClientSession session, C2S_Battle_RematchReadyCmd req)
        {
            if (session == null || session.PlayerEntityId == 0) return;

            var scene = WorldManager.Instance?.GetDefaultScene();
            if (scene == null || scene.MatchPlaying) return;

            bool ready = req == null || req.Ready != 0;
            scene.SetRematchReady(session.PlayerEntityId, ready);
            Console.WriteLine(
                $"[BattleNet] RematchReady entity={session.PlayerEntityId} ready={ready}");
        }

        /// <summary>对时：估单向延迟，回服务器时间和帧号。延迟限制在 0～2 秒。</summary>
        public void OnSyncTimeCmd(ClientSession session, C2S_Battle_SyncTimeCmd req)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long rttMs = nowMs - req.ClientSendTimestamp;
            if (rttMs < 0) rttMs = 0;
            if (rttMs > 2000) rttMs = 2000;

            session.OneWayDelaySeconds = rttMs / 2000f;

            session.Send(EOpCode.S2C_Battle_SyncTimePacket, new S2C_Battle_SyncTimePacket
            {
                ClientSendTimestamp = req.ClientSendTimestamp,
                ServerTime = nowMs,
                ServerTick = BattleSystem.Instance?.ServerTick ?? 0
            });
        }

        /// <summary>断线：立刻清等着出手的技能、地面区域、弹道、Buff，再把人从场上拿走。</summary>
        public void OnClientDisconnected(ClientSession session)
        {
            if (session == null || session.PlayerEntityId == 0) return;

            long leaveId = session.PlayerEntityId;
            session.PlayerEntityId = 0;

            PendingCastService.Instance.Cancel(leaveId, broadcast: false, EBattle_SkillCancelNotifyReason.Illegal);
            // 立刻清掉这人留下的地面区域、弹道、Buff，别等下一帧
            AreaService.Instance.CancelCaster(leaveId);
            ProjectileService.Instance.RemoveByCaster(leaveId);
            BuffService.Instance.ClearEntity(leaveId);

            var scene = WorldManager.Instance?.GetDefaultScene();
            var leaving = scene?.GetEntity(leaveId);
            leaving?.State.ClearAll();
            scene?.RemoveEntity(leaveId);

            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
            {
                EntityId = leaveId,
                StateType = (int)EEntityStateType.Destroy,
                EntityData = null
            });

            scene?.OnRematchPlayerLeft(leaveId);

            Console.WriteLine($"[BattleNet] Player offline Id={leaveId}");
        }

        /// <summary>推进玩家移动一帧。</summary>
        public void TickMovement(float dt) => _movement.TickMovement(dt);

        /// <summary>处理主动取消。走路打断但配置不允许时，只拉回位置，不取消技能。</summary>
        private void OnSkillCancelCmd(ClientSession session, C2S_Battle_SkillCancelCmd req)
        {
            if (session.PlayerEntityId == 0)
                return;

            long casterId = session.PlayerEntityId;

            if (!PendingCastService.Instance.TryGetActive(casterId, out var pending))
                return;

            if (req.SkillId != 0 && pending.SkillId != req.SkillId)
                return;

            if (req.ClientTick != 0 && pending.ClientTick != req.ClientTick)
                return;

            var def = SkillCatalog.Get(pending.SkillId);
            var clientReason = (EBattle_CancelCastReason)req.CancelReason;
            if (clientReason == EBattle_CancelCastReason.Move
                && def != null
                && !def.CanMoveInterrupt)
            {
                var caster = WorldManager.Instance?.GetDefaultScene()?.GetEntity(casterId);
                if (caster != null)
                    PlayerMovementSimulatorHost.SendReconcile(session, caster);
                return;
            }

            PendingCastService.Instance.Cancel(casterId, broadcast: true, EBattle_SkillCancelNotifyReason.ActiveCancel);
        }

        /// <summary>处理放技能：能放就记下，到点再算打中谁。</summary>
        private void OnSkillCastCmd(ClientSession session, C2S_Battle_SkillCastCmd req)
        {
            if (session.PlayerEntityId == 0)
                return;

            var def = SkillCatalog.Get(req.SkillId);
            var scene = WorldManager.Instance?.GetDefaultScene();
            var caster = scene?.GetEntity(session.PlayerEntityId);

            if (!SkillCastValidator.TryAdmitCast(session, req, out var admit))
            {
                RejectCast(session, caster, def, req.ClientTick);
                return;
            }

            caster = admit.Caster;
            def = admit.Def;
            scene = admit.Scene;
            long targetEntityId = admit.TargetEntityId;

            float serverTime = BattleSystem.Instance?.ServerTime ?? 0f;
            uint serverTick = BattleSystem.Instance?.ServerTick ?? 0;

            float oneWay = ComputeOneWayDelay(session, req.ClientTick, serverTick);

            // 位移技：尽量用出手那一帧的位置。客户端报的位置只在合理范围内才信，Y 始终用服务器的。
            float originX = caster.PosX;
            float originY = caster.PosY;
            float originZ = caster.PosZ;
            bool hasCompensated = false;

            if (def.HasCasterMotion)
            {
                bool got = false;
                float rx = originX, ry = originY, rz = originZ;

                if (req.ClientTick != 0
                    && caster.TrySamplePosAtClientTick(req.ClientTick, out rx, out ry, out rz))
                {
                    got = true;
                }
                else if (caster.TrySamplePosAt(serverTime - oneWay, out rx, out ry, out rz))
                {
                    got = true;
                }

                if (got)
                {
                    originX = rx;
                    originY = ry;
                    originZ = rz;
                    hasCompensated = true;

                    if (req.CasterTransform?.Position != null)
                    {
                        float cx = req.CasterTransform.Position.X;
                        float cy = req.CasterTransform.Position.Y;
                        float cz = req.CasterTransform.Position.Z;
                        float err = MovementSimulator.Distance3D(rx, ry, rz, cx, cy, cz);
                        // 允许大约「延迟里能跑过的距离」，但最多 2 米，防止高延迟乱闪
                        float maxErr = Math.Min(
                            2f, Math.Max(1.25f, GameConstants.MoveSpeed * oneWay * 2f + 0.75f));
                        if (err <= maxErr)
                        {
                            originX = cx;
                            // Y 始终用服务器记下的，防垂直坐标乱改
                            originY = ry;
                            originZ = cz;
                        }
                    }
                }
            }

            float dirX = (req.TargetPosition?.X ?? originX) - originX;
            float dirY = (req.TargetPosition?.Y ?? originY) - originY;
            float dirZ = (req.TargetPosition?.Z ?? originZ) - originZ;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            var targetPos = req.TargetPosition ?? Vector3Data.Of(originX + dirX, originY, originZ + dirZ);
            if (SkillRules.NeedsPointTarget(def) && def.CastRange > 0.1f)
            {
                float px = targetPos.X;
                float pz = targetPos.Z;
                SkillRules.ClampPoint2D(originX, originZ, ref px, ref pz, def.CastRange);
                targetPos = Vector3Data.Of(px, GameConstants.GroundY, pz);
                dirX = px - originX;
                dirZ = pz - originZ;
                SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            }

            caster.SetLastCastTime(def.SkillId, serverTime);

            // 多段出手：相对时间换成服务器时间；已经过了的当现在算
            float[] hitRelTimes = SkillHitResolver.CollectHitTimes(def);
            float castLogicalStart = serverTime - oneWay;
            float[] hitAbsTimes;
            if (hitRelTimes.Length == 0)
            {
                hitAbsTimes = Array.Empty<float>();
            }
            else
            {
                hitAbsTimes = new float[hitRelTimes.Length];
                for (int i = 0; i < hitRelTimes.Length; i++)
                {
                    float t = castLogicalStart + hitRelTimes[i];
                    if (t < serverTime) t = serverTime;
                    hitAbsTimes[i] = t;
                }
            }

            float firstResolveAt = hitAbsTimes.Length > 0 ? hitAbsTimes[0] : serverTime;
            float lastResolveAt = hitAbsTimes.Length > 0 ? hitAbsTimes[hitAbsTimes.Length - 1] : serverTime;
            float castLock = Math.Max(lastResolveAt - serverTime, 0.05f);
            caster.State.AddTimedState(EntityStateTag.Casting, serverTime, castLock);

            SkillResultService.BroadcastSkillCast(caster, def.SkillId, targetPos, targetEntityId);

            var pending = new PendingCastService.PendingCastState
            {
                Session = session,
                CasterId = caster.Id,
                SkillId = def.SkillId,
                ClientTick = req.ClientTick,
                DirX = dirX,
                DirY = dirY,
                DirZ = dirZ,
                OriginX = originX,
                OriginY = originY,
                OriginZ = originZ,
                HasCompensatedOrigin = hasCompensated,
                TargetPosition = targetPos,
                TargetId = targetEntityId,
                ResolveAt = firstResolveAt,
                Cancelled = false,
                NextHitIndex = 0,
                HitResolveTimes = hitAbsTimes
            };

            if (firstResolveAt <= serverTime + 0.0001f)
            {
                // 已经到点的段立刻算（可能连跳多段）
                while (!pending.Cancelled)
                {
                    bool finished = ResolvePendingCast(pending);
                    if (finished) break;
                    if (pending.ResolveAt > serverTime + 0.0001f)
                    {
                        PendingCastService.Instance.Enqueue(pending);
                        break;
                    }
                }
                return;
            }

            PendingCastService.Instance.Enqueue(pending);
            float firstHitRel = hitRelTimes.Length > 0 ? hitRelTimes[0] : 0f;
            Console.WriteLine(
                $"[BattleNet] CastAccept skill={def.SkillId} tick={req.ClientTick} hits={hitAbsTimes.Length} " +
                $"firstHitRel={firstHitRel:F2} oneWay={oneWay:F3} firstIn={firstResolveAt - serverTime:F2}");
        }

        /// <summary>出手用的单向延迟：只用对时估的一半往返，上限 1 秒。客户端帧号差不是延迟。</summary>
        private static float ComputeOneWayDelay(ClientSession session, uint clientTick, uint serverTick)
        {
            float syncDelay = session?.OneWayDelaySeconds ?? 0f;
            if (syncDelay < 0f) syncDelay = 0f;
            if (syncDelay > 1f) syncDelay = 1f;
            return syncDelay;
        }

        /// <summary>判定前临时回到出手位置。位移技自己处理，这里跳过。判定后必须还原。</summary>
        private static void ApplyCompensatedOriginIfAny(Entity caster, PendingCastService.PendingCastState cast)
        {
            if (caster == null || cast == null || !cast.HasCompensatedOrigin)
                return;

            caster.MoveState = MovementState.FromPosition(cast.OriginX, cast.OriginY, cast.OriginZ);
        }

        /// <summary>算当前这一段出手。已取消的直接结束，别再打伤害。</summary>
        public static bool ResolvePendingCast(PendingCastService.PendingCastState cast)
        {
            if (cast == null || cast.Cancelled) return true;

            var def = SkillCatalog.Get(cast.SkillId);
            if (def == null)
            {
                Console.WriteLine($"[BattleNet] ResolveSkip skill={cast.SkillId} reason=NoDef");
                return true;
            }

            var scene = WorldManager.Instance?.GetDefaultScene();
            var caster = scene?.GetEntity(cast.CasterId);
            if (caster == null)
            {
                Console.WriteLine($"[BattleNet] ResolveSkip skill={cast.SkillId} reason=NoCaster");
                return true;
            }

            var session = cast.Session;
            int hitIndex = cast.NextHitIndex;
            int totalHits = cast.HitResolveTimes != null ? cast.HitResolveTimes.Length : 0;

            Console.WriteLine(
                $"[BattleNet] ResolveStart skill={def.SkillId} caster={caster.Id} tick={cast.ClientTick} " +
                $"hit={hitIndex}/{totalHits} resolve={def.ResolveMode} motion={def.HasCasterMotion}");

            // 和起手检查一样：该看能不能放、能不能攻击再看一遍
            bool castBlocked = !caster.State.CanCastSkill(def.IsProjectile, def.AllowsCastWhileRooted);
            bool attackBlocked = SkillRules.RequiresAttackGate(def) && !caster.State.CanAttack;
            if (castBlocked || attackBlocked)
            {
                // 被控打断用「控制打断」，别当成非法操作去拉位置
                var reason = castBlocked
                    ? EBattle_SkillCancelNotifyReason.ControlInterrupt
                    : EBattle_SkillCancelNotifyReason.Illegal;
                Console.WriteLine(
                    $"[BattleNet] ResolveCancel skill={def.SkillId} caster={caster.Id} " +
                    $"reason={reason} canCast={!castBlocked} canAttack={caster.State.CanAttack}");
                SkillResultService.BroadcastSkillCancel(
                    caster, def.SkillId, reason, cast.ClientTick);
                if (def.HasCasterMotion && session != null)
                    PlayerMovementSimulatorHost.SendReconcile(session, caster);
                return true;
            }

            // 判定前临时回到出手位置，判定后必须还原
            bool restoreOrigin = false;
            MovementState savedMove = default;
            if (!def.HasCasterMotion && def.HasDirectShapeHit && cast.HasCompensatedOrigin)
            {
                savedMove = caster.MoveState;
                ApplyCompensatedOriginIfAny(caster, cast);
                restoreOrigin = true;
            }

            try
            {
                // 没有出手段：只做位移或空放一次
                if (totalHits == 0)
                {
                    SkillHitResolver.ResolveCast(session, caster, scene, def, cast);
                    return true;
                }

                bool hasMore = SkillHitResolver.ResolveHitSegment(
                    session, caster, scene, def, cast, hitIndex);
                cast.NextHitIndex = hitIndex + 1;
                if (!hasMore || cast.HitResolveTimes == null || cast.NextHitIndex >= cast.HitResolveTimes.Length)
                    return true;

                cast.ResolveAt = cast.HitResolveTimes[cast.NextHitIndex];
                return false;
            }
            finally
            {
                if (restoreOrigin)
                    caster.MoveState = savedMove;
            }
        }

        /// <summary>起手被拒：位移技拉回位置，并通知取消。</summary>
        private static void RejectCast(ClientSession session, Entity caster, SkillConfig def, uint clientTick)
        {
            if (def != null && def.HasCasterMotion)
                PlayerMovementSimulatorHost.SendReconcile(session, caster);

            SkillResultService.BroadcastSkillCancel(
                caster, def != null ? def.SkillId : 0, EBattle_SkillCancelNotifyReason.Rejected, clientTick);
        }
    }
}
