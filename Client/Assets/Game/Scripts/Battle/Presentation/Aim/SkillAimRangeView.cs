using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>常规施法指示：普攻圈、闪现圆+方向箭头、落点圈、近战形状预览。</summary>
    public static class SkillAimRangeView
    {
        #region 常量
        private const int CircleSegments = 48;
        private const int SectorArcSegments = 24;
        private const float GroundLift = 0.06f;
        private const float DirectionArrowWidth = 0.32f;
        private const float LandingPreviewRadius = 0.55f;
        #endregion

        #region 指示器形状定义
        private enum AimShape
        {
            CircleOnly = 0,
            Line = 1,
            Sector = 2,
            MotionArrow = 3,
            /// <summary>施法圈 + 落点小圈（Point）。</summary>
            CircleWithLanding = 4,
        }

        private struct IndicatorSpec
        {
            public int SkillId;
            public float Range;
            public AimShape Shape;
            public float Width;
            public float SpreadDegrees;
            /// <summary>大于 0 时额外画 CastRange 圈（普攻：圈 + 盒/箭）。</summary>
            public float CircleRange;
        }
        #endregion

        #region 公开开关
        public static bool ShowIndicator
        {
            get => Drawer.Enabled;
            set
            {
                Drawer.Enabled = value;
                if (value)
                    Drawer.Ensure();
            }
        }

        public static void EnsureDrawer() => Drawer.Ensure();

        /// <summary>该技能按住瞄准时会不会画出范围/箭头（供 PC 热键决定：松手确认还是同帧出手）。</summary>
        public static bool HasPreview(int skillId) => TryBuildSpec(skillId, out _);

        static SkillAimRangeView()
        {
            Drawer.Enabled = true;
        }
        #endregion

        #region 指示器规格构建（按技能配置逐类型推导形状/射程）
        /// <summary>按技能配置推导出指示器规格；分派到各类型的构建方法，无法确定形状/射程时返回 false。</summary>
        static bool TryBuildSpec(int skillId, out IndicatorSpec spec)
        {
            spec = default;
            if (!SkillCatalog.TryGet(skillId, out var def) || def == null)
                return false;

            // 1) 普攻：CastRange 圈，锁敌点另画
            if (def.IsAutoAttack)
                return TryBuildAutoAttackSpec(skillId, def, out spec);

            // 2) 闪现：位移距离圆 + 地面方向箭头
            if (def.ResolveMode == ESkillResolveMode.Blink)
                return TryBuildBlinkSpec(skillId, def, out spec);

            // 3) Point：施法圈 + 落点 AoE 预览
            if (def.TargetMode == TargetType.Point)
                return TryBuildPointSpec(skillId, def, out spec);

            // 4) Dash：方向箭头
            if (def.ResolveMode == ESkillResolveMode.Dash)
                return TryBuildDashSpec(skillId, def, out spec);

            bool? shapeResult = TryBuildShapePreviewSpec(skillId, def, out spec);
            if (shapeResult.HasValue)
                return shapeResult.Value;

            // 形状预览推不出时：按射程画圈或箭头
            return TryBuildDefaultRangeSpec(skillId, def, out spec);
        }

        /// <summary>普攻：只画 CastRange 圈，锁敌用脚下吸附点，不跟方向盒。</summary>
        static bool TryBuildAutoAttackSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            spec = default;
            float circle = SkillTargeting.GetRange(def);
            if (circle <= 0.05f) return false;

            spec = new IndicatorSpec
            {
                SkillId = skillId,
                Range = circle,
                Shape = AimShape.CircleOnly,
                CircleRange = circle
            };
            return true;
        }

        /// <summary>闪现：位移距离圆 + 地面方向箭头，避免误当成移动摇杆方向。</summary>
        static bool TryBuildBlinkSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            spec = default;
            if (def.NeedsLockTarget)
            {
                float lockRange = SkillTargeting.GetRange(def);
                if (lockRange <= 0.05f) return false;
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = lockRange,
                    Shape = AimShape.CircleOnly,
                    CircleRange = lockRange
                };
                return true;
            }

            float motion = def.MotionDistance > 0.05f ? def.MotionDistance : def.CastRange;
            if (motion <= 0.05f) return false;
            spec = new IndicatorSpec
            {
                SkillId = skillId,
                Range = motion,
                Shape = AimShape.MotionArrow,
                Width = DirectionArrowWidth,
                CircleRange = motion
            };
            return true;
        }

        /// <summary>Point：施法圈 + 落点 AoE 预览。</summary>
        static bool TryBuildPointSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            float range = SkillTargeting.GetRange(def);
            float landing = LandingPreviewRadius;
            if (SkillClipUtil.TryGetPrimaryShape(def.Clips, out var pointShape)
                && pointShape != null
                && pointShape.Type == HitShapeType.Sphere
                && pointShape.SizeX > 0.05f)
            {
                landing = pointShape.SizeX;
            }

            spec = new IndicatorSpec
            {
                SkillId = skillId,
                Range = range,
                Shape = AimShape.CircleWithLanding,
                Width = landing
            };
            return true;
        }

        /// <summary>Dash：方向箭头。</summary>
        static bool TryBuildDashSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            spec = default;
            if (def.NeedsLockTarget)
            {
                float lockRange = SkillTargeting.GetRange(def);
                if (lockRange <= 0.05f) return false;
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = lockRange,
                    Shape = AimShape.CircleOnly,
                    CircleRange = lockRange
                };
                return true;
            }

            float motion = def.MotionDistance;
            if (motion <= 0.05f) return false;
            spec = new IndicatorSpec
            {
                SkillId = skillId,
                Range = motion,
                Shape = AimShape.MotionArrow,
                Width = DirectionArrowWidth
            };
            return true;
        }

        /// <summary>按主形状推出扇/盒/圆预览。没配形状就返回空，交给调用方用射程圈。动态目标用搜索半径当射程，别把爆裂半径当指示器。</summary>
        static bool? TryBuildShapePreviewSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            spec = default;
            if (SkillClipUtil.TryGetPrimaryShapeHit(def.Clips, out var shapeHit)
                && shapeHit?.Shape != null)
            {
                var shape = shapeHit.Shape;
                var anchor = shapeHit.Anchor;
                bool dynamicSearch = anchor != null
                    && anchor.Type == TargetSelectorType.DynamicTargets
                    && anchor.SearchRadius > 0.05f;

                switch (shape.Type)
                {
                    case HitShapeType.Sector:
                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.Sector,
                            Width = DirectionArrowWidth,
                            SpreadDegrees = shape.SizeY > 0.5f ? shape.SizeY : 90f
                        };
                        return spec.Range > 0.05f;

                    case HitShapeType.Box:
                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.Line,
                            Width = shape.SizeY > 0.05f ? shape.SizeY : DirectionArrowWidth
                        };
                        return spec.Range > 0.05f;

                    case HitShapeType.Sphere:
                        if (dynamicSearch)
                        {
                            float search = anchor.SearchRadius;
                            if (def.CastRange > search) search = def.CastRange;
                            float aoe = shape.SizeX > 0.05f ? shape.SizeX : LandingPreviewRadius;
                            spec = new IndicatorSpec
                            {
                                SkillId = skillId,
                                Range = search,
                                Shape = AimShape.CircleWithLanding,
                                Width = aoe
                            };
                            return true;
                        }

                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.CircleOnly
                        };
                        return spec.Range > 0.05f;
                }
            }
            else if (SkillClipUtil.TryGetPrimaryShape(def.Clips, out var shape) && shape != null)
            {
                // 没有 ShapeHit 包装时（如 AreaSpawn）按 clip 形状画
                switch (shape.Type)
                {
                    case HitShapeType.Sector:
                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.Sector,
                            Width = DirectionArrowWidth,
                            SpreadDegrees = shape.SizeY > 0.5f ? shape.SizeY : 90f
                        };
                        return spec.Range > 0.05f;
                    case HitShapeType.Box:
                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.Line,
                            Width = shape.SizeY > 0.05f ? shape.SizeY : DirectionArrowWidth
                        };
                        return spec.Range > 0.05f;
                    case HitShapeType.Sphere:
                        spec = new IndicatorSpec
                        {
                            SkillId = skillId,
                            Range = shape.SizeX > 0.05f ? shape.SizeX : def.CastRange,
                            Shape = AimShape.CircleOnly
                        };
                        return spec.Range > 0.05f;
                }
            }

            return null;
        }

        static bool TryBuildDefaultRangeSpec(int skillId, SkillConfig def, out IndicatorSpec spec)
        {
            spec = default;
            float rangeLen = SkillTargeting.GetRange(def);
            ProjectileConfig proj = null;
            if (ProjectileCatalog.TryGet(def.ProjectileId, out var p) && p != null)
            {
                proj = p;
                if (proj.MaxDistance > rangeLen)
                    rangeLen = proj.MaxDistance;
            }
            if (rangeLen <= 0.05f) return false;

            if (def.TargetMode == TargetType.Self)
            {
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = rangeLen > 0.05f ? rangeLen : 1f,
                    Shape = AimShape.CircleOnly
                };
                return true;
            }

            // 多弹道扇形仍用扇形预览
            if (def.ResolveMode == ESkillResolveMode.SkillshotLine
                && proj != null
                && proj.BoltCount > 1
                && proj.SpreadDegrees > 0.5f)
            {
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = rangeLen,
                    Shape = AimShape.Sector,
                    Width = DirectionArrowWidth,
                    SpreadDegrees = proj.SpreadDegrees
                };
                return true;
            }

            // 6) Direction 弹道/直线：多穿透用宽线，其余用细箭头
            if (SkillRules.NeedsAimDirection(def) || def.IsProjectile
                || def.TargetMode == TargetType.Direction)
            {
                bool piercing = proj != null
                    && proj.BoltCount <= 1
                    && (proj.MaxTargets > 1 || proj.PierceCount > 1);
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = rangeLen,
                    Shape = piercing ? AimShape.Line : AimShape.MotionArrow,
                    Width = piercing && proj.Width > 0.05f ? proj.Width : DirectionArrowWidth
                };
                return true;
            }

            // 锁定技：施法圈
            if (def.TargetMode == TargetType.SingleTarget)
            {
                spec = new IndicatorSpec
                {
                    SkillId = skillId,
                    Range = rangeLen,
                    Shape = AimShape.CircleOnly
                };
                return true;
            }

            spec = new IndicatorSpec
            {
                SkillId = skillId,
                Range = rangeLen,
                Shape = AimShape.CircleOnly
            };
            return true;
        }
        #endregion

        /// <summary>每帧按当前蓄力技能重画范围圈。</summary>
        private sealed class Drawer : MonoBehaviour
        {
            #region 字段
            public static bool Enabled;

            private static Drawer _instance;
            private LineRenderer _circle;  // 施法圈/普攻圈/范围圈
            private LineRenderer _aim;     // 方向箭头/扇形/矩形等瞄准线
            private LineRenderer _landing; // Point 落点小圈
            #endregion

            #region 初始化
            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("SkillAimRangeViewDrawer");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Drawer>();
                _instance.Build();
            }

            private void Build()
            {
                _circle = CreateLine("Circle", 0.08f, new Color(0.35f, 0.85f, 1f, 0.9f));
                _circle.loop = true;
                _circle.positionCount = CircleSegments;

                _aim = CreateLine("Aim", 0.06f, new Color(1f, 0.85f, 0.25f, 0.85f));
                _aim.loop = false;
                _aim.positionCount = 2;

                _landing = CreateLine("Landing", 0.07f, new Color(1f, 0.55f, 0.2f, 0.9f));
                _landing.loop = true;
                _landing.positionCount = CircleSegments;
            }

            private LineRenderer CreateLine(string name, float width, Color color)
            {
                var child = new GameObject(name);
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                line.sortingOrder = 99;
                line.startWidth = width;
                line.endWidth = width;
                BattleUnlitTint.Apply(line, color);
                line.enabled = false;
                return line;
            }
            #endregion

            #region 每帧刷新
            private void LateUpdate()
            {
                if (_circle == null || _aim == null) return;

                if (!Enabled || !TryResolveHeldSpec(out var spec))
                {
                    Hide();
                    return;
                }

                var battle = BattleSystem.Instance;
                if (battle == null || battle.LocalPlayerId == 0)
                {
                    Hide();
                    return;
                }

                var entity = EntityManager.Instance?.GetEntity(battle.LocalPlayerId);
                var t = entity?.GetComponent<TransformComponent>();
                if (t == null)
                {
                    Hide();
                    return;
                }

                Vector3 origin = t.Position;
                origin.y = GameConstants.GroundY + GroundLift;

                Vector3 dir = ResolveAimDir(entity);

                switch (spec.Shape)
                {
                    case AimShape.CircleOnly:
                        DrawCircle(_circle, origin, spec.Range);
                        _aim.enabled = false;
                        if (_landing != null) _landing.enabled = false;
                        break;
                    case AimShape.CircleWithLanding:
                        DrawCircle(_circle, origin, spec.Range);
                        _aim.enabled = false;
                        DrawLandingPreview(origin, dir, spec.Range,
                            spec.Width > 0.05f ? spec.Width : LandingPreviewRadius);
                        break;
                    case AimShape.Line:
                        DrawOptionalRangeCircle(origin, spec);
                        if (_landing != null) _landing.enabled = false;
                        DrawLineBeam(origin, dir, spec.Range, spec.Width);
                        break;
                    case AimShape.Sector:
                        DrawOptionalRangeCircle(origin, spec);
                        if (_landing != null) _landing.enabled = false;
                        DrawSector(origin, dir, spec.Range, spec.SpreadDegrees);
                        break;
                    case AimShape.MotionArrow:
                        DrawOptionalRangeCircle(origin, spec);
                        if (_landing != null) _landing.enabled = false;
                        DrawMotionArrow(origin, dir, spec.Range);
                        break;
                    default:
                        Hide();
                        break;
                }
            }

            private static bool TryResolveHeldSpec(out IndicatorSpec spec)
            {
                spec = default;
                int aimId = PlayerInput.ActiveAimSkillId;
                if (aimId > 0 && TryBuildSpec(aimId, out spec))
                    return true;
                return false;
            }

            private static Vector3 ResolveAimDir(Entity entity)
            {
                Vector3 facing = Vector3.forward;
                var viewGo = entity.GetComponent<ViewComponent>()?.ViewGameObject;
                if (viewGo != null)
                {
                    facing = viewGo.transform.forward;
                    facing.y = 0f;
                    if (facing.sqrMagnitude > 0.0001f)
                        facing.Normalize();
                    else
                        facing = Vector3.forward;
                }

                Vector3 dir = AimAssist.Resolve(Vector3.zero, facing, Camera.main);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                    return facing;
                return dir.normalized;
            }
            #endregion

            #region 绘制方法
            private void DrawLandingPreview(Vector3 origin, Vector3 dir, float castRange, float landingRadius)
            {
                if (_landing == null) return;
                Vector3 landing = origin + dir * castRange;
                var frame = PlayerInput.Frame;
                if (frame.HasAimPoint)
                {
                    Vector3 hit = frame.AimPoint;
                    Vector3 delta = hit - origin;
                    delta.y = 0f;
                    float dist = delta.magnitude;
                    if (dist > castRange && dist > 0.0001f)
                        hit = origin + delta * (castRange / dist);
                    hit.y = origin.y;
                    landing = hit;
                }
                else
                {
                    float mag = frame.AimStickMag;
                    if (mag < 0.12f) mag = 1f;
                    landing = origin + dir * (castRange * mag);
                }

                landing.y = origin.y;
                DrawCircle(_landing, landing, landingRadius);
            }

            private void DrawOptionalRangeCircle(Vector3 origin, IndicatorSpec spec)
            {
                if (spec.CircleRange > 0.05f)
                    DrawCircle(_circle, origin, spec.CircleRange);
                else
                    _circle.enabled = false;
            }

            private static void DrawCircle(LineRenderer line, Vector3 center, float radius)
            {
                line.enabled = true;
                for (int i = 0; i < CircleSegments; i++)
                {
                    float a = (Mathf.PI * 2f) * i / CircleSegments;
                    line.SetPosition(i, center + new Vector3(
                        Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }

            private void DrawLineBeam(Vector3 origin, Vector3 dir, float length, float width)
            {
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.right;
                else
                    right.Normalize();

                float half = Mathf.Max(0.15f, width * 0.5f);
                Vector3 tip = origin + dir * length;
                Vector3 r = right * half;

                _aim.loop = true;
                _aim.positionCount = 5;
                _aim.enabled = true;
                _aim.SetPosition(0, origin - r);
                _aim.SetPosition(1, tip - r);
                _aim.SetPosition(2, tip + r);
                _aim.SetPosition(3, origin + r);
                _aim.SetPosition(4, origin - r);
            }

            private void DrawSector(Vector3 origin, Vector3 dir, float length, float spreadDegrees)
            {
                float half = spreadDegrees * 0.5f * Mathf.Deg2Rad;
                float centerYaw = Mathf.Atan2(dir.x, dir.z);
                int arc = SectorArcSegments;
                int count = arc + 3;
                _aim.loop = true;
                _aim.positionCount = count;
                _aim.enabled = true;

                _aim.SetPosition(0, origin);
                for (int i = 0; i <= arc; i++)
                {
                    float t = i / (float)arc;
                    float yaw = centerYaw - half + (half * 2f) * t;
                    Vector3 edge = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                    _aim.SetPosition(1 + i, origin + edge * length);
                }

                _aim.SetPosition(count - 1, origin);
            }

            private void DrawMotionArrow(Vector3 origin, Vector3 dir, float length)
            {
                Vector3 tip = origin + dir * length;
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.right;
                else
                    right.Normalize();

                float head = Mathf.Clamp(length * 0.18f, 0.35f, 0.9f);
                Vector3 back = tip - dir * head;

                _aim.loop = false;
                _aim.positionCount = 6;
                _aim.startWidth = 0.05f;
                _aim.endWidth = 0.05f;
                _aim.enabled = true;
                _aim.SetPosition(0, origin);
                _aim.SetPosition(1, tip);
                _aim.SetPosition(2, back + right * head * 0.55f);
                _aim.SetPosition(3, tip);
                _aim.SetPosition(4, back - right * head * 0.55f);
                _aim.SetPosition(5, tip);
            }

            private void Hide()
            {
                if (_circle != null) _circle.enabled = false;
                if (_aim != null) _aim.enabled = false;
                if (_landing != null) _landing.enabled = false;
            }
            #endregion
        }
    }
}
