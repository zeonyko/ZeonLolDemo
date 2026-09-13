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
        private static float[] _boundAlong = Array.Empty<float>();
        private static float[] _boundAcross = Array.Empty<float>();

        public static IReadOnlyList<BlockerAabb> All => Blockers;
        /// <summary>要不要再用斜向地图边界限制。</summary>
        public static bool UseUvMapBounds => _useUvMapBounds;
        public static bool HasWalkBoundary => _boundAlong != null && _boundAlong.Length >= 3;
        public static int WalkBoundaryCount => _boundAlong.Length;

        public static void GetWalkBoundaryLane(int i, out float along, out float across)
        {
            along = _boundAlong[i];
            across = _boundAcross[i];
        }

        public static void Clear()
        {
            Blockers.Clear();
            _useUvMapBounds = false;
            _boundAlong = Array.Empty<float>();
            _boundAcross = Array.Empty<float>();
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
            LoadWalkBoundary(cfg);
            _useUvMapBounds = cfg.UseUvMapBounds && !HasWalkBoundary;
            if (HasWalkBoundary)
                AssertPlayableSpawns();
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

        static void LoadWalkBoundary(MapCollisionConfig cfg)
        {
            var src = cfg.WalkBoundary;
            if (src == null || src.Length < 3)
                return;

            _boundAlong = new float[src.Length];
            _boundAcross = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var p = src[i];
                _boundAlong[i] = p != null ? p.Along : 0f;
                _boundAcross[i] = p != null ? p.Across : 0f;
            }
        }

        static void AssertPlayableSpawns()
        {
            AramMap.GetPlayerSpawn(ETeamId.Blue, out float bx, out float bz);
            AramMap.GetPlayerSpawn(ETeamId.Red, out float rx, out float rz);
            if (!Contains(bx, bz) || !Contains(rx, rz))
            {
                throw new InvalidOperationException(
                    "WalkBoundary excludes player spawn; movement would lock.");
            }
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

            if (HasWalkBoundary)
            {
                float tPoly = ClampDistanceByWalkBoundary(originX, originZ, dirX, dirZ, distance);
                if (tPoly < best) best = tPoly;
            }
            else if (_useUvMapBounds)
            {
                float tUv = ClampDistanceByUvBounds(originX, originZ, dirX, dirZ, distance, agentRadius);
                if (tUv < best) best = tUv;
            }

            if (best < 0f) best = 0f;
            if (best < distance)
                best = Math.Max(0f, best - Skin);
            return best > distance ? distance : best;
        }

        #endregion

        #region --- 内部射线/盒子 ---

        public static bool Contains(float x, float z)
        {
            if (!HasWalkBoundary)
                return true;
            AramMap.WorldToLane(x, z, out float along, out float across);
            return PointInBoundary(along, across);
        }

        public static void ClampToWalkable(ref float x, ref float z, float agentRadius = DefaultAgentRadius)
        {
            if (!HasWalkBoundary)
            {
                AramMap.ClampToLane(ref x, ref z, agentRadius);
                return;
            }

            if (Contains(x, z))
                return;

            AramMap.WorldToLane(x, z, out float along, out float across);
            along = Clamp(along, -AramMap.LaneHalfLength, AramMap.LaneHalfLength);
            across = 0f;
            AramMap.LaneToWorld(along, across, out x, out z);
            if (Contains(x, z))
                return;

            AramMap.LaneToWorld(0f, 0f, out x, out z);
        }

        static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        static bool PointInBoundary(float along, float across)
        {
            int n = _boundAlong.Length;
            bool inside = false;
            float x1 = _boundAlong[n - 1];
            float y1 = _boundAcross[n - 1];
            for (int i = 0; i < n; i++)
            {
                float x2 = _boundAlong[i];
                float y2 = _boundAcross[i];
                bool yCross = (y1 > across) != (y2 > across);
                if (yCross)
                {
                    float xAt = x1 + (across - y1) * (x2 - x1) / (y2 - y1);
                    if (along < xAt)
                        inside = !inside;
                }

                x1 = x2;
                y1 = y2;
            }

            return inside;
        }

        static float ClampDistanceByWalkBoundary(float ox, float oz, float dx, float dz, float distance)
        {
            AramMap.WorldToLane(ox + dx * distance, oz + dz * distance, out float au, out float av);
            if (PointInBoundary(au, av))
                return distance;
            if (!Contains(ox, oz))
                return 0f;

            float lo = 0f;
            float hi = distance;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                AramMap.WorldToLane(ox + dx * mid, oz + dz * mid, out float u, out float v);
                if (PointInBoundary(u, v))
                    lo = mid;
                else
                    hi = mid;
            }

            return lo;
        }

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

        private static float MaxTInsideAxis(float origin, float dir, float lim)
        {
            if (Math.Abs(dir) < 1e-8f)
                return (origin < -lim || origin > lim) ? -1f : float.PositiveInfinity;

            if (dir > 0f)
                return (lim - origin) / dir;
            return (-lim - origin) / dir;
        }

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
