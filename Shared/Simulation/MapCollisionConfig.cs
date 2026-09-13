using System;

namespace Shared
{
    /// <summary>一条水平阻挡（世界水平面盒子）。</summary>
    [Serializable]
    public class MapCollisionBlockerData
    {
        public string Name = ""; // 阻挡名（调试用）
        public float CenterX;
        public float CenterZ;
        public float SizeX = 1f;
        public float SizeZ = 1f;
    }

    /// <summary>地图碰撞配置。场景导出的 JSON，两端读同一份。</summary>
    [Serializable]
    public class MapCollisionConfig
    {
        public const string DefaultMapId = "HowlingAbyss";
        public const string DefaultRelativePath = "Maps/Map_HowlingAbyss_Collision.json";

        public string MapId = DefaultMapId;
        public bool UseUvMapBounds = true;
        public MapCollisionBlockerData[] Blockers = Array.Empty<MapCollisionBlockerData>();
        public LanePoint[] WalkBoundary = Array.Empty<LanePoint>();
    }

    /// <summary>推进/横向坐标上的一点。</summary>
    [Serializable]
    public class LanePoint
    {
        public float Along;
        public float Across;
    }
}
