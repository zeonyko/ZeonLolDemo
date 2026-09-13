using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>画出逻辑可行走外框。</summary>
    public static class CollisionDebugView
    {
        static bool _enabled;
        static Drawer _drawer;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (value)
                    Ensure();
                else
                    _drawer?.SetVisible(false);
            }
        }

        public static void Ensure()
        {
            if (_drawer != null) return;
            var go = new GameObject("CollisionDebugView");
            Object.DontDestroyOnLoad(go);
            _drawer = go.AddComponent<Drawer>();
        }

        public static void Shutdown()
        {
            _enabled = false;
            if (_drawer == null) return;
            Object.Destroy(_drawer.gameObject);
            _drawer = null;
        }

        sealed class Drawer : MonoBehaviour
        {
            LineRenderer _uv;
            readonly Vector3[] _uvPts = new Vector3[5];

            void Awake()
            {
                _uv = CreateLine("WalkBoundary", 0.10f, new Color(0.2f, 0.95f, 0.85f, 0.95f));
            }

            LineRenderer CreateLine(string name, float width, Color color)
            {
                var child = new GameObject(name);
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.loop = false;
                line.numCapVertices = 2;
                line.sortingOrder = 80;
                line.startWidth = width;
                line.endWidth = width;
                BattleUnlitTint.Apply(line, color);
                return line;
            }

            void LateUpdate()
            {
                if (!Enabled)
                {
                    SetVisible(false);
                    return;
                }

                SetVisible(true);
                DrawBoundary();
            }

            public void SetVisible(bool on)
            {
                if (_uv != null) _uv.enabled = on;
            }

            void DrawBoundary()
            {
                float y = GameConstants.GroundY + 0.08f;
                if (WorldCollision.HasWalkBoundary)
                {
                    int n = WorldCollision.WalkBoundaryCount;
                    var pts = new Vector3[n + 1];
                    for (int i = 0; i < n; i++)
                    {
                        WorldCollision.GetWalkBoundaryLane(i, out float along, out float across);
                        AramMap.LaneToWorld(along, across, out float x, out float z);
                        pts[i] = new Vector3(x, y, z);
                    }

                    pts[n] = pts[0];
                    _uv.positionCount = pts.Length;
                    _uv.SetPositions(pts);
                    return;
                }

                float u = AramMap.LaneHalfLength;
                float v = AramMap.LaneHalfWidth;
                SetLaneCorner(0, -u, -v, y);
                SetLaneCorner(1, u, -v, y);
                SetLaneCorner(2, u, v, y);
                SetLaneCorner(3, -u, v, y);
                _uvPts[4] = _uvPts[0];
                _uv.positionCount = 5;
                _uv.SetPositions(_uvPts);
            }

            void SetLaneCorner(int i, float along, float across, float y)
            {
                AramMap.LaneToWorld(along, across, out float x, out float z);
                _uvPts[i] = new Vector3(x, y, z);
            }
        }
    }
}
