using System.Collections.Generic;
using System.IO;
using Client.Battle;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Client.EditorTools
{
    /// <summary>把场景里的碰撞盒导出成地图碰撞配置。</summary>
    public static class MapSceneBuilder
    {
        [MenuItem("Game/Map/导出碰撞配置", false, 100)]
        public static void ExportCollisionMenu() => ExportCollisionInternal(log: true);

        static void ExportCollisionInternal(bool log)
        {
            var volumes = Object.FindObjectsOfType<MapCollisionVolume>();
            var list = new List<MapCollisionBlockerData>(volumes.Length);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i] == null || !volumes[i].IncludeInExport) continue;
                list.Add(volumes[i].ToBlockerData());
            }

            LanePoint[] keep = System.Array.Empty<LanePoint>();
            string clientAbs = BattlePaths.Config("Maps", "Map_HowlingAbyss_Collision.json");
            if (File.Exists(clientAbs))
            {
                var old = JsonUtility.FromJson<MapCollisionConfig>(File.ReadAllText(clientAbs));
                if (old != null && old.WalkBoundary != null)
                    keep = old.WalkBoundary;
            }

            var cfg = new MapCollisionConfig
            {
                MapId = MapCollisionConfig.DefaultMapId,
                UseUvMapBounds = keep.Length < 3,
                Blockers = list.ToArray(),
                WalkBoundary = keep
            };
            string json = JsonUtility.ToJson(cfg, true);
            WriteText(clientAbs, json);
            AssetDatabase.Refresh();
            if (log)
                Debug.Log($"[MapSceneBuilder] Exported blockers={list.Count}\n  {clientAbs}");
        }

        static void WriteText(string absPath, string content)
        {
            string dir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(absPath, content);
        }
    }
}
