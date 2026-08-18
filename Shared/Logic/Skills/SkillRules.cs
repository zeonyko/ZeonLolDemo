using System;

namespace Shared
{
    /// <summary>查技能冷却、射程、要不要瞄准、圈外怎么走近。两端共用，别各写一份。</summary>
    public static class SkillRules
    {
        #region --- 冷却 ---

        public static bool IsKnownSkill(int skillId) => SkillCatalog.IsKnown(skillId);

        public static float GetCooldown(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) ? data.Cooldown : 0f;
        }

        /// <summary>冷却好了没。双方要用同一套时钟。</summary>
        public static bool IsCooldownReady(float nowSeconds, float lastCastSeconds, int skillId)
        {
            return nowSeconds - lastCastSeconds >= GetCooldown(skillId);
        }

        public static float GetCooldownRemaining(float nowSeconds, float lastCastSeconds, int skillId)
        {
            float cd = GetCooldown(skillId);
            if (cd <= 0f) return 0f;
            float left = cd - (nowSeconds - lastCastSeconds);
            return left > 0f ? left : 0f;
        }

        #endregion

        #region --- 伤害 / 射程 ---

        public static float GetAttackDamage(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) ? data.Damage : 0f;
        }

        public static float GetRange(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) ? data.CastRange : 0f;
        }

        /// <summary>走近再放时停在哪。比满射程略短，Admit 仍按满射程。</summary>
        public static float GetCastDistance(float range)
        {
            float d = range * GameConstants.CastApproachMul;
            return d < GameConstants.CastApproachMin ? GameConstants.CastApproachMin : d;
        }

        public static void ClampPoint2D(float ox, float oz, ref float px, ref float pz, float range)
        {
            float dx = px - ox;
            float dz = pz - oz;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist <= range || dist <= 0.0001f) return;
            float s = range / dist;
            px = ox + dx * s;
            pz = oz + dz * s;
        }

        public static bool IsInSkillRange(
            float casterX, float casterY, float casterZ,
            float targetX, float targetY, float targetZ,
            int skillId)
        {
            float range = GetRange(skillId);
            if (range <= 0f) return false;
            return MovementSimulator.Distance3D(
                casterX, casterY, casterZ,
                targetX, targetY, targetZ) <= range;
        }

        public static bool IsInRange(
            float casterX, float casterY, float casterZ,
            float targetX, float targetY, float targetZ,
            float range)
        {
            return MovementSimulator.Distance3D(
                casterX, casterY, casterZ,
                targetX, targetY, targetZ) <= range;
        }

        #endregion

        #region --- 方向 ---

        public static void NormalizeHorizontal(ref float dirX, ref float dirZ)
        {
            float magSq = dirX * dirX + dirZ * dirZ;
            if (magSq < 0.0001f)
            {
                dirX = 0f;
                dirZ = 1f;
                return;
            }

            float inv = 1f / (float)Math.Sqrt(magSq);
            dirX *= inv;
            dirZ *= inv;
        }

        #endregion

        #region --- 瞄准 / 目标 ---

        public static bool IsCancelSkill(SkillConfig data)
        {
            if (data == null) return false;
            return data.ResolveMode == ESkillResolveMode.Blink
                   || data.ResolveMode == ESkillResolveMode.Dash
                   || data.ResolveMode == ESkillResolveMode.Jump;
        }

        public static bool IsCancelSkill(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) && IsCancelSkill(data);
        }

        /// <summary>对自己、锁定单体、普攻不需要方向技摇杆。普攻按住后吸附锁定，不画取消区。</summary>
        public static bool NeedsAimDirection(SkillConfig data)
        {
            if (data == null) return false;
            if (data.IsAutoAttack) return false;
            if (data.TargetMode == TargetType.Self || data.TargetMode == TargetType.SingleTarget)
                return false;
            if (data.TargetMode == TargetType.Direction || data.TargetMode == TargetType.Point)
                return true;
            return data.ResolveMode == ESkillResolveMode.SkillshotLine
                   || data.ResolveMode == ESkillResolveMode.Blink
                   || data.ResolveMode == ESkillResolveMode.Dash
                   || data.ResolveMode == ESkillResolveMode.MeleeShape;
        }

        public static bool NeedsAimDirection(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) && NeedsAimDirection(data);
        }

        public static bool NeedsPointTarget(SkillConfig data) =>
            data != null && data.TargetMode == TargetType.Point;

        public static bool NeedsPointTarget(int skillId) =>
            SkillCatalog.TryGet(skillId, out var data) && NeedsPointTarget(data);

        public static bool IsSelfTarget(SkillConfig data) =>
            data != null && data.TargetMode == TargetType.Self;

        public static bool IsSelfTarget(int skillId) =>
            SkillCatalog.TryGet(skillId, out var data) && IsSelfTarget(data);

        public static bool NeedsLockTarget(SkillConfig data) =>
            data != null && data.NeedsLockTarget;

        public static bool NeedsLockTarget(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data) && NeedsLockTarget(data);
        }

        public static bool RequiresAttackGate(SkillConfig data) =>
            data != null && data.RequiresAttackGate;

        public static bool RequiresAttackGate(int skillId) =>
            SkillCatalog.TryGet(skillId, out var data) && RequiresAttackGate(data);

        #endregion

        #region --- 出手时机 ---

        public static float GetCastWindup(SkillConfig data) =>
            data != null ? data.HitDelay : 0f;

        public static float GetCastWindup(int skillId) =>
            SkillCatalog.TryGet(skillId, out var data) ? GetCastWindup(data) : 0f;

        /// <summary>动画前摇用。配表有 HitDelay 就用，没有用默认。别拿去算伤害时机。</summary>
        public static float GetEffectiveHitDelay(SkillConfig data)
        {
            if (data != null && data.HitDelay > 0.001f)
                return data.HitDelay;
            return GameConstants.DefaultHitDelay;
        }

        public static float GetEffectiveHitDelay(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data)
                ? GetEffectiveHitDelay(data)
                : GameConstants.DefaultHitDelay;
        }

        #endregion

        #region --- 圈外怎么走近 ---

        public static CastApproachPolicy GetCastApproach(SkillConfig data)
        {
            if (data == null) return CastApproachPolicy.None;
            if (data.IsAutoAttack && data.NeedsLockTarget)
                return CastApproachPolicy.WalkIntoRange;
            if (data.ResolveMode == ESkillResolveMode.Blink)
                return CastApproachPolicy.ClampInstant;
            if (data.TargetMode == TargetType.Point)
                return CastApproachPolicy.WalkIntoRange;
            if (data.TargetMode == TargetType.SingleTarget && !data.IsAutoAttack)
                return CastApproachPolicy.WalkIntoRange;
            return CastApproachPolicy.None;
        }

        public static CastApproachPolicy GetCastApproach(int skillId)
        {
            return SkillCatalog.TryGet(skillId, out var data)
                ? GetCastApproach(data)
                : CastApproachPolicy.None;
        }

        /// <summary>锁定目标背后落点：沿接近方向越过目标 behindDistance。face 指向目标。</summary>
        public static bool TryGetLockBehindDestination(
            float casterX, float casterZ,
            float targetX, float targetZ,
            float behindDistance,
            out float destX, out float destZ,
            out float faceX, out float faceZ)
        {
            destX = targetX;
            destZ = targetZ;
            faceX = 0f;
            faceZ = 1f;
            if (behindDistance < 0.05f)
                behindDistance = 0.05f;

            float dx = targetX - casterX;
            float dz = targetZ - casterZ;
            NormalizeHorizontal(ref dx, ref dz);
            destX = targetX + dx * behindDistance;
            destZ = targetZ + dz * behindDistance;
            faceX = targetX - destX;
            faceZ = targetZ - destZ;
            NormalizeHorizontal(ref faceX, ref faceZ);
            return true;
        }

        /// <summary>锁定目标身前落点：停在目标前方 frontDistance。face 指向目标。</summary>
        public static bool TryGetLockFrontDestination(
            float casterX, float casterZ,
            float targetX, float targetZ,
            float frontDistance,
            out float destX, out float destZ,
            out float faceX, out float faceZ)
        {
            destX = targetX;
            destZ = targetZ;
            faceX = 0f;
            faceZ = 1f;
            if (frontDistance < 0.05f)
                frontDistance = 0.05f;

            float dx = targetX - casterX;
            float dz = targetZ - casterZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            NormalizeHorizontal(ref dx, ref dz);
            // 身前停点不能超过当前距离，否则会落到施法者身后。
            float stop = frontDistance;
            float maxStop = dist - 0.15f;
            if (maxStop < 0.05f) maxStop = 0.05f;
            if (stop > maxStop) stop = maxStop;
            destX = targetX - dx * stop;
            destZ = targetZ - dz * stop;
            faceX = targetX - destX;
            faceZ = targetZ - destZ;
            NormalizeHorizontal(ref faceX, ref faceZ);
            return true;
        }

        #endregion

        #region --- 点到线段距离 ---

        public static float DistPointToSegmentSq2D(
            float px, float pz,
            float ax, float az,
            float bx, float bz)
        {
            float abx = bx - ax;
            float abz = bz - az;
            float apx = px - ax;
            float apz = pz - az;
            float abLenSq = abx * abx + abz * abz;
            if (abLenSq < 0.0001f)
                return apx * apx + apz * apz;

            float t = (apx * abx + apz * abz) / abLenSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float cx = ax + abx * t;
            float cz = az + abz * t;
            float dx = px - cx;
            float dz = pz - cz;
            return dx * dx + dz * dz;
        }

        #endregion
    }
}
