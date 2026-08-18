using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>普攻吸附锁定：目标脚下小圈加点，类似王者荣耀锁敌点。</summary>
    public static class AttackTargetMark
    {
        const int Segments = 28;
        const float GroundLift = 0.08f;
        const float RingRadius = 0.48f;
        const float DotRadius = 0.11f;

        public static void Ensure() => Drawer.Ensure();

        sealed class Drawer : MonoBehaviour
        {
            static Drawer _instance;
            LineRenderer _ring;
            LineRenderer _dot;

            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("AttackTargetMark");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Drawer>();
                _instance.Build();
            }

            void Build()
            {
                _ring = CreateLine("LockRing", 0.07f, new Color(1f, 0.92f, 0.35f, 0.95f));
                _ring.loop = true;
                _ring.positionCount = Segments;

                _dot = CreateLine("LockDot", 0.1f, new Color(1f, 0.78f, 0.2f, 1f));
                _dot.loop = true;
                _dot.positionCount = 12;
            }

            LineRenderer CreateLine(string name, float width, Color color)
            {
                var child = new GameObject(name);
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                line.sortingOrder = 100;
                line.startWidth = width;
                line.endWidth = width;
                BattleUnlitTint.Apply(line, color);
                line.enabled = false;
                return line;
            }

            void LateUpdate()
            {
                long id = SkillTargeting.AttackTargetId;
                if (id == 0 || !SkillTargeting.IsAliveTarget(id)
                    || !SkillTargeting.TryGetWorldPos(id, out Vector3 pos))
                {
                    Hide();
                    return;
                }

                pos.y = GameConstants.GroundY + GroundLift;
                DrawCircle(_ring, pos, RingRadius, Segments);
                DrawCircle(_dot, pos, DotRadius, 12);
            }

            static void DrawCircle(LineRenderer line, Vector3 center, float radius, int segments)
            {
                if (line == null) return;
                line.enabled = true;
                if (line.positionCount != segments)
                    line.positionCount = segments;
                for (int i = 0; i < segments; i++)
                {
                    float a = (Mathf.PI * 2f) * i / segments;
                    line.SetPosition(i, center + new Vector3(
                        Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }

            void Hide()
            {
                if (_ring != null) _ring.enabled = false;
                if (_dot != null) _dot.enabled = false;
            }
        }
    }
}
