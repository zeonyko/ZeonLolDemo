using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Client.Battle;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Client.EditorTools
{
    /// <summary>Scene 画出整圈内沿；菜单按活体甲板采样左右岸。</summary>
    [InitializeOnLoad]
    public static class WalkBoundaryBaker
    {
        const float DeckMinY = -0.4f;
        const float DeckMaxY = 0.55f;
        const float AlongMin = -56f;
        const float AlongMax = 56f;
        const float AcrossLimit = 12f;
        const float AlongStep = 0.5f;
        const float AcrossStep = 0.15f;

        static List<Vector3> _sceneLoop;
        static string _sceneStamp;

        static WalkBoundaryBaker()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        [MenuItem("Game/Map/从场景甲板采样可行走内沿", false, 101)]
        public static void BakeFromLiveMap()
        {
            Transform root = BattleScene.Map;
            if (root == null)
            {
                var riot = GameObject.Find("Riot_HowlingAbyss");
                root = riot != null ? riot.transform : null;
            }

            if (root == null)
                throw new System.InvalidOperationException("场景里找不到地图。");

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            var added = new List<MeshCollider>();
            try
            {
                for (int i = 0; i < filters.Length; i++)
                {
                    var mf = filters[i];
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mf.GetComponent<MeshCollider>() != null) continue;
                    var col = mf.gameObject.AddComponent<MeshCollider>();
                    col.sharedMesh = mf.sharedMesh;
                    added.Add(col);
                }

                Physics.SyncTransforms();
                bool back = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;

                var left = new List<Vector2>();
                var right = new List<Vector2>();
                for (float along = AlongMin; along <= AlongMax + 1e-4f; along += AlongStep)
                {
                    if (!TrySampleRail(along, out float lo, out float hi))
                        continue;
                    left.Add(new Vector2(along, lo));
                    right.Add(new Vector2(along, hi));
                }

                Physics.queriesHitBackfaces = back;
                if (left.Count < 8)
                    throw new System.InvalidOperationException("甲板采样点太少: " + left.Count);

                var along = new List<float>(left.Count + right.Count);
                var across = new List<float>(left.Count + right.Count);
                for (int i = 0; i < left.Count; i++)
                {
                    along.Add(left[i].x);
                    across.Add(left[i].y);
                }

                for (int i = right.Count - 1; i >= 0; i--)
                {
                    along.Add(right[i].x);
                    across.Add(right[i].y);
                }

                if (!PointIn(along, across, -50f, 0f) || !PointIn(along, across, 50f, 0f) || !PointIn(along, across, 0f, 0f))
                    throw new System.InvalidOperationException("采样结果没包住出生点或中路，未写入。");

                WriteConfigs(along, across);
                _sceneLoop = null;
                SceneView.RepaintAll();
                Debug.Log($"[WalkBoundary] 已按场景甲板写入 {along.Count} 点。");
            }
            finally
            {
                for (int i = 0; i < added.Count; i++)
                {
                    if (added[i] != null)
                        Object.DestroyImmediate(added[i]);
                }
            }
        }

        static bool TrySampleRail(float along, out float lo, out float hi)
        {
            var hits = new List<float>(64);
            for (float across = -AcrossLimit; across <= AcrossLimit + 1e-4f; across += AcrossStep)
            {
                if (IsDeck(along, across))
                    hits.Add(across);
            }

            lo = 0f;
            hi = 0f;
            if (hits.Count == 0)
                return false;

            int seed = 0;
            float best = float.MaxValue;
            for (int i = 0; i < hits.Count; i++)
            {
                float d = Mathf.Abs(hits[i]);
                if (d < best)
                {
                    best = d;
                    seed = i;
                }
            }

            int a = seed;
            int b = seed;
            while (a > 0 && hits[a] - hits[a - 1] <= AcrossStep * 2.2f) a--;
            while (b + 1 < hits.Count && hits[b + 1] - hits[b] <= AcrossStep * 2.2f) b++;

            if (hits[a] > 0.4f)
            {
                for (int i = a - 1; i >= 0; i--)
                {
                    if (hits[i] < -0.2f)
                    {
                        a = i;
                        while (a > 0 && hits[a] - hits[a - 1] <= AcrossStep * 2.2f) a--;
                        break;
                    }
                }
            }

            if (hits[b] < -0.4f)
            {
                for (int i = b + 1; i < hits.Count; i++)
                {
                    if (hits[i] > 0.2f)
                    {
                        b = i;
                        while (b + 1 < hits.Count && hits[b + 1] - hits[b] <= AcrossStep * 2.2f) b++;
                        break;
                    }
                }
            }

            lo = hits[a] - 0.05f;
            hi = hits[b] + 0.05f;
            if (lo > -0.8f) lo = -0.8f;
            if (hi < 0.8f) hi = 0.8f;
            return hi - lo >= 1.2f;
        }

        static bool IsDeck(float along, float across)
        {
            AramMap.LaneToWorld(along, across, out float x, out float z);
            if (!Physics.Raycast(new Vector3(x, 24f, z), Vector3.down, out var hit, 40f))
                return false;
            if (hit.point.y < DeckMinY || hit.point.y > DeckMaxY)
                return false;
            return hit.normal.y > 0.45f;
        }

        static void WriteConfigs(List<float> along, List<float> across)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"MapId\": \"HowlingAbyss\",\n  \"UseUvMapBounds\": true,\n  \"Blockers\": [],\n  \"WalkAlong\": [");
            for (int i = 0; i < along.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(along[i].ToString("0.###", CultureInfo.InvariantCulture));
            }

            sb.Append("],\n  \"WalkAcross\": [");
            for (int i = 0; i < across.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(across[i].ToString("0.###", CultureInfo.InvariantCulture));
            }

            sb.Append("]\n}\n");
            string json = sb.ToString();
            string[] dest =
            {
                BattlePaths.RepoConfig("Maps", "Map_HowlingAbyss_Collision.json"),
                Path.Combine(BattlePaths.ConfigRoot, "Maps", "Map_HowlingAbyss_Collision.json"),
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Server", "Config", "Maps", "Map_HowlingAbyss_Collision.json"))
            };
            foreach (var p in dest)
            {
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(p, json);
            }

            AssetDatabase.Refresh();
        }

        static void OnSceneGui(SceneView view)
        {
            var pts = LoadWorldLoop();
            if (pts == null || pts.Count < 3)
                return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(0.15f, 1f, 0.85f, 1f);
            for (int i = 0; i < pts.Count; i++)
                Handles.DrawAAPolyLine(6f, pts[i], pts[(i + 1) % pts.Count]);
        }

        static List<Vector3> LoadWorldLoop()
        {
            string path = Path.Combine(BattlePaths.ConfigRoot, "Maps", "Map_HowlingAbyss_Collision.json");
            if (!File.Exists(path))
                path = BattlePaths.RepoConfig("Maps", "Map_HowlingAbyss_Collision.json");
            if (!File.Exists(path))
                return null;

            string stamp = path + File.GetLastWriteTimeUtc(path).Ticks;
            if (_sceneLoop != null && stamp == _sceneStamp)
                return _sceneLoop;

            var cfg = JsonUtility.FromJson<MapCollisionConfig>(File.ReadAllText(path));
            var pts = new List<Vector3>();
            float y = 0.45f;
            if (cfg != null && cfg.WalkAlong != null && cfg.WalkAcross != null
                && cfg.WalkAlong.Length >= 3 && cfg.WalkAlong.Length == cfg.WalkAcross.Length)
            {
                for (int i = 0; i < cfg.WalkAlong.Length; i++)
                {
                    AramMap.LaneToWorld(cfg.WalkAlong[i], cfg.WalkAcross[i], out float x, out float z);
                    pts.Add(new Vector3(x, y, z));
                }
            }

            _sceneStamp = stamp;
            _sceneLoop = pts.Count >= 3 ? pts : null;
            return _sceneLoop;
        }

        static bool PointIn(List<float> along, List<float> across, float u, float v)
        {
            bool inside = false;
            int n = along.Count;
            float x1 = along[n - 1];
            float y1 = across[n - 1];
            for (int i = 0; i < n; i++)
            {
                float x2 = along[i];
                float y2 = across[i];
                if ((y1 > v) != (y2 > v))
                {
                    float xAt = x1 + (v - y1) * (x2 - x1) / (y2 - y1);
                    if (u < xAt)
                        inside = !inside;
                }

                x1 = x2;
                y1 = y2;
            }

            return inside;
        }
    }
}
