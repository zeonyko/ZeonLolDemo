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
    /// <summary>Scene 里画出可行走内沿，并按活体甲板采样写出碰撞表。</summary>
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
                throw new System.InvalidOperationException("场景里找不到地图。先打开带 Map 的战斗场景。");

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

                var left = new List<LanePoint>();
                var right = new List<LanePoint>();
                for (float along = AlongMin; along <= AlongMax + 1e-4f; along += AlongStep)
                {
                    if (!TrySampleRail(along, out float lo, out float hi))
                        continue;
                    left.Add(new LanePoint { Along = along, Across = lo });
                    right.Add(new LanePoint { Along = along, Across = hi });
                }

                Physics.queriesHitBackfaces = back;
                if (left.Count < 8)
                    throw new System.InvalidOperationException("甲板采样点太少: " + left.Count);

                var poly = new List<LanePoint>(left.Count + right.Count);
                poly.AddRange(left);
                for (int i = right.Count - 1; i >= 0; i--)
                    poly.Add(right[i]);

                if (!PointIn(poly, -50f, 0f) || !PointIn(poly, 50f, 0f) || !PointIn(poly, 0f, 0f))
                    throw new System.InvalidOperationException("采样结果没包住出生点或中路，未写入。");

                WriteConfigs(poly);
                SceneView.RepaintAll();
                Debug.Log($"[WalkBoundary] 已按场景甲板写入 {poly.Count} 点。看 Scene 青线。");
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

            // 广场水晶处中线是空洞：左右两翼都在就连起来，出生点才在里面。
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
            if (lo > -0.6f) lo = -0.6f;
            if (hi < 0.6f) hi = 0.6f;
            return hi - lo >= 1.2f;
        }

        static bool IsDeck(float along, float across)
        {
            AramMap.LaneToWorld(along, across, out float x, out float z);
            var origin = new Vector3(x, 24f, z);
            if (!Physics.Raycast(origin, Vector3.down, out var hit, 40f))
                return false;
            if (hit.point.y < DeckMinY || hit.point.y > DeckMaxY)
                return false;
            return hit.normal.y > 0.45f;
        }

        static void WriteConfigs(List<LanePoint> poly)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"MapId\": \"HowlingAbyss\",\n  \"UseUvMapBounds\": false,\n  \"Blockers\": [],\n  \"WalkBoundary\": [\n");
            for (int i = 0; i < poly.Count; i++)
            {
                sb.Append("    {\n      \"Along\": ");
                sb.Append(poly[i].Along.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(",\n      \"Across\": ");
                sb.Append(poly[i].Across.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append("\n    }");
                if (i + 1 < poly.Count) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  ]\n}\n");
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
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                Handles.DrawAAPolyLine(6f, a, b);
            }
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

            var pts = ParseLaneLoop(File.ReadAllText(path));
            _sceneStamp = stamp;
            _sceneLoop = pts.Count >= 3 ? pts : null;
            return _sceneLoop;
        }

        static List<Vector3> ParseLaneLoop(string json)
        {
            var pts = new List<Vector3>();
            float y = 0.45f;
            int i = 0;
            while (i < json.Length)
            {
                int a = json.IndexOf("\"Along\"", i, System.StringComparison.Ordinal);
                if (a < 0) break;
                int c = json.IndexOf("\"Across\"", a, System.StringComparison.Ordinal);
                if (c < 0) break;
                if (!TryReadNumber(json, a, out float along) || !TryReadNumber(json, c, out float across))
                    break;
                AramMap.LaneToWorld(along, across, out float x, out float z);
                pts.Add(new Vector3(x, y, z));
                i = c + 8;
            }

            return pts;
        }

        static bool TryReadNumber(string json, int keyIndex, out float value)
        {
            value = 0f;
            int colon = json.IndexOf(':', keyIndex);
            if (colon < 0) return false;
            int s = colon + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\n' || json[s] == '\r')) s++;
            int e = s;
            while (e < json.Length && "0123456789+-.eE".IndexOf(json[e]) >= 0) e++;
            return float.TryParse(json.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static bool PointIn(List<LanePoint> poly, float along, float across)
        {
            bool inside = false;
            int n = poly.Count;
            var last = poly[n - 1];
            for (int i = 0; i < n; i++)
            {
                var cur = poly[i];
                bool yCross = (last.Across > across) != (cur.Across > across);
                if (yCross)
                {
                    float xAt = last.Along + (across - last.Across) * (cur.Along - last.Along) / (cur.Across - last.Across);
                    if (along < xAt)
                        inside = !inside;
                }

                last = cur;
            }

            return inside;
        }
    }
}
