using System;
using Shared;

namespace Server.Battle
{
    public enum EMinionAiState
    {
        PushLane = 0,
        Engage = 1,
    }

    /// <summary>小兵：沿路推进，遇敌站住打。进射程就钉住，避免大家跟着晃。</summary>
    public static class MinionAi
    {
        public static void Tick(Entity minion, Scene scene, float dt, float serverTime)
        {
            if (minion == null || !minion.IsMinion || minion.Hp <= 0f || scene == null)
                return;

            if (minion.State.HasState(EntityStateTag.Root | EntityStateTag.Stun | EntityStateTag.Airborne))
            {
                ApplySoftSeparation(minion, scene, engaging: false);
                SkillResultService.TryBroadcastUnitSnapshot(minion, dt);
                return;
            }

            var target = ResolveTarget(minion, scene, serverTime);
            bool engaging = target != null;
            if (engaging)
            {
                minion.AiState = (EMonsterAiState)(int)EMinionAiState.Engage;
                float range = SkillRules.GetRange(minion.AttackSkillId);
                float distTarget = HorizDist(minion.PosX, minion.PosZ, target.PosX, target.PosZ);

                // 进射程就站住；稍微超出还站着，避免互追乱晃
                float unplantRange = range * 1.2f;
                if (distTarget <= range)
                    minion.EngagePlanted = true;
                else if (distTarget > unplantRange)
                    minion.EngagePlanted = false;

                if (minion.EngagePlanted)
                {
                    // 站住互殴：完全不移动
                    if (distTarget <= range)
                        SkillResultService.TryUnitAttack(minion, target, minion.AttackSkillId, serverTime);
                }
                else
                {
                    EnsureEngageSlot(minion, target, scene, range);
                    GetSlotWorld(minion, target, range, out float slotX, out float slotZ);
                    MoveToSlotWithRepulsion(minion, scene, slotX, slotZ, dt);
                }
            }
            else
            {
                if (minion.AiState == (EMonsterAiState)(int)EMinionAiState.Engage)
                    SnapToNearestLaneWaypoint(minion);

                ClearEngageSlot(minion);
                minion.AiState = (EMonsterAiState)(int)EMinionAiState.PushLane;
                minion.LaneAcross = 0f;
                PushAlongLane(minion, dt);
                ApplySoftSeparation(minion, scene, engaging: false);
            }

            SkillResultService.TryBroadcastUnitSnapshot(minion, dt);
        }

        /// <summary>有人挨打后，周围小兵来帮忙。</summary>
        public static void OnUnitDamaged(Entity attacker, Entity victim, Scene scene, float serverTime)
        {
            if (attacker == null || victim == null || scene == null) return;
            if (!Entity.AreEnemies(attacker, victim) || victim.Hp <= 0f) return;

            // 英雄打英雄：小兵转打那个英雄
            if (attacker.IsPlayer && victim.IsPlayer)
            {
                ForceAggroNearbyMinions(scene, victim.TeamId, attacker, victim.PosX, victim.PosZ, serverTime);
                return;
            }

            // 英雄打小兵：周围小兵来帮忙
            if (victim.IsMinion && attacker.IsPlayer)
            {
                ForceAggroMinion(victim, attacker, serverTime, allowBacklineRedirect: false);
                ForceAggroNearbyMinions(scene, victim.TeamId, attacker, victim.PosX, victim.PosZ, serverTime);
                return;
            }

            // 小兵打小兵：别全体绕去打后排，优先打前排
            if (victim.IsMinion && attacker.IsMinion)
            {
                PreferFrontlineAggro(victim, scene);
                return;
            }
        }

        private static void ForceAggroNearbyMinions(
            Scene scene, int allyTeamId, Entity attacker, float ox, float oz, float serverTime)
        {
            float helpSq = AramMap.MinionCallForHelpRange * AramMap.MinionCallForHelpRange;
            float leash = AramMap.MinionAggroRange * AramMap.MinionLeashMul;
            foreach (var m in scene.GetAllEntities())
            {
                if (m == null || !m.IsMinion || m.Hp <= 0f || m.TeamId != allyTeamId)
                    continue;
                float dx = m.PosX - ox;
                float dz = m.PosZ - oz;
                if (dx * dx + dz * dz > helpSq) continue;
                if (!IsValidTarget(m, attacker, leash)) continue;
                ForceAggroMinion(m, attacker, serverTime, allowBacklineRedirect: true);
            }
        }

        private static void ForceAggroMinion(
            Entity minion, Entity attacker, float serverTime, bool allowBacklineRedirect)
        {
            if (minion == null || attacker == null) return;

            Entity aggro = attacker;
            // 近战被远程打中：范围内有敌方近战就改打前排，别全体绕后
            if (allowBacklineRedirect
                && IsMeleeLineMinion(minion)
                && IsBacklineMinion(attacker)
                && minion.Scene != null)
            {
                var front = FindNearestEnemyMelee(minion, minion.Scene, AramMap.MinionAggroRange);
                if (front != null)
                    aggro = front;
            }

            if (minion.AggroTargetId != aggro.Id)
                ClearEngageSlot(minion);
            minion.AggroTargetId = aggro.Id;
            // 锁前排不必强制盯死；锁英雄才强制一段时间
            minion.ForcedAggroUntil = aggro.IsPlayer
                ? serverTime + AramMap.MinionForcedAggroDuration
                : -999f;
        }

        /// <summary>挨打的近战优先把仇恨放到最近的敌方近战。</summary>
        private static void PreferFrontlineAggro(Entity victim, Scene scene)
        {
            if (victim == null || scene == null || !IsMeleeLineMinion(victim)) return;
            var front = FindNearestEnemyMelee(victim, scene, AramMap.MinionAggroRange);
            if (front == null) return;
            if (victim.AggroTargetId == front.Id) return;
            // 已经强制锁着英雄就别抢
            if (victim.ForcedAggroUntil > 0f)
            {
                var cur = scene.GetEntity(victim.AggroTargetId);
                if (cur != null && cur.IsPlayer) return;
            }

            ClearEngageSlot(victim);
            victim.AggroTargetId = front.Id;
            victim.ForcedAggroUntil = -999f;
            victim.EngagePlanted = false;
        }

        private static Entity FindNearestEnemyMelee(Entity self, Scene scene, float range)
        {
            Entity best = null;
            float bestSq = float.MaxValue;
            float rangeSq = range * range;
            foreach (var e in scene.GetAllEntities())
            {
                if (e == null || e.Hp <= 0f || !e.IsMinion) continue;
                if (!Entity.AreEnemies(self, e)) continue;
                if (e.EntityType != EEntityType.MinionMelee && !e.IsSuperMinion) continue;

                float dx = e.PosX - self.PosX;
                float dz = e.PosZ - self.PosZ;
                float sq = dx * dx + dz * dz;
                if (sq > rangeSq || sq >= bestSq) continue;
                bestSq = sq;
                best = e;
            }

            return best;
        }

        private static Entity ResolveTarget(Entity self, Scene scene, float now)
        {
            float acquire = AramMap.MinionAggroRange;
            float leash = acquire * AramMap.MinionLeashMul;

            Entity locked = null;
            if (self.AggroTargetId != 0)
            {
                locked = scene.GetEntity(self.AggroTargetId);
                if (!IsValidTarget(self, locked, leash))
                {
                    locked = null;
                    self.AggroTargetId = 0;
                    self.ForcedAggroUntil = -999f;
                    ClearEngageSlot(self);
                }
            }

            // 近战若锁着远程，每帧试试改打前排
            if (IsMeleeLineMinion(self) && locked != null && IsBacklineMinion(locked))
            {
                var front = FindNearestEnemyMelee(self, scene, acquire);
                if (front != null)
                {
                    ClearEngageSlot(self);
                    self.AggroTargetId = front.Id;
                    self.ForcedAggroUntil = -999f;
                    self.EngagePlanted = false;
                    return front;
                }
            }

            // 强制仇恨：英雄拉兵才钉死；仍允许上面改打前排
            if (locked != null && now <= self.ForcedAggroUntil && IsValidTarget(self, locked, leash))
                return locked;

            var best = FindBestTarget(self, scene, acquire, now);
            if (locked != null && IsValidTarget(self, locked, acquire))
            {
                if (best == null)
                    return locked;

                int lockScore = ScoreTarget(self, locked, scene, now);
                int bestScore = ScoreTarget(self, best, scene, now);
                if (lockScore < bestScore)
                    return locked;
                if (lockScore == bestScore && !ShouldSwitchFrontline(self, locked, best))
                    return locked;
            }

            if (best != null)
            {
                if (self.AggroTargetId != best.Id)
                    ClearEngageSlot(self);
                self.AggroTargetId = best.Id;
                return best;
            }

            if (locked != null && IsValidTarget(self, locked, leash))
                return locked;

            self.AggroTargetId = 0;
            self.ForcedAggroUntil = -999f;
            ClearEngageSlot(self);
            return null;
        }

        private static Entity FindBestTarget(Entity self, Scene scene, float range, float now)
        {
            Entity best = null;
            int bestScore = int.MaxValue;
            float bestEffSq = float.MaxValue;
            int bestType = int.MaxValue;
            float rangeSq = range * range;
            bool super = self.IsSuperMinion;

            foreach (var e in scene.GetAllEntities())
            {
                if (!Entity.AreEnemies(self, e) || e.Hp <= 0f) continue;
                if (!e.IsMinion && !e.IsStructure && !e.IsPlayer) continue;

                float dx = e.PosX - self.PosX;
                float dz = e.PosZ - self.PosZ;
                float sq = dx * dx + dz * dz;
                if (sq > rangeSq) continue;

                int score = super ? ScoreSuperTarget(e) : ScoreTarget(self, e, scene, now);
                float effSq = EffectiveTargetSq(self, e, sq);
                int typeOrder = MinionFrontlineOrder(e);

                bool better = score < bestScore
                              || (score == bestScore && effSq < bestEffSq)
                              || (score == bestScore && MathF.Abs(effSq - bestEffSq) < 0.01f && typeOrder < bestType);
                if (better)
                {
                    bestScore = score;
                    bestEffSq = effSq;
                    bestType = typeOrder;
                    best = e;
                }
            }

            return best;
        }

        /// <summary>近战自己看远程时当更远，避免绕后排；远程仍按真实距离选。</summary>
        private static float EffectiveTargetSq(Entity self, Entity candidate, float sq)
        {
            if (candidate == null || !candidate.IsMinion) return sq;
            if (!IsMeleeLineMinion(self)) return sq;

            if (candidate.EntityType == EEntityType.MinionRanged)
            {
                float b = AramMap.MinionBacklineTargetBias;
                return sq + b * b;
            }

            if (candidate.IsSiegeMinion)
                return sq + 1.2f * 1.2f;

            return sq;
        }

        private static bool IsMeleeLineMinion(Entity e) =>
            e != null && (e.EntityType == EEntityType.MinionMelee || e.IsSuperMinion);

        private static bool IsBacklineMinion(Entity e) =>
            e != null && e.EntityType == EEntityType.MinionRanged;

        /// <summary>越小越偏前排：近战、炮车、远程。</summary>
        private static int MinionFrontlineOrder(Entity e)
        {
            if (e == null || !e.IsMinion) return 10;
            if (e.EntityType == EEntityType.MinionMelee || e.IsSuperMinion) return 0;
            if (e.IsSiegeMinion) return 1;
            if (e.EntityType == EEntityType.MinionRanged) return 2;
            return 5;
        }

        /// <summary>已经锁着远程时，前排近战能打到就改打前排。</summary>
        private static bool ShouldSwitchFrontline(Entity self, Entity locked, Entity best)
        {
            if (!IsMeleeLineMinion(self)) return false;
            if (locked == null || best == null) return false;
            if (!IsBacklineMinion(locked)) return false;
            if (!locked.IsMinion || !best.IsMinion) return false;
            if (MinionFrontlineOrder(best) >= MinionFrontlineOrder(locked)) return false;

            float lockSq = DistSq(self, locked);
            float bestSq = DistSq(self, best);
            return EffectiveTargetSq(self, best, bestSq) <= EffectiveTargetSq(self, locked, lockSq) + 0.25f;
        }

        private static float DistSq(Entity a, Entity b)
        {
            float dx = a.PosX - b.PosX;
            float dz = a.PosZ - b.PosZ;
            return dx * dx + dz * dz;
        }

        private static int ScoreTarget(Entity self, Entity candidate, Scene scene, float now)
        {
            if (candidate == null) return 100;
            if (now <= self.ForcedAggroUntil && candidate.Id == self.AggroTargetId)
                return -1;

            bool hitsAllyHero = IsAttackingAllyOfType(candidate, self.TeamId, scene, now, wantHero: true, wantMinion: false);
            bool hitsAllyMinion = IsAttackingAllyOfType(candidate, self.TeamId, scene, now, wantHero: false, wantMinion: true);

            if (candidate.IsPlayer && hitsAllyHero) return 0;
            if (candidate.IsPlayer && hitsAllyMinion) return 1;
            // 敌方近战正在打友军：优先打；远程先手压不过前排
            if (candidate.IsMinion && hitsAllyMinion && !IsBacklineMinion(candidate))
                return 2 + MinionFrontlineOrder(candidate);
            if (candidate.IsAttackingStructure && hitsAllyMinion) return 5;
            // 普通小兵：近战优先于炮车、远程
            if (candidate.IsMinion)
                return 6 + MinionFrontlineOrder(candidate);
            if (candidate.IsPlayer) return 9;
            if (candidate.IsStructure) return 10;
            return 50;
        }

        private static int ScoreSuperTarget(Entity e)
        {
            if (e.IsTower) return 0;
            if (e.IsBarracks) return 1;
            if (e.IsCrystal) return 2;
            if (e.IsMinion) return 3;
            if (e.IsPlayer) return 4;
            return 50;
        }

        private static bool IsAttackingAllyOfType(
            Entity attacker, int allyTeamId, Scene scene, float now, bool wantHero, bool wantMinion)
        {
            if (attacker == null || scene == null) return false;
            if (now - attacker.LastDealtDamageAt > AramMap.MinionCombatMemory) return false;

            var victim = scene.GetEntity(attacker.LastDealtDamageTargetId);
            if (victim == null || victim.Hp <= 0f || victim.TeamId != allyTeamId) return false;
            if (wantHero && victim.IsPlayer) return true;
            if (wantMinion && victim.IsMinion) return true;
            return false;
        }

        private static bool IsValidTarget(Entity self, Entity target, float maxRange)
        {
            if (target == null || target.Hp <= 0f) return false;
            if (!Entity.AreEnemies(self, target)) return false;
            if (!target.IsMinion && !target.IsStructure && !target.IsPlayer) return false;

            float dx = target.PosX - self.PosX;
            float dz = target.PosZ - self.PosZ;
            return dx * dx + dz * dz <= maxRange * maxRange;
        }

        private static void ClearEngageSlot(Entity m)
        {
            if (m == null) return;
            m.EngageSlotIndex = int.MinValue;
            m.EngageSlotTargetId = 0;
            m.EngagePlanted = false;
        }

        /// <summary>占一个站位：换目标才重占；挑离自己最近的空位。</summary>
        private static void EnsureEngageSlot(Entity self, Entity target, Scene scene, float attackRange)
        {
            if (self.EngageSlotTargetId == target.Id && self.EngageSlotIndex != int.MinValue)
                return;

            self.EngageSlotTargetId = target.Id;
            self.EngageSlotIndex = ClaimNearestFreeSlot(self, target, scene, attackRange);
        }

        private static int ClaimNearestFreeSlot(Entity self, Entity target, Scene scene, float attackRange)
        {
            Span<bool> taken = stackalloc bool[AramMap.MinionEngageSlotMax * 2 + 1];
            int center = AramMap.MinionEngageSlotMax;

            foreach (var e in scene.GetAllEntities())
            {
                if (e == null || e.Id == self.Id || !e.IsMinion || e.Hp <= 0f) continue;
                if (e.TeamId != self.TeamId) continue;
                if (e.EngageSlotTargetId != target.Id) continue;
                if (e.EngageSlotIndex == int.MinValue) continue;

                int idx = e.EngageSlotIndex + center;
                if ((uint)idx < (uint)taken.Length)
                    taken[idx] = true;
            }

            int bestSlot = 0;
            float bestSq = float.MaxValue;
            bool any = false;

            for (int slot = -AramMap.MinionEngageSlotMax; slot <= AramMap.MinionEngageSlotMax; slot++)
            {
                int idx = slot + center;
                if (taken[idx]) continue;

                ComputeSlotWorld(self, target, attackRange, slot, out float sx, out float sz);
                float dx = sx - self.PosX;
                float dz = sz - self.PosZ;
                float sq = dx * dx + dz * dz;
                if (!any || sq < bestSq)
                {
                    any = true;
                    bestSq = sq;
                    bestSlot = slot;
                }
            }

            return any ? bestSlot : 0;
        }

        private static void GetSlotWorld(Entity self, Entity target, float attackRange, out float x, out float z)
        {
            int slot = self.EngageSlotIndex == int.MinValue ? 0 : self.EngageSlotIndex;
            ComputeSlotWorld(self, target, attackRange, slot, out x, out z);
            self.LaneAcross = slot * 0.5f;
        }

        private static void ComputeSlotWorld(
            Entity self, Entity target, float attackRange, int slot, out float x, out float z)
        {
            float standOff = attackRange * 0.88f;
            if (standOff < 0.75f) standOff = 0.75f;
            if (self.EntityType == EEntityType.MinionRanged)
                standOff = attackRange * 0.82f;
            else if (self.IsSiegeMinion)
                standOff = attackRange * 0.85f;

            // 站在目标正面半圈，朝自己推进的方向
            float pushDir = self.TeamId == ETeamId.Blue ? 1f : -1f;
            const float invSqrt2 = 0.70710678f;
            float fwdX = -pushDir * invSqrt2;
            float fwdZ = -pushDir * invSqrt2;

            float angle = slot * AramMap.MinionEngageRingAngleDeg * (MathF.PI / 180f);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            float rx = fwdX * cos - fwdZ * sin;
            float rz = fwdX * sin + fwdZ * cos;
            float mag = MathF.Sqrt(rx * rx + rz * rz);
            if (mag < 1e-5f)
            {
                x = target.PosX + fwdX * standOff;
                z = target.PosZ + fwdZ * standOff;
                return;
            }

            x = target.PosX + rx / mag * standOff;
            z = target.PosZ + rz / mag * standOff;
        }

        /// <summary>走向站位时被友军轻轻推开，避免挤成一团。只推友军。</summary>
        private static void MoveToSlotWithRepulsion(Entity self, Scene scene, float slotX, float slotZ, float dt)
        {
            float speed = self.IsSuperMinion
                ? AramMap.MinionMoveSpeed * 0.85f
                : AramMap.MinionMoveSpeed;
            speed *= self.State.MoveSpeedMultiplier;

            float dx = slotX - self.PosX;
            float dz = slotZ - self.PosZ;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            float vx = 0f;
            float vz = 0f;
            if (dist > 0.05f)
            {
                vx = dx / dist * speed;
                vz = dz / dist * speed;
            }

            float repX = 0f;
            float repZ = 0f;
            float detect = AramMap.MinionEngageRepulseRadius;
            float detectSq = detect * detect;

            foreach (var other in scene.GetAllEntities())
            {
                if (other == null || other.Id == self.Id || !other.IsMinion || other.Hp <= 0f)
                    continue;
                if (other.TeamId != self.TeamId) continue;

                float ox = self.PosX - other.PosX;
                float oz = self.PosZ - other.PosZ;
                float sq = ox * ox + oz * oz;
                if (sq >= detectSq || sq < 1e-8f) continue;

                float d = MathF.Sqrt(sq);
                // 越近推得越开
                float mul = (detect - d) / d;
                repX += ox / d * mul;
                repZ += oz / d * mul;
            }

            float w = AramMap.MinionEngageRepulseWeight;
            vx += repX * w;
            vz += repZ * w;

            float vMag = MathF.Sqrt(vx * vx + vz * vz);
            if (vMag < 1e-5f) return;
            if (vMag > speed)
            {
                float s = speed / vMag;
                vx *= s;
                vz *= s;
                vMag = speed;
            }

            float step = vMag * dt;
            float dirX = vx / vMag;
            float dirZ = vz / vMag;
            float actual = WorldCollision.ClampMoveDistance(self.PosX, self.PosZ, dirX, dirZ, step);
            self.PosX += dirX * actual;
            self.PosZ += dirZ * actual;
            self.PosY = GameConstants.GroundY;
            self.MoveState.IsGrounded = true;
            self.MoveState.VelY = 0f;
        }

        private static void SnapToNearestLaneWaypoint(Entity minion)
        {
            minion.LanePathIndex = AramMap.ResolveLanePathIndex(minion.TeamId, minion.PosX, minion.PosZ);
            minion.LaneAcross = 0f;
        }

        private static void PushAlongLane(Entity minion, float dt)
        {
            minion.LanePathIndex = AramMap.ResolveLanePathIndex(minion.TeamId, minion.PosX, minion.PosZ);
            if (!AramMap.TryGetLanePathWorld(
                    minion.TeamId, minion.LanePathIndex, 0f, out float goalX, out float goalZ))
            {
                AramMap.GetCrystalPos(ETeamId.EnemyOf(minion.TeamId), out goalX, out goalZ);
            }

            MoveToward(minion, goalX, goalZ, dt);

            AramMap.WorldToLane(minion.PosX, minion.PosZ, out float along, out _);
            float wpAlong = AramMap.GetLanePathAlong(minion.TeamId, minion.LanePathIndex);
            float dir = minion.TeamId == ETeamId.Blue ? 1f : -1f;
            if (dir * (along - wpAlong) >= -0.35f
                && minion.LanePathIndex < AramMap.LanePathPointCount - 1)
            {
                minion.LanePathIndex++;
            }
        }

        /// <summary>推线时友军轻轻分开，别叠在一起。</summary>
        private static void ApplySoftSeparation(Entity self, Scene scene, bool engaging)
        {
            float radius = AramMap.MinionBodyRadius;
            if (self.IsSuperMinion) radius *= 1.25f;
            else if (self.IsSiegeMinion) radius *= 1.15f;

            float pushX = 0f;
            float pushZ = 0f;
            int hits = 0;

            foreach (var other in scene.GetAllEntities())
            {
                if (other == null || other.Id == self.Id || !other.IsMinion || other.Hp <= 0f)
                    continue;
                if (other.TeamId != self.TeamId) continue;

                float otherR = AramMap.MinionBodyRadius;
                if (other.IsSuperMinion) otherR *= 1.25f;
                else if (other.IsSiegeMinion) otherR *= 1.15f;

                float minDist = (radius + otherR) * 0.95f;
                float dx = self.PosX - other.PosX;
                float dz = self.PosZ - other.PosZ;
                float sq = dx * dx + dz * dz;
                if (sq < 1e-6f)
                {
                    float side = ((self.Id + other.Id) & 1) == 0 ? 1f : -1f;
                    pushX += side * 0.08f;
                    pushZ += (1f - side) * 0.06f;
                    hits++;
                    continue;
                }

                if (sq >= minDist * minDist) continue;
                float dist = MathF.Sqrt(sq);
                float overlap = minDist - dist;
                pushX += dx / dist * overlap;
                pushZ += dz / dist * overlap;
                hits++;
            }

            if (hits == 0) return;

            float k = AramMap.MinionSeparationStrength;
            ApplyPush(self, pushX * k, pushZ * k);
        }

        private static void ApplyPush(Entity self, float pushX, float pushZ)
        {
            float mag = MathF.Sqrt(pushX * pushX + pushZ * pushZ);
            if (mag < 1e-5f) return;
            float dirX = pushX / mag;
            float dirZ = pushZ / mag;
            float actual = WorldCollision.ClampMoveDistance(self.PosX, self.PosZ, dirX, dirZ, mag);
            self.PosX += dirX * actual;
            self.PosZ += dirZ * actual;
            self.PosY = GameConstants.GroundY;
            self.MoveState.IsGrounded = true;
            self.MoveState.VelY = 0f;
        }

        private static void MoveToward(Entity unit, float tx, float tz, float dt)
        {
            float dx = tx - unit.PosX;
            float dz = tz - unit.PosZ;
            float mag = MathF.Sqrt(dx * dx + dz * dz);
            if (mag < 0.08f) return;

            float speed = unit.IsSuperMinion
                ? AramMap.MinionMoveSpeed * 0.85f
                : AramMap.MinionMoveSpeed;
            float step = speed * unit.State.MoveSpeedMultiplier * dt;
            float dirX = dx / mag;
            float dirZ = dz / mag;
            float actual = WorldCollision.ClampMoveDistance(unit.PosX, unit.PosZ, dirX, dirZ, Math.Min(step, mag));
            unit.PosX += dirX * actual;
            unit.PosZ += dirZ * actual;
            unit.PosY = GameConstants.GroundY;
            unit.MoveState.IsGrounded = true;
            unit.MoveState.VelY = 0f;
        }

        private static float HorizDist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
