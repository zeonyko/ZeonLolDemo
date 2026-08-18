using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>给技能找目标。不拿摄像机；点屏幕在 ScreenPick。</summary>
    public static class SkillTargeting
    {
        public const float AutoAttackAimStickSnap = 0.32f;

        public static long AttackTargetId { get; private set; }

        public static void ClearAttackTarget() => AttackTargetId = 0;

        public static void SetAttackTarget(long entityId)
        {
            AttackTargetId = entityId > 0 ? entityId : 0;
        }

        public static float GetRange(SkillConfig def)
        {
            if (def == null) return GameConstants.DefaultCastRange;
            float range = def.CastRange > 0.1f ? def.CastRange : SkillRules.GetRange(def.SkillId);
            return range > 0.1f ? range : GameConstants.DefaultCastRange;
        }

        public static float GetCastDistance(float range) => SkillRules.GetCastDistance(range);

        public static Vector3 ClampPoint(Vector3 origin, Vector3 point, float range)
        {
            float px = point.x;
            float pz = point.z;
            SkillRules.ClampPoint2D(origin.x, origin.z, ref px, ref pz, range);
            return new Vector3(px, GameConstants.GroundY, pz);
        }

        public static bool TryResolve(long casterId, int skillId, SkillAim aim, out SkillTarget target)
        {
            target = default;
            var def = SkillCatalog.Get(skillId);
            if (def == null) return false;
            if (!TryGetWorldPos(casterId, out Vector3 origin)) return false;

            Vector3 dir = Flatten(aim.Dir);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

            if (SkillRules.IsSelfTarget(def))
            {
                target.EntityId = casterId;
                target.Dir = dir;
                target.Point = origin;
                target.Range = GetRange(def);
                return true;
            }

            if (def.IsAutoAttack)
                return TryResolveAutoAttack(casterId, def, aim, out target);

            if (def.NeedsLockTarget)
                return TryResolveLock(casterId, def, aim, out target);

            if (SkillRules.NeedsPointTarget(def))
                return TryResolvePoint(origin, def, aim, dir, out target);

            if (aim.EntityId != 0 && TryEvaluate(casterId, aim.EntityId, def.SkillId, out target))
                return true;

            target.Dir = dir;
            target.Range = GetRange(def);
            target.Point = origin + dir * target.Range;
            target.Point.y = GameConstants.GroundY;
            return true;
        }

        public static bool IsAliveTarget(long targetId) => IsAlive(targetId);

        public static bool HasLockInRange(long casterId, int skillId)
        {
            var def = SkillCatalog.Get(skillId);
            if (def == null) return false;
            float range = GetRange(def);
            if (TryEvaluate(casterId, AttackTargetId, skillId, out var locked) && locked.Dist <= range)
                return true;
            return TryPickNearestEnemy(casterId, range, skillId, out _);
        }

        /// <summary>普攻吸附：轻点锁当前/最近；拖动摇杆则往瞄准方向吸最近敌人。</summary>
        public static bool TrySnapAutoAttack(
            long casterId, int skillId, Vector3 aimDir, bool preferAimDir, out long entityId)
        {
            entityId = 0;
            var def = SkillCatalog.Get(skillId);
            if (def == null || !def.IsAutoAttack) return false;
            float snapRange = GetAutoAttackSnapRange(def);

            if (preferAimDir
                && aimDir.sqrMagnitude > 0.0001f
                && TryPickEnemyByAim(casterId, Flatten(aimDir), snapRange, skillId, out entityId))
            {
                SetAttackTarget(entityId);
                return true;
            }

            if (TryEvaluate(casterId, AttackTargetId, skillId, out var cur) && cur.Dist <= snapRange)
            {
                entityId = AttackTargetId;
                return true;
            }

            if (TryPickNearestEnemy(casterId, snapRange, skillId, out entityId))
            {
                SetAttackTarget(entityId);
                return true;
            }

            return false;
        }

        public static bool TryEvaluate(long casterId, long candidateId, int skillId, out SkillTarget target)
        {
            target = default;
            if (candidateId == 0 || !IsAlive(candidateId)) return false;
            if (BattleCache.Instance == null) return false;
            if (!BattleCache.Instance.TryGet(casterId, out var self) || self == null) return false;
            if (!BattleCache.Instance.TryGet(candidateId, out var tgt) || tgt == null) return false;
            if (!AreEnemies(self.TeamId, tgt.TeamId)) return false;

            var def = SkillCatalog.Get(skillId);
            if (!UnitCatalog.CanReceiveSkillDamage(tgt.EntityType, def))
                return false;
            if (!TryGetWorldPos(casterId, out Vector3 selfPos)) return false;
            if (!TryGetWorldPos(candidateId, out Vector3 tgtPos)) return false;

            float range = GetRange(def);
            Vector3 toward = Flatten(tgtPos - selfPos);
            float dist = Vector3.Distance(selfPos, tgtPos);
            if (toward.sqrMagnitude < 0.0001f) toward = Vector3.forward;
            else toward.Normalize();

            target.EntityId = candidateId;
            target.Dir = toward;
            target.Point = tgtPos;
            target.Dist = dist;
            target.Range = range;
            target.InRange = dist <= GetCastDistance(range);
            return true;
        }

        public static bool TryGetWorldPos(long id, out Vector3 pos)
        {
            pos = Vector3.zero;
            var tf = EntityManager.Instance?.GetEntity(id)?.GetComponent<TransformComponent>();
            if (tf != null)
            {
                pos = tf.Position;
                return true;
            }

            if (BattleCache.Instance != null && BattleCache.Instance.TryGet(id, out var data) && data != null)
            {
                pos = new Vector3(data.PosX, data.PosY, data.PosZ);
                return true;
            }

            return false;
        }

        public static bool AreEnemies(int aTeam, int bTeam)
        {
            if (aTeam == ETeamId.None || bTeam == ETeamId.None) return false;
            return aTeam != bTeam;
        }

        public static bool IsSelectable(int entityType)
        {
            return entityType == EEntityType.Player
                   || entityType == EEntityType.Monster
                   || entityType == EEntityType.Boss
                   || entityType == EEntityType.MinionMelee
                   || entityType == EEntityType.MinionRanged
                   || entityType == EEntityType.MinionSiege
                   || entityType == EEntityType.MinionSuper
                   || entityType == EEntityType.Tower
                   || entityType == EEntityType.Crystal
                   || entityType == EEntityType.Barracks;
        }

        static bool TryResolveAutoAttack(
            long casterId, SkillConfig def, SkillAim aim, out SkillTarget target)
        {
            if (aim.EntityId != 0 && TryEvaluate(casterId, aim.EntityId, def.SkillId, out target))
            {
                SetAttackTarget(aim.EntityId);
                return true;
            }

            bool preferAim = aim.StickMag >= AutoAttackAimStickSnap;
            if (TrySnapAutoAttack(casterId, def.SkillId, aim.Dir, preferAim, out long snapped)
                && TryEvaluate(casterId, snapped, def.SkillId, out target))
                return true;

            target = default;
            return false;
        }

        static float GetAutoAttackSnapRange(SkillConfig def)
        {
            float range = GetRange(def);
            return Mathf.Max(range * 2.8f, range + 3.5f);
        }

        static bool TryResolveLock(
            long casterId, SkillConfig def, SkillAim aim, out SkillTarget target)
        {
            target = default;
            float range = GetRange(def);

            if (aim.EntityId != 0
                && TryEvaluate(casterId, aim.EntityId, def.SkillId, out target)
                && target.Dist <= range)
            {
                SetAttackTarget(aim.EntityId);
                return true;
            }

            if (TryPickNearestEnemy(casterId, range, def.SkillId, out long nearestId)
                && TryEvaluate(casterId, nearestId, def.SkillId, out target))
            {
                SetAttackTarget(nearestId);
                return true;
            }

            if (TryEvaluate(casterId, AttackTargetId, def.SkillId, out target) && target.Dist <= range)
                return true;

            target = default;
            return false;
        }

        static bool TryResolvePoint(
            Vector3 origin, SkillConfig def, SkillAim aim, Vector3 dir, out SkillTarget target)
        {
            float range = GetRange(def);
            Vector3 point;
            if (aim.HasPoint)
            {
                point = aim.Point;
                if (def.CastApproach == CastApproachPolicy.ClampInstant)
                    point = ClampPoint(origin, point, range);
            }
            else
            {
                float mag = aim.StickMag < 0.12f ? 1f : Mathf.Clamp01(aim.StickMag);
                point = origin + dir * (range * mag);
                point.y = GameConstants.GroundY;
            }

            Vector3 to = Flatten(point - origin);
            float dist = to.magnitude;
            target = new SkillTarget
            {
                Dir = dist > 0.0001f ? to / dist : dir,
                Point = point,
                HasPoint = true,
                Dist = dist,
                Range = range,
                InRange = dist <= GetCastDistance(range)
            };
            return true;
        }

        static bool TryPickEnemyByAim(
            long casterId, Vector3 dir, float maxRange, int skillId, out long entityId)
        {
            entityId = 0;
            if (dir.sqrMagnitude < 0.0001f) return false;
            dir.Normalize();

            var all = BattleCache.Instance?.GetAll();
            if (all == null) return false;
            if (!BattleCache.Instance.TryGet(casterId, out var self) || self == null)
                return false;
            if (!TryGetWorldPos(casterId, out Vector3 selfPos)) return false;
            var def = SkillCatalog.Get(skillId);

            float best = float.MinValue;
            long bestId = 0;
            foreach (var kv in all)
            {
                var data = kv.Value;
                if (data == null || data.Id == 0 || data.Id == casterId) continue;
                if (data.Hp <= 0f) continue;
                if (!AreEnemies(self.TeamId, data.TeamId)) continue;
                if (!IsSelectable(data.EntityType)) continue;
                if (!UnitCatalog.CanReceiveSkillDamage(data.EntityType, def)) continue;
                if (!TryGetWorldPos(data.Id, out Vector3 tgtPos)) continue;

                Vector3 toward = Flatten(tgtPos - selfPos);
                float dist = toward.magnitude;
                if (dist > maxRange || dist < 0.001f) continue;
                toward /= dist;
                float dot = Vector3.Dot(dir, toward);
                if (dot < 0.2f) continue;

                float score = dot * 1.6f - dist / maxRange;
                if (score <= best) continue;
                best = score;
                bestId = data.Id;
            }

            if (bestId == 0) return false;
            entityId = bestId;
            return true;
        }

        static bool TryPickNearestEnemy(long localPlayerId, float maxRange, int skillId, out long entityId)
        {
            entityId = 0;
            var all = BattleCache.Instance?.GetAll();
            if (all == null) return false;
            if (!BattleCache.Instance.TryGet(localPlayerId, out var self) || self == null)
                return false;
            if (!TryGetWorldPos(localPlayerId, out Vector3 selfPos)) return false;
            var def = SkillCatalog.Get(skillId);

            float best = maxRange;
            long bestId = 0;
            foreach (var kv in all)
            {
                var data = kv.Value;
                if (data == null || data.Id == 0 || data.Id == localPlayerId) continue;
                if (data.Hp <= 0f) continue;
                if (!AreEnemies(self.TeamId, data.TeamId)) continue;
                if (!IsSelectable(data.EntityType)) continue;
                if (!UnitCatalog.CanReceiveSkillDamage(data.EntityType, def)) continue;
                if (!TryGetWorldPos(data.Id, out Vector3 tgtPos)) continue;

                float d = Vector3.Distance(selfPos, tgtPos);
                if (d > best) continue;
                best = d;
                bestId = data.Id;
            }

            if (bestId == 0) return false;
            entityId = bestId;
            return true;
        }

        static bool IsAlive(long targetId)
        {
            if (targetId == 0 || BattleCache.Instance == null) return false;
            return BattleCache.Instance.TryGet(targetId, out var data) && data != null && data.Hp > 0f;
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
