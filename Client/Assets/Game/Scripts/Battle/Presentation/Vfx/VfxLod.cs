using UnityEngine;

namespace Client.Battle
{
    /// <summary>特效距离分级：由近到远依次降低表现开销。</summary>
    public enum VfxLodLevel
    {
        Full = 0,
        Reduced = 1,
        TextOnly = 2,
        Culled = 3,
    }

    /// <summary>特效远了就简化：降粒子 / 只飘字 / 不播。</summary>
    public static class VfxLod
    {
        #region 距离阈值与存活计数
        public const float ReducedDistance = 35f;
        public const float TextOnlyDistance = 50f;
        public const float CulledDistance = 70f;
        public const int SoftCapOneShot = 24;

        static int _oneShotAlive; // 当前存活的一次性特效数量，超软上限时进一步限流

        public static int OneShotAlive => _oneShotAlive;
        #endregion

        #region 距离分级与生成许可
        public static VfxLodLevel Evaluate(Vector3 worldPos)
        {
            var cam = Camera.main;
            if (cam == null) return VfxLodLevel.Full;
            float d = Vector3.Distance(cam.transform.position, worldPos);
            if (d >= CulledDistance) return VfxLodLevel.Culled;
            if (d >= TextOnlyDistance) return VfxLodLevel.TextOnly;
            if (d >= ReducedDistance) return VfxLodLevel.Reduced;
            return VfxLodLevel.Full;
        }

        public static bool AllowVfxSpawn(Vector3 worldPos)
        {
            var level = Evaluate(worldPos);
            if (level >= VfxLodLevel.TextOnly) return false;
            if (_oneShotAlive >= SoftCapOneShot && level >= VfxLodLevel.Reduced)
                return false;
            return true;
        }

        public static void NotifySpawned() => _oneShotAlive++;

        public static void NotifyDespawned()
        {
            if (_oneShotAlive > 0) _oneShotAlive--;
        }

        public static void ResetCounters() => _oneShotAlive = 0;
        #endregion

        #region 按远近简化 Prefab 实例
        /// <summary>Reduced：关闭次级粒子（保留主 Renderer）。</summary>
        public static void ApplyToInstance(GameObject go, VfxLodLevel level)
        {
            if (go == null || level == VfxLodLevel.Full) return;

            var particles = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                if (ps == null) continue;
                // 保留第一个主爆发，关掉后续层
                if (level == VfxLodLevel.Reduced && i == 0) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }
        }
        #endregion
    }
}
