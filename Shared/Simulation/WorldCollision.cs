using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>水平面阻挡盒。</summary>
    public struct BlockerAabb
    {
        public float MinX;
        public float MaxX;
        public float MinZ;
        public float MaxZ;

        /// <summary>用中心点和大小生成阻挡盒。</summary>
        public static BlockerAabb FromCenterSize(float cx, float cz, float sizeX, float sizeZ)
        {
            float hx = sizeX * 0.5f;
            float hz = sizeZ * 0.5f;
            return new BlockerAabb
            {
                MinX = cx - hx,
                MaxX = cx + hx,
                MinZ = cz - hz,
                MaxZ = cz + hz
            };
        }
    }

    /// <summary>撞墙判定。两端用同一份墙，本地先动才对得上。嚎哭深渊还有斜向边界。</summary>
    public static class WorldCollision
    {
        #region --- 字段 / 基础操作 ---

        public const float DefaultAgentRadius = 0.45f;
        public const float Skin = 0.05f;

        private static readonly List<BlockerAabb> Blockers = new List<BlockerAabb>(16);
        private static bool _useUvMapBounds;

        public static IReadOnlyList<BlockerAabb> All => Blockers;
        /// <summary>要不要再用斜向地图边界限制。</summary>
        public static bool UseUvMapBounds => _useUvMapBounds;

        public static void Clear()
        {
            Blockers.Clear();
            _useUvMapBounds = false;
        }

        /// <summary>加一块水平阻挡。</summary>
        public static void AddBox(float centerX, float centerZ, float sizeX, float sizeZ)
        {
            Blockers.Add(BlockerAabb.FromCenterSize(centerX, centerZ, sizeX, sizeZ));
        }

        #endregion

        #region --- 加载 ---

        /// <summary>嚎哭深渊：斜向长方形可行走区。</summary>
        public static void LoadHowlingAbyss()
        {
            Clear();
            _useUvMapBounds = true;
        }

        /// <summary>从地图碰撞配置加载。失败时调用方自行回退。</summary>
        public static bool TryLoadFromConfig(MapCollisionConfig cfg)
        {
            if (cfg == null) return false;
            Clear();
            _useUvMapBounds = cfg.UseUvMapBounds;
            if (cfg.Blockers == null) return true;

            for (int i = 0; i < cfg.Blockers.Length; i++)
            {
                var b = cfg.Blockers[i];
                if (b == null) continue;
                float sx = b.SizeX > 0.01f ? b.SizeX : 0.01f;
                float sz = b.SizeZ > 0.01f ? b.SizeZ : 0.01f;
                AddBox(b.CenterX, b.CenterZ, sx, sz);
            }
            return true;
        }

        /// <summary>读取默认地图碰撞表。地图配置缺失或无效时直接失败。</summary>
        public static void LoadRequiredDefaultArenaFromConfig()
        {
            if (!ConfigService.IsReady)
                throw new InvalidOperationException("ConfigService is not ready.");
            if (!ConfigService.Exists(MapCollisionConfig.DefaultRelativePath))
            {
                throw new System.IO.FileNotFoundException(
                    $"Missing required {MapCollisionConfig.DefaultRelativePath}.");
            }

            var cfg = ConfigService.LoadFile<MapCollisionConfig>(MapCollisionConfig.DefaultRelativePath);
            if (cfg == null || !TryLoadFromConfig(cfg))
            {
                throw new System.IO.InvalidDataException(
                    $"Invalid {MapCollisionConfig.DefaultRelativePath}.");
            }
            if (!string.Equals(cfg.MapId, MapCollisionConfig.DefaultMapId, StringComparison.Ordinal))
            {
                throw new System.IO.InvalidDataException(
                    $"Collision config id mismatch: {cfg.MapId}.");
            }
        }

        #endregion

        #region --- 撞墙缩短 ---

        /// <summary>往前走，撞墙停在墙外。返回实际能走多远。</summary>
        public static float ClampMoveDistance(
            float originX,
            float originZ,
            float dirX,
            float dirZ,
            float distance,
            float agentRadius = DefaultAgentRadius)
        {
            if (distance <= 0f) return 0f;

            float magSq = dirX * dirX + dirZ * dirZ;
            if (magSq < 0.0001f) return 0f;
            float inv = 1f / (float)Math.Sqrt(magSq);
            dirX *= inv;
            dirZ *= inv;

            float best = distance;
            float expand = agentRadius + Skin;

            for (int i = 0; i < Blockers.Count; i++)
            {
                var b = Blockers[i];
                float minX = b.MinX - expand;
                float maxX = b.MaxX + expand;
                float minZ = b.MinZ - expand;
                float maxZ = b.MaxZ + expand;

                if (TryRayAabb2D(originX, originZ, dirX, dirZ, distance, minX, maxX, minZ, maxZ, out float tHit)
                    && tHit < best)
                {
                    best = tHit;
                }
            }

            if (_useUvMapBounds)
            {
                float tUv = ClampDistanceByUvBounds(originX, originZ, dirX, dirZ, distance, agentRadius);
                if (tUv < best) best = tUv;
            }

            if (best < 0f) best = 0f;
            // 没撞墙时不要扣空隙。步子太小会被扣成走不动。
            if (best < distance)
                best = Math.Max(0f, best - Skin);
            return best > distance ? distance : best;
        }

        #endregion

        #region --- 内部射线/盒子 ---

        // 斜向边界：沿推进/横向轴看会不会走出地图。
        private static float ClampDistanceByUvBounds(
            float ox, float oz, float dx, float dz, float distance, float agentRadius)
        {
            AramMap.WorldToLane(ox, oz, out float u0, out float v0);
            AramMap.WorldToLane(ox + dx, oz + dz, out float u1, out float v1);
            float du = u1 - u0;
            float dv = v1 - v0;

            float limU = AramMap.LaneHalfLength - agentRadius - Skin;
            float limV = AramMap.LaneHalfWidth - agentRadius - Skin;
            if (limU < 1f) limU = 1f;
            if (limV < 1f) limV = 1f;

            float tU = MaxTInsideAxis(u0, du, limU);
            float tV = MaxTInsideAxis(v0, dv, limV);
            float t = Math.Min(tU, tV);
            if (t < 0f) return 0f;
            if (t > distance) return distance;
            return t;
        }

        // 这一轴上最多还能走多远才出界。
        private static float MaxTInsideAxis(float origin, float dir, float lim)
        {
            if (Math.Abs(dir) < 1e-8f)
                return (origin < -lim || origin > lim) ? -1f : float.PositiveInfinity;

            if (dir > 0f)
                return (lim - origin) / dir;
            return (-lim - origin) / dir;
        }

        // 这条线和阻挡盒相不相交，相交就返回碰到的距离。
        private static bool TryRayAabb2D(
            float ox, float oz, float dx, float dz, float maxDist,
            float minX, float maxX, float minZ, float maxZ,
            out float tHit)
        {
            tHit = 0f;
            float tMin = 0f;
            float tMax = maxDist;

            if (!ClipAxis(ox, dx, minX, maxX, ref tMin, ref tMax)) return false;
            if (!ClipAxis(oz, dz, minZ, maxZ, ref tMin, ref tMax)) return false;

            if (tMax < 0f || tMin > maxDist) return false;

            if (tMin < 0f)
            {
                tHit = 0f;
                return true;
            }

            tHit = tMin;
            return true;
        }

        // 按这一轴裁剪能走的范围。
        private static bool ClipAxis(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (Math.Abs(dir) < 1e-8f)
                return origin >= min && origin <= max;

            float inv = 1f / dir;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }

        #endregion
    }
}
