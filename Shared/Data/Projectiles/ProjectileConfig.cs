using System;

namespace Shared
{
    /// <summary>弹道落地后再按形状找人。</summary>
    [Serializable]
    public class ProjectileImpactPayload
    {
        /// <summary>受击反馈键。</summary>
        public string AttackType = "";
        /// <summary>施法者反馈键。</summary>
        public string CasterFeedbackType = "";
        /// <summary>落地后再按形状找人的列表。</summary>
        public SkillShapeHitPayload[] ShapeHits = Array.Empty<SkillShapeHitPayload>();

        /// <summary>落地后还有没有判定。</summary>
        public bool HasContent => ShapeHits != null && ShapeHits.Length > 0;

        /// <summary>转成技能命中数据，好复用按形状找人。</summary>
        public SkillHitPayload ToHitPayload()
        {
            return new SkillHitPayload
            {
                AttackType = AttackType ?? "",
                CasterFeedbackType = CasterFeedbackType ?? "",
                ShapeHits = ShapeHits ?? Array.Empty<SkillShapeHitPayload>(),
                ProjectileSpawns = Array.Empty<SkillProjectilePayload>(),
                AreaSpawns = Array.Empty<SkillAreaPayload>(),
                CasterBuffs = Array.Empty<BuffApplySpec>()
            };
        }
    }

    /// <summary>弹道怎么飞、碰到人怎么伤、落地再炸不炸。特效在表现表。没配碰撞球时用宽度当半径。</summary>
    [Serializable]
    public class ProjectileConfig
    {
        #region 飞行参数（服务器为准）

        public int ProjectileId;
        public string Name = "";
        /// <summary>宽度。没配碰撞球时当半径用。</summary>
        public float Width = 1f;
        /// <summary>飞行速度。</summary>
        public float Speed = 16f;
        /// <summary>最远飞多远；超出后消失。</summary>
        public float MaxDistance;
        /// <summary>一次最多打中几人。</summary>
        public int MaxTargets = 1;
        /// <summary>能穿几个人。</summary>
        public int PierceCount = 1;
        /// <summary>一次放出几发。</summary>
        public int BoltCount = 1;
        /// <summary>多发时散开的角度。</summary>
        public float SpreadDegrees;

        #endregion

        #region 碰到人 / 落地

        /// <summary>落地爆炸前等多久（秒）；0=立刻。</summary>
        public float ImpactDelay;
        /// <summary>飞行碰到人用的形状；没配则按宽度推球。</summary>
        public SkillShapePayload CollisionShape;
        /// <summary>碰到人时的伤害/Buff。</summary>
        public HitEffectPayload HitEffect;
        /// <summary>碰到人时的受击反馈。</summary>
        public HitFeedbackSignal HitFeedback;
        /// <summary>落地后再按形状找人。</summary>
        public ProjectileImpactPayload Impact;
        /// <summary>命中后是否在地上铺区域。</summary>
        public bool SpawnAreaOnHit;
        /// <summary>命中后铺的地面区域。</summary>
        public SkillAreaPayload GroundArea;

        #endregion

        #region 派生只读

        /// <summary>落地后还有没有判定。</summary>
        public bool HasImpact => Impact != null && Impact.HasContent;

        /// <summary>落地圈有多大。取第一个形状宽度，没有就用 1.5。</summary>
        public float ImpactRadius
        {
            get
            {
                if (!HasImpact || Impact.ShapeHits == null || Impact.ShapeHits.Length == 0)
                    return 0f;
                var mh = Impact.ShapeHits[0];
                var s = mh?.Shape;
                return s != null && s.SizeX > 0.1f ? s.SizeX : 1.5f;
            }
        }

        #endregion
    }

    /// <summary>运行时弹道表。缺文件会报错，不会偷偷塞默认弹。</summary>
    public static class ProjectileCatalog
    {
        static readonly IdCatalog<ProjectileConfig> Items = new IdCatalog<ProjectileConfig>(
            c => c.ProjectileId,
            Prepare);

        static void Prepare(ProjectileConfig data)
        {
            if (data.BoltCount < 1) data.BoltCount = 1;
            Normalize(data);
        }

        public static void RegisterOrReplace(ProjectileConfig data) => Items.RegisterOrReplace(data);

        /// <summary>缺的碰撞球、伤害、落地判定补上默认值。</summary>
        static void Normalize(ProjectileConfig data)
        {
            // 没配碰撞形状时，用宽度的一半当球半径。
            if (data.CollisionShape == null && data.Width > 0.01f)
            {
                data.CollisionShape = new SkillShapePayload
                {
                    ShapeType = (int)HitShapeType.Sphere,
                    SizeX = data.Width * 0.5f
                };
            }

            if (data.HitEffect == null)
                data.HitEffect = new HitEffectPayload { Damage = DamagePayload.DefaultInstant(1f) };
            data.HitEffect.TargetBuffs ??= Array.Empty<BuffApplySpec>();
            if (data.HitEffect.Damage == null)
                data.HitEffect.Damage = DamagePayload.DefaultInstant(1f);

            data.HitFeedback ??= new HitFeedbackSignal();

            if (data.Impact != null)
            {
                data.Impact.ShapeHits ??= Array.Empty<SkillShapeHitPayload>();
                for (int i = 0; i < data.Impact.ShapeHits.Length; i++)
                {
                    var mh = data.Impact.ShapeHits[i];
                    if (mh == null) continue;
                    mh.Anchor ??= TargetSelectorData.FixedWorld();
                    mh.Effect ??= new HitEffectPayload();
                    mh.Effect.TargetBuffs ??= Array.Empty<BuffApplySpec>();
                    if (mh.Effect.Damage == null)
                        mh.Effect.Damage = DamagePayload.DefaultInstant();
                    if (mh.Effect.Damage.Coefficient <= 0f)
                        mh.Effect.Damage.Coefficient = 1f;
                }
                if (!data.Impact.HasContent)
                    data.Impact = null;
            }

            if (data.SpawnAreaOnHit && data.GroundArea != null)
            {
                data.GroundArea.Anchor ??= TargetSelectorData.FixedWorld();
                if (data.GroundArea.TickInterval < 0.05f)
                    data.GroundArea.TickInterval = 0.5f;
                if (!data.GroundArea.HasContent)
                    data.GroundArea = null;
            }
        }

        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<ProjectileConfig> list) =>
            Items.ClearAndLoad(list);

        #region 查找

        public static ProjectileConfig Get(int id) => Items.Get(id);

        public static bool TryGet(int id, out ProjectileConfig data) => Items.TryGet(id, out data);

        /// <summary>取弹道落地后再扫的数据。</summary>
        public static bool TryGetImpact(int projectileId, out ProjectileImpactPayload impact)
        {
            impact = null;
            if (!TryGet(projectileId, out var cfg) || cfg == null || !cfg.HasImpact)
                return false;
            impact = cfg.Impact;
            return true;
        }

        public static System.Collections.Generic.IReadOnlyDictionary<int, ProjectileConfig> All => Items.All;

        #endregion
    }
}
