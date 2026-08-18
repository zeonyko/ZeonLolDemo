using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>已经准许出手、到点再算打中谁。</summary>
    public sealed class PendingCastService
    {
        public static PendingCastService Instance { get; } = new PendingCastService();

        /// <summary>还在等出手结果的技能（已取消的本帧末尾清掉）。</summary>
        private readonly List<PendingCastState> _pending = new List<PendingCastState>(16);

        /// <summary>本帧到期或要丢掉的，遍历时先记下来。</summary>
        private readonly List<PendingCastState> _due = new List<PendingCastState>(16);

        /// <summary>一次已准许的施法。多段出手共用这份上下文。</summary>
        public sealed class PendingCastState
        {
            public ClientSession Session;
            public long CasterId;
            public int SkillId;
            public uint ClientTick;
            public float DirX;
            public float DirY;
            public float DirZ;
            /// <summary>补偿延迟后的起手位置（位移技用）。</summary>
            public float OriginX;
            public float OriginY;
            public float OriginZ;
            public bool HasCompensatedOrigin;
            public Vector3Data TargetPosition;
            public long TargetId;
            /// <summary>下一段出手的服务器时间。</summary>
            public float ResolveAt;
            public bool Cancelled;
            /// <summary>下一段待结算的出手序号（从 0 起）。</summary>
            public int NextHitIndex;
            /// <summary>各段出手的服务器时间。</summary>
            public float[] HitResolveTimes;
        }

        /// <summary>这人是不是还有没取消、等着算出手的技能。</summary>
        public bool HasPending(long casterId)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (!_pending[i].Cancelled && _pending[i].CasterId == casterId)
                    return true;
            }
            return false;
        }

        /// <summary>取出这人当前还在等出手结果的技能。</summary>
        public bool TryGetActive(long casterId, out PendingCastState cast)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Cancelled || _pending[i].CasterId != casterId) continue;
                cast = _pending[i];
                return true;
            }
            cast = null;
            return false;
        }

        /// <summary>排进去。同一个人已有技能时先悄悄取消再换上，别多打一条取消。</summary>
        public void Enqueue(PendingCastState cast)
        {
            if (cast == null) return;
            if (HasPending(cast.CasterId))
            {
                Console.WriteLine(
                    $"[PendingCast] Supersede caster={cast.CasterId} skill={cast.SkillId} tick={cast.ClientTick}");
            }
            Cancel(cast.CasterId, broadcast: false, reason: EBattle_SkillCancelNotifyReason.ActiveCancel);
            _pending.Add(cast);
        }

        /// <summary>立刻取消。先标取消再从队列拿掉，避免还去算伤害。</summary>
        public bool Cancel(long casterId, bool broadcast, EBattle_SkillCancelNotifyReason reason)
        {
            bool any = false;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var cast = _pending[i];
                if (cast.CasterId != casterId || cast.Cancelled) continue;

                cast.Cancelled = true;
                _pending.RemoveAt(i);
                any = true;

                var scene = WorldManager.Instance?.GetDefaultScene();
                var caster = scene?.GetEntity(casterId);
                if (caster != null)
                    caster.State.ForceClearState(EntityStateTag.Casting);
                AreaService.Instance.CancelSkill(casterId, cast.SkillId);

                if (broadcast && caster != null)
                {
                    SkillResultService.BroadcastSkillCancel(caster, cast.SkillId, reason, cast.ClientTick);
                    Console.WriteLine(
                        $"[PendingCast] Cancel caster={casterId} skill={cast.SkillId} tick={cast.ClientTick} reason={reason}");
                }
            }

            return any;
        }

        /// <summary>每帧看谁到期。到期先出队再算；别先标取消，否则会当成已取消而没结果。</summary>
        public void Tick(float serverTime)
        {
            if (_pending.Count == 0) return;

            // 找出到期、已死或被控的
            _due.Clear();
            for (int i = 0; i < _pending.Count; i++)
            {
                var cast = _pending[i];
                if (cast.Cancelled) continue;

                var scene = WorldManager.Instance?.GetDefaultScene();
                var caster = scene?.GetEntity(cast.CasterId);

                if (caster == null || caster.Hp <= 0f || caster.State.HasState(EntityStateTag.Stun | EntityStateTag.Airborne))
                {
                    _due.Add(cast);
                    continue;
                }

                if (serverTime + 0.0001f >= cast.ResolveAt)
                    _due.Add(cast);
            }

            // 逐条算出手或广播打断
            for (int i = 0; i < _due.Count; i++)
            {
                var cast = _due[i];
                if (cast.Cancelled) continue;

                // 先出队再算。别先标取消，否则会当成已取消而没打中结果。
                _pending.Remove(cast);

                var scene = WorldManager.Instance?.GetDefaultScene();
                var caster = scene?.GetEntity(cast.CasterId);
                if (caster == null || caster.Hp <= 0f)
                {
                    cast.Cancelled = true;
                    // 人没了必须通知取消，否则客户端会一直等
                    if (caster != null)
                    {
                        caster.State.ForceClearState(EntityStateTag.Casting);
                        SkillResultService.BroadcastSkillCancel(
                            caster, cast.SkillId, EBattle_SkillCancelNotifyReason.Illegal, cast.ClientTick);
                    }
                    Console.WriteLine(
                        $"[PendingCast] DropResolve caster={cast.CasterId} skill={cast.SkillId} " +
                        $"tick={cast.ClientTick} reason={(caster == null ? "NoCaster" : "Dead")}");
                    continue;
                }

                caster.State.ForceClearState(EntityStateTag.Casting);

                // 被晕/浮空/沉默/定身等打断
                var windupDef = SkillCatalog.Get(cast.SkillId);
                bool controlInterrupt = windupDef != null
                    ? !caster.State.CanCastSkill(windupDef.IsProjectile, windupDef.AllowsCastWhileRooted)
                    : caster.State.HasState(
                        EntityStateTag.Stun | EntityStateTag.Airborne
                        | EntityStateTag.Silence | EntityStateTag.Root);
                if (controlInterrupt)
                {
                    cast.Cancelled = true;
                    SkillResultService.BroadcastSkillCancel(
                        caster, cast.SkillId, EBattle_SkillCancelNotifyReason.ControlInterrupt, cast.ClientTick);
                    Console.WriteLine(
                        $"[PendingCast] Cancel caster={cast.CasterId} skill={cast.SkillId} " +
                        $"tick={cast.ClientTick} reason=ControlInterrupt");
                    continue;
                }

                // 已经标了取消就别再算
                if (cast.Cancelled) continue;

                bool finished = BattleCommandHandler.ResolvePendingCast(cast);
                if (finished)
                {
                    cast.Cancelled = true; // 各段都算完了
                }
                else if (!cast.Cancelled)
                {
                    // 还有后续出手：保持施法中，重新入队
                    float now = BattleSystem.Instance?.ServerTime ?? cast.ResolveAt;
                    float left = cast.ResolveAt - now;
                    if (left < 0.05f) left = 0.05f;
                    // 等到最后一段
                    if (cast.HitResolveTimes != null && cast.HitResolveTimes.Length > 0)
                    {
                        float last = cast.HitResolveTimes[cast.HitResolveTimes.Length - 1];
                        left = Math.Max(0.05f, last - now);
                    }
                    caster.State.AddTimedState(EntityStateTag.Casting, now, left);
                    _pending.Add(cast);
                }
            }

            // 清掉已取消的
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Cancelled)
                    _pending.RemoveAt(i);
            }
        }

        /// <summary>清空全部等着出手的技能（场景重置 / 对局结束）。</summary>
        public void Clear() => _pending.Clear();
    }
}
