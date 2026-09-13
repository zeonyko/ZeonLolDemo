using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>运行时画出可行走外框。</summary>
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
                if (value) Ensure();
                else _drawer?.SetVisible(false);
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
            LineRenderer _line;
            readonly Vector3[] _rect = new Vector3[5];

            void Awake()
            {
                var child = new GameObject("WalkBoundary");
                child.transform.SetParent(transform, false);
                _line = child.AddComponent<LineRenderer>();
                _line.useWorldSpace = true;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.receiveShadows = false;
                _line.loop = true;
                _line.numCapVertices = 2;
                _line.sortingOrder = 80;
                _line.startWidth = 0.22f;
                _line.endWidth = 0.22f;
                BattleUnlitTint.Apply(_line, new Color(0.05f, 1f, 0.75f, 1f));
            }

            void LateUpdate()
            {
                if (!Enabled || _line == null)
                {
                    if (_line != null) _line.enabled = false;
                    return;
                }

                _line.enabled = true;
                float y = GameConstants.GroundY + 0.45f;
                if (WorldCollision.HasWalkBoundary)
                {
                    int n = WorldCollision.WalkBoundaryCount;
                    var pts = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        WorldCollision.GetWalkBoundaryLane(i, out float along, out float across);
                        AramMap.LaneToWorld(along, across, out float x, out float z);
                        pts[i] = new Vector3(x, y, z);
                    }

                    _line.positionCount = pts.Length;
                    _line.SetPositions(pts);
                    return;
                }

                float u = AramMap.LaneHalfLength;
                float v = AramMap.LaneHalfWidth;
                Set(0, -u, -v, y);
                Set(1, u, -v, y);
                Set(2, u, v, y);
                Set(3, -u, v, y);
                _rect[4] = _rect[0];
                _line.positionCount = 5;
                _line.SetPositions(_rect);
            }

            void Set(int i, float along, float across, float y)
            {
                AramMap.LaneToWorld(along, across, out float x, out float z);
                _rect[i] = new Vector3(x, y, z);
            }

            public void SetVisible(bool on)
            {
                if (_line != null) _line.enabled = on;
            }
        }
    }
}
