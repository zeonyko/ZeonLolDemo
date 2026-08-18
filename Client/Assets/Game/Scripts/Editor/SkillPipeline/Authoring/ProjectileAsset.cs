using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.SkillAuthoring
{
    /// <summary>弹道编辑资产：怎么飞、碰到人、落地再炸、要不要铺地。</summary>
    [CreateAssetMenu(fileName = "Projectile_New", menuName = "Battle/Projectile Asset", order = 12)]
    public class ProjectileAsset : ScriptableObject
    {
        #region 基础飞行参数

        public int ProjectileId = 2000;
        public string ProjectileName = "新弹道";
        public string VfxKey = "";
        public float Width = 1f;
        public float Speed = 16f;
        public float MaxDistance = 10f;
        public int MaxTargets = 1;
        public int PierceCount = 1;
        public int BoltCount = 1;
        public float SpreadDegrees;
        public Color VfxColor = new Color(0.9f, 0.9f, 1f, 0.9f);
        public Vector2 VfxScale = new Vector2(0.4f, 0.4f);
        public float VfxYLift;

        #endregion

        #region 接触判定与落地表现

        [Header("碰到人")]
        [Tooltip("落地爆炸前等几秒")]
        public float ImpactDelay;
        [Tooltip("飞行碰撞形状。空则按宽度推一个球")]
        public HitShapeData CollisionShape = new HitShapeData
        {
            ShapeType = HitShapeType.Sphere,
            ShapeSize = new Vector3(0.4f, 0f, 0f)
        };
        [Tooltip("碰到人时打出的效果")]
        public HitEffectData HitEffect = new HitEffectData
        {
            Damage = new DamagePayloadData { Coefficient = 0.35f }
        };
        [Tooltip("碰到人时播什么反馈")]
        public HitFeedbackData HitFeedback = new HitFeedbackData();

        [Header("落地再炸")]
        public HitImpactData Impact = new HitImpactData();

        [Header("命中后铺地面区域")]
        [Tooltip("命中后是否在地上铺一块持续区域")]
        public bool SpawnAreaOnHit;
        public AreaSpawnData GroundArea = new AreaSpawnData();

        #endregion

        #region 导出给运行时
        /// <summary>导出弹道怎么飞、碰到人、落地怎么炸。</summary>
        public ProjectileConfig ToData()
        {
            var collision = CollisionShape ?? new HitShapeData
            {
                ShapeType = HitShapeType.Sphere,
                ShapeSize = new Vector3(Width * 0.5f, 0f, 0f)
            };
            return new ProjectileConfig
            {
                ProjectileId = ProjectileId,
                Name = ProjectileName ?? "",
                Width = Width,
                Speed = Speed,
                MaxDistance = MaxDistance,
                MaxTargets = MaxTargets > 0 ? MaxTargets : Mathf.Max(1, PierceCount),
                PierceCount = PierceCount > 0 ? PierceCount : 1,
                BoltCount = BoltCount > 0 ? BoltCount : 1,
                SpreadDegrees = SpreadDegrees,
                ImpactDelay = ImpactDelay,
                CollisionShape = new SkillShapePayload
                {
                    ShapeType = (int)collision.ShapeType,
                    SizeX = collision.ShapeSize.x,
                    SizeY = collision.ShapeSize.y,
                    SizeZ = collision.ShapeSize.z,
                    OffsetX = collision.Offset.x,
                    OffsetY = collision.Offset.y,
                    OffsetZ = collision.Offset.z
                },
                HitEffect = (HitEffect ?? new HitEffectData()).ToPayload(),
                HitFeedback = (HitFeedback ?? new HitFeedbackData()).ToPayload(),
                Impact = Impact != null ? Impact.ToPayload() : null,
                SpawnAreaOnHit = SpawnAreaOnHit,
                GroundArea = SpawnAreaOnHit && GroundArea != null && GroundArea.Enabled
                    ? ToGroundArea(GroundArea)
                    : null
            };
        }

        /// <summary>导出弹道客户端表现配置（VFX 颜色/缩放等）。</summary>
        public ProjectilePresentationConfig ToPresentation()
        {
            return new ProjectilePresentationConfig
            {
                ProjectileId = ProjectileId,
                VfxKey = string.IsNullOrEmpty(VfxKey) ? (ProjectileName ?? "") : VfxKey,
                VfxColorR = VfxColor.r,
                VfxColorG = VfxColor.g,
                VfxColorB = VfxColor.b,
                VfxColorA = VfxColor.a,
                VfxScaleX = VfxScale.x,
                VfxScaleY = VfxScale.y,
                VfxYLift = VfxYLift
            };
        }

        /// <summary>把地面区域编辑数据转成运行时区域数据。</summary>
        static SkillAreaPayload ToGroundArea(AreaSpawnData a)
        {
            var shape = a.Shape ?? new HitShapeData();
            return new SkillAreaPayload
            {
                Name = a.Name ?? "",
                Anchor = (a.Anchor ?? new TargetSelectorAuthoring
                {
                    SelectorType = TargetSelectorType.FixedWorldPos
                }).ToPayload(),
                Duration = a.Duration > 0.05f ? a.Duration : 6f,
                TickInterval = a.TickInterval > 0.05f ? a.TickInterval : 0.5f,
                ReplaceSame = a.ReplaceSame,
                Shape = new SkillShapePayload
                {
                    ShapeType = (int)shape.ShapeType,
                    SizeX = shape.ShapeSize.x,
                    SizeY = shape.ShapeSize.y,
                    SizeZ = shape.ShapeSize.z,
                    OffsetX = shape.Offset.x,
                    OffsetY = shape.Offset.y,
                    OffsetZ = shape.Offset.z
                },
                TickEffect = (a.TickEffect ?? new HitEffectData()).ToPayload(),
                TickFeedback = (a.TickFeedback ?? new HitFeedbackData()).ToPayload()
            };
        }

        #endregion

        #region 数据转换：从运行时数据反填

        /// <summary>从运行时配置反填编辑资产（含飞行/碰撞/落地/地面区域）。</summary>
        public void ApplyFromData(ProjectileConfig data, ProjectilePresentationConfig presentation = null)
        {
            if (data == null) return;
            ProjectileId = data.ProjectileId;
            ProjectileName = data.Name ?? "";
            Width = data.Width;
            Speed = data.Speed;
            MaxDistance = data.MaxDistance;
            MaxTargets = data.MaxTargets > 0 ? data.MaxTargets : 1;
            PierceCount = data.PierceCount > 0 ? data.PierceCount : 1;
            BoltCount = data.BoltCount > 0 ? data.BoltCount : 1;
            SpreadDegrees = data.SpreadDegrees;
            ImpactDelay = data.ImpactDelay;

            if (presentation != null)
            {
                VfxKey = presentation.VfxKey ?? "";
                VfxColor = new Color(presentation.VfxColorR, presentation.VfxColorG, presentation.VfxColorB, presentation.VfxColorA);
                VfxScale = new Vector2(
                    presentation.VfxScaleX > 0.01f ? presentation.VfxScaleX : 0.4f,
                    presentation.VfxScaleY > 0.01f ? presentation.VfxScaleY : 0.4f);
                VfxYLift = presentation.VfxYLift;
            }

            if (data.CollisionShape != null)
            {
                var s = data.CollisionShape;
                CollisionShape = new HitShapeData
                {
                    ShapeType = s.Type,
                    ShapeSize = new Vector3(s.SizeX, s.SizeY, s.SizeZ),
                    Offset = new Vector3(s.OffsetX, s.OffsetY, s.OffsetZ)
                };
            }
            else
            {
                CollisionShape = new HitShapeData
                {
                    ShapeType = HitShapeType.Sphere,
                    ShapeSize = new Vector3(Width * 0.5f, 0f, 0f)
                };
            }

            HitEffect = HitEffectData.FromPayload(
                data.HitEffect ?? new HitEffectPayload
                {
                    Damage = DamagePayload.DefaultInstant(0.35f)
                });
            HitFeedback = HitFeedbackData.FromPayload(data.HitFeedback);
            Impact ??= new HitImpactData();
            Impact.ApplyFromPayload(data.Impact);

            SpawnAreaOnHit = data.SpawnAreaOnHit;
            GroundArea = FromGroundArea(data.GroundArea);
        }

        /// <summary>把运行时区域数据反填成地面区域编辑数据。</summary>
        static AreaSpawnData FromGroundArea(SkillAreaPayload a)
        {
            if (a == null || !a.HasContent)
                return new AreaSpawnData { Enabled = false };
            var s = a.Shape;
            return new AreaSpawnData
            {
                Enabled = true,
                Name = a.Name ?? "",
                Anchor = TargetSelectorAuthoring.FromPayload(
                    a.Anchor ?? TargetSelectorData.FixedWorld()),
                Duration = a.Duration,
                TickInterval = a.TickInterval,
                ReplaceSame = a.ReplaceSame,
                Shape = s == null
                    ? new HitShapeData { ShapeType = HitShapeType.Sphere, ShapeSize = new Vector3(3.5f, 1f, 0f) }
                    : new HitShapeData
                    {
                        ShapeType = s.Type,
                        ShapeSize = new Vector3(s.SizeX, s.SizeY, s.SizeZ),
                        Offset = new Vector3(s.OffsetX, s.OffsetY, s.OffsetZ)
                    },
                TickEffect = HitEffectData.FromPayload(a.TickEffect),
                TickFeedback = HitFeedbackData.FromPayload(a.TickFeedback)
            };
        }

        #endregion
    }
}
