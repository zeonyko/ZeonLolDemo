using System;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>地面材质类型。</summary>
    public enum GroundSurfaceType
    {
        Default = 0,
        Stone = 1,
        Grass = 2,
        Water = 3,
    }

    /// <summary>按车道横向坐标区间映射地面材质的配置规则。</summary>
    [Serializable]
    public class GroundSurfaceRule
    {
        public float MinAcross;
        public float MaxAcross;
        public string Surface = "Stone";
    }

    /// <summary>按名称包含关系映射地面材质的配置规则（用于 Tag / 材质名 / 物体名）。</summary>
    [Serializable]
    public class GroundSurfaceNameMap
    {
        public string Contains = "";
        public string Surface = "Stone";
    }

    /// <summary>地面材质分区表，对应 GroundSurfaceZones.json。</summary>
    [Serializable]
    public class GroundSurfaceZonesFile
    {
        public int Version = 2;
        public string DefaultSurface = "Stone";
        public bool UsePhysicsRaycast = true;
        public float RaycastUp = 2f;
        public float RaycastDistance = 6f;
        public GroundSurfaceRule[] Rules = Array.Empty<GroundSurfaceRule>();
        public GroundSurfaceNameMap[] TagMappings = Array.Empty<GroundSurfaceNameMap>();
        public GroundSurfaceNameMap[] MaterialNameMappings = Array.Empty<GroundSurfaceNameMap>();
    }

    /// <summary>脚下地面材质：先射线打 Tag/物理材质/贴图名，不行再按车道分区。</summary>
    public static class GroundSurfaceSampler
    {
        #region 配置加载与缓存
        static bool _loaded;
        static GroundSurfaceZonesFile _file;
        static GroundSurfaceType _default = GroundSurfaceType.Stone;
        static readonly RaycastHit[] Hits = new RaycastHit[8]; // Physics.RaycastNonAlloc 复用缓冲区

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _file = null;
            if (ConfigService.IsReady
                && ConfigService.Exists("Presentation", "GroundSurfaceZones.json"))
            {
                _file = ConfigService.LoadFile<GroundSurfaceZonesFile>("Presentation", "GroundSurfaceZones.json");
            }

            _file ??= new GroundSurfaceZonesFile();
            _default = Parse(_file.DefaultSurface, GroundSurfaceType.Stone);
        }

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
        #endregion

        #region 采样入口
        public static GroundSurfaceType Sample(Vector3 worldPos)
        {
            EnsureLoaded();

            if (_file.UsePhysicsRaycast && TrySamplePhysics(worldPos, out var fromPhysics))
                return fromPhysics;

            return SampleByLaneUv(worldPos);
        }

        public static string VfxKeyFor(GroundSurfaceType surface) => surface switch
        {
            GroundSurfaceType.Grass => "Vfx_GroundGrass",
            GroundSurfaceType.Water => "Vfx_GroundWater",
            _ => "Vfx_GroundStone",
        };
        #endregion

        #region Physics 射线采样
        static bool TrySamplePhysics(Vector3 worldPos, out GroundSurfaceType surface)
        {
            surface = _default;
            Vector3 origin = worldPos + Vector3.up * Mathf.Max(0.5f, _file.RaycastUp);
            float dist = Mathf.Max(1f, _file.RaycastDistance);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            // 取最近命中
            int best = 0;
            float bestDist = Hits[0].distance;
            for (int i = 1; i < count; i++)
            {
                if (Hits[i].distance < bestDist)
                {
                    bestDist = Hits[i].distance;
                    best = i;
                }
            }

            var hit = Hits[best];
            var col = hit.collider;
            if (col == null) return false;

            // 1) Tag
            if (!string.IsNullOrEmpty(col.tag) && col.tag != "Untagged"
                && TryMapName(col.tag, _file.TagMappings, out surface))
                return true;

            // 2) PhysicMaterial 名
            var pm = col.sharedMaterial;
            if (pm != null && TryMapName(pm.name, _file.MaterialNameMappings, out surface))
                return true;

            // 3) Renderer 材质名
            var rend = col.GetComponent<Renderer>() ?? col.GetComponentInParent<Renderer>();
            if (rend != null && rend.sharedMaterial != null
                && TryMapName(rend.sharedMaterial.name, _file.MaterialNameMappings, out surface))
                return true;

            // 4) 碰撞体/物体名启发式
            if (TryMapName(col.name, _file.MaterialNameMappings, out surface))
                return true;
            if (col.transform != null && TryMapName(col.transform.name, _file.MaterialNameMappings, out surface))
                return true;

            return false;
        }
        #endregion

        #region 车道 UV 回退采样
        static GroundSurfaceType SampleByLaneUv(Vector3 worldPos)
        {
            AramMap.WorldToLane(worldPos.x, worldPos.z, out _, out float across);
            var rules = _file.Rules;
            if (rules != null)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    var r = rules[i];
                    if (r == null) continue;
                    if (across >= r.MinAcross && across < r.MaxAcross)
                        return Parse(r.Surface, _default);
                }
            }
            return _default;
        }
        #endregion

        #region 名称映射与枚举解析工具
        static bool TryMapName(string name, GroundSurfaceNameMap[] maps, out GroundSurfaceType surface)
        {
            surface = _default;
            if (string.IsNullOrEmpty(name) || maps == null) return false;
            for (int i = 0; i < maps.Length; i++)
            {
                var m = maps[i];
                if (m == null || string.IsNullOrEmpty(m.Contains)) continue;
                if (name.IndexOf(m.Contains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    surface = Parse(m.Surface, _default);
                    return true;
                }
            }
            return false;
        }

        static GroundSurfaceType Parse(string name, GroundSurfaceType fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            return Enum.TryParse(name, true, out GroundSurfaceType t) ? t : fallback;
        }
        #endregion
    }
}
