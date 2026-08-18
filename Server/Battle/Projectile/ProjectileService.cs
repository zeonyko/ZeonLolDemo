using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>管飞行物：放出、推进、打中人或飞到头爆炸。</summary>
    public sealed class ProjectileService
    {
        public static ProjectileService Instance { get; } = new ProjectileService();

        /// <summary>弹道编号，进程内不重复。</summary>
        private long _nextInstanceId = 1;

        /// <summary>当前还在飞的弹道。</summary>
        private readonly List<ProjectileState> _active = new List<ProjectileState>(32);

        /// <summary>本帧要删的弹道，遍历时先记下来。</summary>
        private readonly List<ProjectileState> _toRemove = new List<ProjectileState>(8);

        /// <summary>晚点才落地爆炸的清单。</summary>
        private readonly List<DelayedImpactState> _delayedImpacts = new List<DelayedImpactState>(8);

        /// <summary>一条正在飞的弹道。</summary>
        private sealed class ProjectileState
        {
            public long InstanceId;
            public int ProjectileId;
            public int SkillId;
            public long CasterId;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float DirX;
            public float DirZ;
            public float Speed;
            /// <summary>碰撞半宽。</summary>
            public float Width;
            public float MaxDistance;
            public float Traveled;
            public long TargetId;
            /// <summary>true=跟着目标飞；false=直线飞。</summary>
            public bool Locked;
            /// <summary>已经打过的人，避免重复算。</summary>
            public readonly HashSet<long> HitIds = new HashSet<long>();
            /// <summary>还能打中几次。</summary>
            public int HitsLeft;
            /// <summary>第几段命中（从 0 起）。</summary>
            public uint HitIndex;
            /// <summary>出手时带过来的命中数据。</summary>
            public SkillHitPayload SourceHit;
        }

        /// <summary>晚点才落地爆炸。</summary>
        private sealed class DelayedImpactState
        {
            public float ResolveAt;
            public long CasterId;
            public int SkillId;
            public int ProjectileId;
            public uint HitIndex;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float DirX;
            public float DirZ;
        }

        /// <summary>清空全部弹道（对局结束用）。</summary>
        public void Clear()
        {
            _active.Clear();
            _toRemove.Clear();
            _delayedImpacts.Clear();
        }

        /// <summary>断线时立刻清掉这人的弹道。先清延迟爆炸，再清还在飞的。</summary>
        public void RemoveByCaster(long casterId)
        {
            for (int i = _delayedImpacts.Count - 1; i >= 0; i--)
            {
                if (_delayedImpacts[i].CasterId == casterId)
                    _delayedImpacts.RemoveAt(i);
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                if (p.CasterId != casterId) continue;
                Despawn(p, EBattle_ProjectileDespawnReason.Cancelled);
                _active.RemoveAt(i);
            }
        }

        /// <summary>安排一段时间后在落点爆炸。极短延迟会提到 0.05 秒。</summary>
        public void ScheduleDelayedImpact(
            long casterId,
            int skillId,
            int projectileId,
            uint hitIndex,
            float posX, float posY, float posZ,
            float dirX, float dirZ,
            float delaySeconds)
        {
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            if (delaySeconds < 0.05f) delaySeconds = 0.05f;
            _delayedImpacts.Add(new DelayedImpactState
            {
                ResolveAt = now + delaySeconds,
                CasterId = casterId,
                SkillId = skillId,
                ProjectileId = projectileId,
                HitIndex = hitIndex,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                DirX = dirX,
                DirZ = dirZ
            });
        }

        /// <summary>放出直线弹道。失败也要通知客户端，别让它一直等。</summary>
        public long SpawnLinearFromHit(
            Entity caster,
            Scene scene,
            SkillConfig def,
            int projectileId,
            SkillHitPayload sourceHit,
            uint hitIndex,
            float originX, float originY, float originZ,
            float dirX, float dirZ)
        {
            if (caster == null || scene == null || def == null) return 0;
            if (!TryResolveFlightParams(def, projectileId, out var speed, out var maxDist, out var width, out var hitsLeft))
            {
                SkillHitResolver.BroadcastHit(caster, def.SkillId, hitIndex, Array.Empty<SkillHitEntry>());
                return 0;
            }

            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            var state = CreateState(caster, def, projectileId, speed, maxDist, width, hitsLeft, sourceHit, hitIndex);
            state.PosX = originX;
            state.PosY = originY;
            state.PosZ = originZ;
            state.DirX = dirX;
            state.DirZ = dirZ;
            state.Locked = false;
            state.TargetId = 0;

            _active.Add(state);
            BroadcastSpawn(state);
            Console.WriteLine(
                $"[Projectile] SpawnLinear id={state.InstanceId} skill={def.SkillId} " +
                $"proj={projectileId} speed={speed:F1} max={maxDist:F1} w={width:F2}");
            return state.InstanceId;
        }

        /// <summary>放出跟着目标飞的弹道。目标无效时通知客户端，不生成空弹。</summary>
        public long SpawnLockedFromHit(
            Entity caster,
            Scene scene,
            SkillConfig def,
            long targetId,
            int projectileId,
            SkillHitPayload sourceHit,
            uint hitIndex,
            float originX, float originY, float originZ)
        {
            if (caster == null || scene == null || def == null) return 0;

            var target = scene.GetEntity(targetId);
            if (target == null
                || target.Hp <= 0f
                || !SkillResultService.IsValidSkillTarget(caster, target, def.SkillId))
            {
                SkillHitResolver.BroadcastHit(caster, def.SkillId, hitIndex, Array.Empty<SkillHitEntry>());
                return 0;
            }

            if (!TryResolveFlightParams(def, projectileId, out var speed, out var maxDist, out var width, out var hitsLeft))
            {
                SkillHitResolver.BroadcastHit(caster, def.SkillId, hitIndex, Array.Empty<SkillHitEntry>());
                return 0;
            }

            float dirX = target.PosX - originX;
            float dirZ = target.PosZ - originZ;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            var state = CreateState(caster, def, projectileId, speed, maxDist, width, hitsLeft, sourceHit, hitIndex);
            state.PosX = originX;
            state.PosY = originY;
            state.PosZ = originZ;
            state.DirX = dirX;
            state.DirZ = dirZ;
            state.Locked = true;
            state.TargetId = targetId;

            _active.Add(state);
            BroadcastSpawn(state);
            Console.WriteLine(
                $"[Projectile] SpawnLocked id={state.InstanceId} skill={def.SkillId} " +
                $"proj={projectileId} target={targetId}");
            return state.InstanceId;
        }

        /// <summary>每帧推进弹道。先落地爆炸，再飞；对局结束要清干净。</summary>
        public void Tick(float dt)
        {
            TickDelayedImpacts();

            if (_active.Count == 0 || dt <= 0f) return;

            var scene = WorldManager.Instance?.GetDefaultScene();
            if (scene == null || scene.MatchEnded)
            {
                if (scene != null && scene.MatchEnded)
                    Clear();
                return;
            }

            _toRemove.Clear();
            for (int i = 0; i < _active.Count; i++)
            {
                var p = _active[i];
                if (TickOne(p, scene, dt))
                    _toRemove.Add(p);
            }

            for (int i = 0; i < _toRemove.Count; i++)
                _active.Remove(_toRemove[i]);
        }

        /// <summary>处理到期的落地爆炸。倒着删，避免漏或重复。</summary>
        private void TickDelayedImpacts()
        {
            if (_delayedImpacts.Count == 0) return;

            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            var scene = WorldManager.Instance?.GetDefaultScene();
            for (int i = _delayedImpacts.Count - 1; i >= 0; i--)
            {
                var d = _delayedImpacts[i];
                if (now + 0.0001f < d.ResolveAt) continue;
                _delayedImpacts.RemoveAt(i);

                if (scene == null || scene.MatchEnded) continue;
                var caster = scene.GetEntity(d.CasterId);
                var def = SkillCatalog.Get(d.SkillId);
                if (caster == null || caster.Hp <= 0f || def == null) continue;

                SkillHitResolver.ResolveProjectileDelayedImpact(
                    caster, scene, def, d.ProjectileId,
                    d.PosX, d.PosY, d.PosZ, d.DirX, d.DirZ, d.HitIndex);
            }
        }

        /// <summary>推进一条弹道。施法者死了就取消；步长不能为 0。</summary>
        private bool TickOne(ProjectileState p, Scene scene, float dt)
        {
            var caster = scene.GetEntity(p.CasterId);
            if (caster == null || caster.Hp <= 0f)
            {
                Despawn(p, EBattle_ProjectileDespawnReason.Cancelled);
                return true;
            }

            float step = p.Speed * dt;
            if (step < 0.0001f) step = 0.0001f;

            if (p.Locked)
                return TickLocked(p, scene, caster, step);

            return TickLinear(p, scene, caster, step);
        }

        /// <summary>直线弹道飞一帧：扫到人就打，飞到头就炸。打满或爆炸后立刻消失。</summary>
        private bool TickLinear(ProjectileState p, Scene scene, Entity caster, float step)
        {
            float remain = p.MaxDistance - p.Traveled;
            if (remain <= 0.0001f)
            {
                // 飞到头还没打中：有爆炸就在终点炸
                TryExpireImpact(p, scene, caster);
                Despawn(p, EBattle_ProjectileDespawnReason.Expired);
                return true;
            }

            if (step > remain) step = remain;

            float prevX = p.PosX;
            float prevZ = p.PosZ;
            float nextX = prevX + p.DirX * step;
            float nextZ = prevZ + p.DirZ * step;
            p.PosX = nextX;
            p.PosZ = nextZ;
            p.Traveled += step;

            float widthSq = p.Width * p.Width;
            var ordered = CollectSegmentHits(p, scene, caster, prevX, prevZ, nextX, nextZ, widthSq);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!ApplyHit(p, scene, caster, ordered[i]))
                {
                    Despawn(p, EBattle_ProjectileDespawnReason.HitCap);
                    return true;
                }
            }

            if (p.Traveled >= p.MaxDistance - 0.0001f)
            {
                TryExpireImpact(p, scene, caster);
                Despawn(p, EBattle_ProjectileDespawnReason.Expired);
                return true;
            }

            return false;
        }

        /// <summary>锁定弹道飞一帧：跟着目标，碰到就打并消失。目标没了当飞空。</summary>
        private bool TickLocked(ProjectileState p, Scene scene, Entity caster, float step)
        {
            var target = scene.GetEntity(p.TargetId);
            if (target == null
                || target.Hp <= 0f
                || !SkillResultService.IsValidSkillTarget(caster, target, p.SkillId))
            {
                Despawn(p, EBattle_ProjectileDespawnReason.Expired);
                return true;
            }

            float remain = p.MaxDistance - p.Traveled;
            if (remain <= 0.0001f)
            {
                Despawn(p, EBattle_ProjectileDespawnReason.Expired);
                return true;
            }

            float dx = target.PosX - p.PosX;
            float dz = target.PosZ - p.PosZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.0001f)
            {
                if (!ApplyHit(p, scene, caster, target))
                {
                    Despawn(p, EBattle_ProjectileDespawnReason.HitCap);
                    return true;
                }

                Despawn(p, EBattle_ProjectileDespawnReason.HitCap);
                return true;
            }

            p.DirX = dx / dist;
            p.DirZ = dz / dist;

            // 刚出手给一点最短飞行，避免贴脸瞬间 Hit+Despawn 导致客户端看不见弹道
            const float minTravel = 0.35f;
            bool allowHit = p.Traveled >= minTravel;

            if (allowHit && (dist <= p.Width || step >= dist))
            {
                p.PosX = target.PosX;
                p.PosZ = target.PosZ;
                p.Traveled += Math.Min(step, dist);
                if (!ApplyHit(p, scene, caster, target))
                {
                    Despawn(p, EBattle_ProjectileDespawnReason.HitCap);
                    return true;
                }

                Despawn(p, EBattle_ProjectileDespawnReason.HitCap);
                return true;
            }

            float move = step;
            if (move > remain) move = remain;
            if (!allowHit && move > dist - 0.05f)
                move = Math.Max(0.05f, dist - 0.05f);
            p.PosX += p.DirX * move;
            p.PosZ += p.DirZ * move;
            p.Traveled += move;

            if (p.Traveled >= p.MaxDistance - 0.0001f)
            {
                Despawn(p, EBattle_ProjectileDespawnReason.Expired);
                return true;
            }

            return false;
        }

        /// <summary>飞到头还没打中人时，有爆炸就在终点炸。</summary>
        private void TryExpireImpact(ProjectileState p, Scene scene, Entity caster)
        {
            if (!ProjectileCatalog.TryGetImpact(p.ProjectileId, out _))
                return;
            var def = SkillCatalog.Get(p.SkillId);
            if (def == null) return;

            SkillHitResolver.ResolveProjectileContact(
                caster, scene, def, p.SourceHit, p.ProjectileId, null,
                p.PosX, p.PosY, p.PosZ, p.DirX, p.DirZ, p.HitIndex);
        }

        /// <summary>本帧线段扫到谁，从近到远排。已经打过的跳过。</summary>
        private static List<Entity> CollectSegmentHits(
            ProjectileState p,
            Scene scene,
            Entity caster,
            float ax, float az,
            float bx, float bz,
            float widthSq)
        {
            var hits = new List<(Entity e, float t)>(8);
            foreach (var entity in scene.GetAllEntities())
            {
                if (entity.Id == caster.Id) continue;
                if (entity.Hp <= 0f) continue;
                if (p.HitIds.Contains(entity.Id)) continue;
                if (!SkillResultService.IsValidSkillTarget(caster, entity, p.SkillId)) continue;

                float distSq = SkillRules.DistPointToSegmentSq2D(
                    entity.PosX, entity.PosZ, ax, az, bx, bz);
                if (distSq > widthSq) continue;

                float abx = bx - ax;
                float abz = bz - az;
                float apx = entity.PosX - ax;
                float apz = entity.PosZ - az;
                float abLenSq = abx * abx + abz * abz;
                float t = abLenSq > 0.0001f ? (apx * abx + apz * abz) / abLenSq : 0f;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                hits.Add((entity, t));
            }

            hits.Sort((a, b) => a.t.CompareTo(b.t));
            var result = new List<Entity>(hits.Count);
            for (int i = 0; i < hits.Count; i++)
                result.Add(hits[i].e);
            return result;
        }

        /// <summary>打中一个人。有落地爆炸就立刻结束，即使还能打几次。</summary>
        private bool ApplyHit(ProjectileState p, Scene scene, Entity caster, Entity target)
        {
            if (target == null || p.HitIds.Contains(target.Id)) return true;

            p.HitIds.Add(target.Id);
            var def = SkillCatalog.Get(p.SkillId);
            if (def != null)
            {
                SkillHitResolver.ResolveProjectileContact(
                    caster, scene, def, p.SourceHit, p.ProjectileId, target,
                    p.PosX, p.PosY, p.PosZ, p.DirX, p.DirZ, p.HitIndex);
            }

            p.HitIndex++;
            p.HitsLeft--;

            Console.WriteLine(
                $"[Projectile] Hit id={p.InstanceId} skill={p.SkillId} " +
                $"target={target.Id} left={p.HitsLeft}");

            if (ProjectileCatalog.TryGetImpact(p.ProjectileId, out _))
                return false;

            return p.HitsLeft > 0;
        }

        /// <summary>通知客户端弹道消失。取消时别播爆炸。</summary>
        private void Despawn(ProjectileState p, EBattle_ProjectileDespawnReason reason)
        {
            bool hasImpact = reason != EBattle_ProjectileDespawnReason.Cancelled
                             && ProjectileCatalog.TryGetImpact(p.ProjectileId, out _);

            BattleNetPublisher.Instance.BroadcastAll(
                EOpCode.S2C_Battle_ProjectileDespawnPacket,
                new S2C_Battle_ProjectileDespawnPacket
                {
                    ProjectileInstanceId = p.InstanceId,
                    Reason = (uint)reason,
                    SkillId = p.SkillId,
                    HasImpact = hasImpact ? 1 : 0,
                    ImpactPos = Vector3Data.Of(p.PosX, p.PosY, p.PosZ)
                });
        }

        /// <summary>通知客户端出现弹道。</summary>
        private void BroadcastSpawn(ProjectileState p)
        {
            float serverTime = BattleSystem.Instance?.ServerTime ?? 0f;
            BattleNetPublisher.Instance.BroadcastAll(
                EOpCode.S2C_Battle_ProjectileSpawnPacket,
                new S2C_Battle_ProjectileSpawnPacket
                {
                    ProjectileInstanceId = p.InstanceId,
                    ProjectileId = p.ProjectileId,
                    SkillId = p.SkillId,
                    CasterId = p.CasterId,
                    Origin = Vector3Data.Of(p.PosX, p.PosY, p.PosZ),
                    Dir = Vector3Data.Of(p.DirX, 0f, p.DirZ),
                    TargetId = p.TargetId,
                    Speed = p.Speed,
                    MaxDistance = p.MaxDistance,
                    Width = p.Width,
                    SpawnServerTime = serverTime
                });
        }

        private ProjectileState CreateState(
            Entity caster,
            SkillConfig def,
            int projId,
            float speed,
            float maxDist,
            float width,
            int hitsLeft,
            SkillHitPayload sourceHit,
            uint hitIndex)
        {
            return new ProjectileState
            {
                InstanceId = _nextInstanceId++,
                ProjectileId = projId,
                SkillId = def.SkillId,
                CasterId = caster.Id,
                Speed = speed,
                Width = width,
                MaxDistance = maxDist,
                Traveled = 0f,
                HitsLeft = hitsLeft,
                HitIndex = hitIndex,
                SourceHit = sourceHit
            };
        }

        /// <summary>读飞行速度、距离、宽度、还能打几次。缺配置用默认值。</summary>
        private static bool TryResolveFlightParams(
            SkillConfig def,
            int projectileId,
            out float speed,
            out float maxDistance,
            out float width,
            out int hitsLeft)
        {
            speed = 16f;
            maxDistance = def.CastRange > 0f ? def.CastRange : GameConstants.DefaultCastRange;
            width = 0.5f;
            hitsLeft = 1;

            if (projectileId == 0)
                return false;

            if (!ProjectileCatalog.TryGet(projectileId, out var cfg) || cfg == null)
                return true;

            if (cfg.Speed > 0.1f) speed = cfg.Speed;
            if (cfg.MaxDistance > 0.1f)
                maxDistance = cfg.MaxDistance;
            else if (def.CastRange > 0.1f)
                maxDistance = def.CastRange;

            if (cfg.Width > 0.01f) width = cfg.Width;

            int maxTargets = cfg.MaxTargets > 0 ? cfg.MaxTargets : 0;
            int pierce = cfg.PierceCount > 0 ? cfg.PierceCount : 0;
            hitsLeft = Math.Max(maxTargets, pierce);
            if (hitsLeft <= 0) hitsLeft = 1;

            return true;
        }
    }
}
