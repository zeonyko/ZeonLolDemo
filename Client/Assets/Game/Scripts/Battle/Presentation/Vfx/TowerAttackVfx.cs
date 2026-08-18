using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>防御塔攻击画面：充能光球 + 锁定光束 + 飞行弹。</summary>
    public static class TowerAttackVfx
    {
        #region 表现参数
        const float TowerMuzzleLift = 2.1f;
        const float TargetChestLift = 1.0f;
        const float BeamDuration = 0.28f;
        const float ChargeDuration = 0.22f;
        #endregion

        #region 公开 API：起手 / 命中
        public static void PlayCast(Entity caster, long targetId, Vector3 targetPosHint)
        {
            if (caster == null || VfxManager.Instance == null) return;
            if (!VfxLod.AllowVfxSpawn(caster.GetComponent<TransformComponent>()?.Position ?? targetPosHint))
                return;

            Vector3 origin = ResolveTowerMuzzle(caster);
            Vector3 target = ResolveTarget(targetId, targetPosHint, origin);
            Vector3 dir = target - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            else dir.Normalize();

            // 炮口充能
            VfxLibrary.SpawnTransient(
                "Vfx_TowerCharge", origin, dir,
                durationOverride: ChargeDuration,
                worldVelocity: Vector3.up * 0.2f);

            // 锁定光束朝向目标；飞行弹改由服务器弹道包驱动，避免播两发
            SpawnLockBeam(origin, target);
        }

        public static void PlayHitImpact(Vector3 feetOrHit)
        {
            if (VfxManager.Instance == null) return;
            Vector3 pos = feetOrHit;
            pos.y = GameConstants.GroundY + 0.05f;
            if (!VfxLod.AllowVfxSpawn(pos)) return;
            VfxManager.Instance.PlayImpactBurst(pos, 0.65f, new Color(1f, 0.5f, 0.15f, 0.9f));
            VfxManager.Instance.PlayGroundSplash(pos);
        }
        #endregion

        #region 内部：光束与坐标解析
        static void SpawnLockBeam(Vector3 from, Vector3 to)
        {
            Vector3 mid = (from + to) * 0.5f;
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 0.2f) return;

            Vector3 flat = delta;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();

            VfxLibrary.SpawnTransient(
                "Vfx_TowerLockBeam", mid, flat,
                durationOverride: BeamDuration,
                worldVelocity: Vector3.zero,
                onSpawned: StretchBeam);

            void StretchBeam(TransientFx spawned)
            {
                if (spawned == null) return;
                spawned.transform.position = mid;
                spawned.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
                var scale = spawned.transform.localScale;
                scale.z = len;
                scale.x = 0.16f;
                scale.y = 0.16f;
                spawned.transform.localScale = scale;
            }

            // 两端额外闪点（不受非均匀拉伸压扁）
            VfxLibrary.SpawnTransient(
                "Vfx_HitLight", from, flat,
                durationOverride: 0.18f,
                worldVelocity: Vector3.zero,
                scaleMul: new Vector3(0.55f, 0.55f, 0.55f),
                tint: new Color(1f, 0.7f, 0.25f, 0.9f));
            VfxLibrary.SpawnTransient(
                "Vfx_HitLight", to, flat,
                durationOverride: 0.2f,
                worldVelocity: Vector3.zero,
                scaleMul: new Vector3(0.7f, 0.7f, 0.7f),
                tint: new Color(1f, 0.55f, 0.18f, 0.95f));
        }

        static Vector3 ResolveTowerMuzzle(Entity caster)
        {
            var t = caster.GetComponent<TransformComponent>();
            Vector3 feet = t != null ? t.Position : Vector3.zero;
            feet.y = GameConstants.GroundY;
            return feet + Vector3.up * TowerMuzzleLift;
        }

        static Vector3 ResolveTarget(long targetId, Vector3 hint, Vector3 fallbackOrigin)
        {
            if (targetId != 0)
            {
                var ent = EntityManager.Instance?.GetEntity(targetId);
                var t = ent?.GetComponent<TransformComponent>();
                if (t != null)
                    return t.Position + Vector3.up * TargetChestLift;
            }

            if (hint.sqrMagnitude > 0.0001f)
            {
                var p = hint;
                if (p.y < GameConstants.GroundY + 0.2f)
                    p.y = GameConstants.GroundY + TargetChestLift;
                return p;
            }

            return fallbackOrigin + Vector3.forward * 4f;
        }
        #endregion
    }
}
