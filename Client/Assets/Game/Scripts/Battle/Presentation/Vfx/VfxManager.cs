using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>世界特效入口。画面都来自 Assets/Game/Vfx 的 Prefab。</summary>
    public class VfxManager
    {
        #region 全局一份：开关
        public static VfxManager Instance { get; private set; }

        public static VfxManager Create()
        {
            Instance = new VfxManager();
            return Instance;
        }

        public void Shutdown()
        {
            VfxLibrary.ClearWorld();
            if (Instance == this)
                Instance = null;
        }
        #endregion

        #region 伤害 / 状态飘字
        /// <summary>伤害飘字。复用条目，少创建销毁。数字只用来显示。</summary>
        public TransientFx PlayDamageFloat(Vector3 worldPos, float damage)
        {
            PlayDamageFloat(worldPos, damage, 0u, DamageType.Physical);
            return null;
        }

        public void PlayDamageFloat(
            Vector3 worldPos, float damage, uint hitFlags, DamageType damageType)
        {
            var pool = CombatTextPool.Instance ?? CombatTextPool.Create();
            var kind = CombatTextUtil.ResolveDamageKind(damage, hitFlags, damageType);
            if (kind == CombatTextKind.Miss)
            {
                pool.Spawn(worldPos, "MISS", CombatTextKind.Miss);
                return;
            }
            if (Mathf.Abs(damage) < 0.01f) return;

            var style = CombatTextCatalog.GetStyle(kind);
            string text = CombatTextUtil.FormatDamage(damage, style);
            pool.Spawn(worldPos, text, kind);
        }

        public TransientFx PlayStatusPopup(Vector3 worldPos, string label, Color color)
        {
            var pool = CombatTextPool.Instance ?? CombatTextPool.Create();
            var style = CombatTextCatalog.GetStyle(CombatTextKind.Status);
            string text = CombatTextUtil.FormatStatus(label, style);
            pool.Spawn(worldPos, text, CombatTextKind.Status, color);
            return null;
        }
        #endregion

        #region 近战打击类特效
        public TransientFx PlayAttackSlash(Vector3 origin, Vector3 forward, float duration = 0.32f)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 spawn = origin + forward * 0.55f + Vector3.up * 0.35f;
            return VfxLibrary.SpawnTransient(
                "Vfx_Slash", spawn, forward,
                durationOverride: duration > 0.05f ? duration : 0.32f);
        }

        public TransientFx PlayCleave(Vector3 origin, Vector3 forward, float duration = 0.42f)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 spawn = origin + forward * 1.0f + Vector3.up * 0.4f;
            return VfxLibrary.SpawnTransient(
                "Vfx_Cleave", spawn, forward,
                durationOverride: duration > 0.05f ? duration : 0.42f,
                worldVelocity: forward * 1.9f + Vector3.up * 0.18f);
        }

        public TransientFx PlayHitSpark(Vector3 worldPos)
        {
            return VfxLibrary.SpawnTransient(
                "Vfx_Hit", worldPos + Vector3.up * 0.9f, Vector3.forward,
                durationOverride: 0.32f,
                worldVelocity: Vector3.up * 0.55f);
        }
        #endregion

        #region 地面 / 位移 / 延时特效
        /// <summary>脚底地面材质交互（石/草/水）。</summary>
        public TransientFx PlayGroundSplash(Vector3 worldPos)
        {
            Vector3 feet = worldPos;
            feet.y = GameConstants.GroundY + 0.03f;
            if (VfxLod.Evaluate(feet) >= VfxLodLevel.TextOnly)
                return null;

            var surface = GroundSurfaceSampler.Sample(feet);
            string key = GroundSurfaceSampler.VfxKeyFor(surface);
            return VfxLibrary.SpawnTransient(
                key, feet, Vector3.forward,
                durationOverride: 0.42f,
                worldVelocity: Vector3.up * 0.08f);
        }

        public TransientFx PlayBlinkBurst(Vector3 feet, float duration = 0.32f)
        {
            return VfxLibrary.SpawnTransient(
                "Vfx_Blink", feet + Vector3.up * 0.55f, Vector3.forward,
                durationOverride: duration > 0.05f ? duration : 0.32f);
        }

        public TransientFx PlayExplodeRing(Vector3 worldPos, float radius, float durationOverride = 0f)
        {
            float r = radius > 0.1f ? radius : 1.5f;
            Vector3 feet = worldPos;
            feet.y = GameConstants.GroundY + 0.03f;
            return VfxLibrary.SpawnTransient(
                "Vfx_ExplodeRing", feet, Vector3.forward,
                durationOverride: durationOverride > 0.05f ? durationOverride : 0.35f,
                scaleMul: Vector3.one * r);
        }

        public TransientFx PlayImpactBurst(
            Vector3 worldPos, float radius, Color? color = null, float durationOverride = 0f)
        {
            float r = radius > 0.1f ? radius : 1.5f;
            float scale = Mathf.Clamp(r * 0.55f, 0.7f, 2.8f);
            return VfxLibrary.SpawnTransient(
                "Vfx_ImpactBurst", worldPos + Vector3.up * 0.2f, Vector3.forward,
                durationOverride: durationOverride > 0.05f ? durationOverride : 0.4f,
                worldVelocity: Vector3.up * 0.4f,
                tint: color,
                scaleMul: new Vector3(scale, scale, scale));
        }

        public void PlayDelayedImpactFuse(
            Vector3 worldPos, float radius, float delay, TransientFx reuseOrb = null, Color? color = null)
        {
            var go = new GameObject("Fx_ImpactFuse");
            go.transform.SetParent(BattleScene.Vfx, true);
            go.transform.position = worldPos;
            var fuse = go.AddComponent<DelayedImpactFuse>();
            fuse.Bind(worldPos, radius, delay, reuseOrb, color ?? new Color(1f, 0.4f, 0.1f, 1f));
        }
        #endregion

        #region 弹道特效
        public TransientFx PlayAuthProjectile(
            int projectileId, Vector3 origin, Vector3 forward, float speed, float maxDistance)
        {
            ProjectileCatalog.TryGet(projectileId, out var logic);
            ProjectilePresentationCatalog.TryGet(projectileId, out var pres);
            // persist：由 ProjectileViewService 每帧推位置；传入速度供兜底运动
            return SpawnProjectileFx(
                logic, pres, origin, forward,
                speedOverride: speed,
                maxDistOverride: maxDistance,
                persist: true);
        }
        #endregion

        #region 按 Key 播放分发（配置驱动）
        public TransientFx PlayByVfxKey(
            string vfxKey, Vector3 origin, Vector3 forward, float range, float duration = 0f,
            Vector3? scaleMul = null)
        {
            string norm = VfxLibrary.NormalizeKey(vfxKey);

            if (IsGroundShapeKey(norm))
                return PlayGroundShape(norm, origin, forward, duration, range, scaleMul);

            if (IsOneShotKey(norm))
                return PlayOneShotByKey(norm, origin, forward, duration, range);

            if (IsMuzzleKey(norm))
                return PlayMuzzleFlash(norm, origin, forward, duration);

            if (ProjectilePresentationCatalog.TryGetByVfxKey(norm, out var pres) && pres != null)
            {
                ProjectileCatalog.TryGet(pres.ProjectileId, out var logic);
                float speed = logic != null && logic.Speed > 0.1f ? logic.Speed : 16f;
                float maxDist = range > 0.1f
                    ? range
                    : (logic != null && logic.MaxDistance > 0.1f ? logic.MaxDistance : GameConstants.DefaultCastRange);
                return SpawnProjectileFx(logic, pres, origin, forward, speed, maxDist, persist: false);
            }

            float dur = duration > 0.05f ? duration : 0.45f;
            Vector3? scale = scaleMul;
            if (!scale.HasValue && range > 0.1f)
                scale = Vector3.one * range;
            return VfxLibrary.SpawnTransient(
                norm, origin, forward,
                durationOverride: dur,
                scaleMul: scale);
        }

        static bool IsGroundShapeKey(string norm)
        {
            return norm == "Vfx_GroundBox" || norm == "Vfx_SectorFan"
                   || norm == "Vfx_ShieldRing" || norm == "Vfx_ExplodeRing"
                   || norm == "Vfx_SlowRing";
        }

        static bool IsOneShotKey(string norm)
        {
            return norm == "Vfx_Slash" || norm == "Vfx_Cleave" || norm == "Vfx_Hit"
                   || norm == "Vfx_HitLight" || norm == "Vfx_HitHeavy" || norm == "Vfx_HitPierce"
                   || norm == "Vfx_HitCrit" || norm == "Vfx_CastBurst"
                   || norm == "Vfx_Blink" || norm == "Vfx_ImpactBurst"
                   || norm == "Vfx_GroundStone"
                   || norm == "Vfx_TowerCharge" || norm == "Vfx_SuperArmor";
        }

        static bool IsMuzzleKey(string norm)
        {
            // 仅枪口闪；飞弹 key（如 Vfx_HeroBolt）走权威弹道 / 弹道表现表，勿收成贴手闪一下
            return norm == "Vfx_MuzzleFlash"
                   || norm == "Vfx_Fireball"
                   || norm == "Vfx_TripleBolt"
                   || norm == "Vfx_FuseOrb";
        }

        static TransientFx PlayMuzzleFlash(string norm, Vector3 origin, Vector3 forward, float duration)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            float dur = duration > 0.05f ? duration : 0.28f;
            return VfxLibrary.SpawnTransient(
                norm,
                origin + forward * 0.45f + Vector3.up * 0.9f,
                forward,
                durationOverride: dur,
                scaleMul: Vector3.one * 0.55f);
        }

        TransientFx PlayOneShotByKey(
            string norm, Vector3 origin, Vector3 forward, float duration, float range)
        {
            float dur = duration > 0.05f ? duration : 0f;
            switch (norm)
            {
                case "Vfx_Slash":
                    return PlayAttackSlash(origin, forward, dur > 0f ? dur : 0.32f);
                case "Vfx_Cleave":
                    return PlayCleave(origin, forward, dur > 0f ? dur : 0.42f);
                case "Vfx_Hit":
                case "Vfx_HitLight":
                    return PlayTieredHit("Vfx_HitLight", origin, 0.85f, dur);
                case "Vfx_HitHeavy":
                    return PlayTieredHit("Vfx_HitHeavy", origin, 1.15f, dur);
                case "Vfx_HitPierce":
                    return PlayTieredHit("Vfx_HitPierce", origin, 1.0f, dur);
                case "Vfx_HitCrit":
                    return PlayTieredHit("Vfx_HitCrit", origin, 1.25f, dur);
                case "Vfx_CastBurst":
                    return VfxLibrary.SpawnTransient(
                        "Vfx_CastBurst", origin + Vector3.up * 0.1f, forward,
                        durationOverride: dur > 0f ? dur : 0.28f,
                        worldVelocity: Vector3.up * 0.25f);
                case "Vfx_Blink":
                    return PlayBlinkBurst(origin, dur);
                case "Vfx_ImpactBurst":
                    return PlayImpactBurst(origin, range > 0.1f ? range : 1.5f, durationOverride: dur);
                case "Vfx_GroundStone":
                    return PlayGroundRing("Vfx_GroundStone", origin, 1f, dur > 0f ? dur : 0.5f);
                case "Vfx_TowerCharge":
                    return VfxLibrary.SpawnTransient(
                        "Vfx_TowerCharge", origin + Vector3.up * 0.05f, forward,
                        durationOverride: dur > 0f ? dur : 1.0f);
                case "Vfx_SuperArmor":
                    return VfxLibrary.SpawnTransient(
                        "Vfx_SuperArmor", origin + Vector3.up * 0.4f, forward,
                        durationOverride: dur > 0f ? dur : 0.5f);
                default:
                    return null;
            }
        }

        static TransientFx PlayGroundShape(
            string key, Vector3 origin, Vector3 forward, float duration, float range, Vector3? scaleMul)
        {
            Vector3 feet = origin;
            feet.y = GameConstants.GroundY + 0.03f;
            float dur = duration > 0.05f ? duration : 0.4f;
            Vector3 scale = scaleMul ?? (range > 0.1f ? Vector3.one * range : Vector3.one);
            Color? tint = null;
            if (key == "Vfx_SlowRing" && dur >= 4f)
                tint = new Color(1f, 0.45f, 0.12f, 0.9f);
            return VfxLibrary.SpawnTransient(
                key, feet, forward.sqrMagnitude > 0.0001f ? forward : Vector3.forward,
                durationOverride: dur,
                tint: tint,
                scaleMul: scale);
        }

        static TransientFx PlayGroundRing(string key, Vector3 origin, float radius, float duration)
        {
            float scale = radius > 0.1f ? radius : 1f;
            Vector3 feet = origin;
            feet.y = GameConstants.GroundY + 0.03f;
            return VfxLibrary.SpawnTransient(
                key, feet, Vector3.forward,
                durationOverride: duration,
                scaleMul: Vector3.one * scale);
        }

        static TransientFx PlayTieredHit(string key, Vector3 worldPos, float scale, float duration)
        {
            return VfxLibrary.SpawnTransient(
                key, worldPos + Vector3.up * 0.9f, Vector3.forward,
                durationOverride: duration > 0.05f ? duration : 0.28f,
                worldVelocity: Vector3.up * 0.7f,
                scaleMul: Vector3.one * scale);
        }
        #endregion

        #region 弹道生成辅助
        /// <summary>按飞行数据和特效生成弹道。persist=true 时由调用方自己取消。</summary>
        private static TransientFx SpawnProjectileFx(
            ProjectileConfig logic, ProjectilePresentationConfig pres,
            Vector3 origin, Vector3 forward,
            float speedOverride = 0f, float maxDistOverride = 0f, bool persist = true)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            string key = string.IsNullOrEmpty(pres?.VfxKey) ? "Vfx_HeroBolt" : pres.VfxKey;
            var color = pres != null
                ? new Color(pres.VfxColorR, pres.VfxColorG, pres.VfxColorB, pres.VfxColorA)
                : new Color(0.9f, 0.9f, 1f, 0.9f);
            float sx = pres != null && pres.VfxScaleX > 0.01f ? pres.VfxScaleX : 0.45f;
            float sy = pres != null && pres.VfxScaleY > 0.01f ? pres.VfxScaleY : 0.45f;
            // Prefab 默认约 1 单位；JSON 缩放映射到球体直径
            float scale = Mathf.Max(sx, sy) * 1.6f;
            float yLift = pres != null && pres.VfxYLift > 0.01f ? pres.VfxYLift : 0.9f;
            Vector3 spawn = origin + Vector3.up * yLift + forward * 0.35f;

            if (persist)
            {
                return VfxLibrary.SpawnTransient(
                    key, spawn, forward,
                    persist: true,
                    tint: color,
                    scaleMul: Vector3.one * scale);
            }

            float speed = speedOverride > 0.1f
                ? speedOverride
                : (logic != null && logic.Speed > 0.1f ? logic.Speed : 16f);
            float maxDist = maxDistOverride > 0.1f
                ? maxDistOverride
                : (logic != null && logic.MaxDistance > 0.1f ? logic.MaxDistance : GameConstants.DefaultCastRange);
            float flyTime = maxDist / speed;
            if (flyTime < 0.12f) flyTime = 0.12f;
            return VfxLibrary.SpawnTransient(
                key, spawn, forward,
                durationOverride: flyTime,
                worldVelocity: forward * speed,
                tint: color,
                scaleMul: Vector3.one * scale);
        }
        #endregion
    }
}
