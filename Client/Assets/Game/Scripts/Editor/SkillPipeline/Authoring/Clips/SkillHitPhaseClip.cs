using System;
using System.Collections.Generic;
using Shared;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    #region 基础数据结构：形状 / 伤害 / 目标选择

    /// <summary>形状：方块、圆球、扇形。</summary>
    [Serializable]
    public class HitShapeData
    {
        [Tooltip("形状：方块 / 圆球 / 扇形")]
        public HitShapeType ShapeType = HitShapeType.Box;
        [Tooltip("尺寸。圆球时 X 填半径")]
        public Vector3 ShapeSize = Vector3.one;
        [Tooltip("相对落点的偏移")]
        public Vector3 Offset;
    }

    /// <summary>伤害数据：基础伤害、系数、类型。</summary>
    [Serializable]
    public class DamagePayloadData
    {
        [Tooltip("基础伤害。填 0 则用技能表")]
        public float BaseDamage;
        [Tooltip("伤害倍率。Demo 可不管")]
        public float Coefficient = 1f;
        [Tooltip("伤害类型")]
        public DamageType DamageType = DamageType.Physical;

        public DamagePayload ToPayload()
        {
            return new DamagePayload
            {
                BaseDamage = BaseDamage,
                Coefficient = Coefficient > 0f ? Coefficient : 1f,
                DamageType = (int)DamageType
            };
        }

        public static DamagePayloadData FromPayload(DamagePayload p)
        {
            if (p == null)
                return new DamagePayloadData();
            return new DamagePayloadData
            {
                BaseDamage = p.BaseDamage,
                Coefficient = p.Coefficient > 0f ? p.Coefficient : 1f,
                DamageType = p.Type
            };
        }
    }

    /// <summary>从哪选落点、看哪边、偏移多少。</summary>
    [Serializable]
    public class TargetSelectorAuthoring
    {
        [Tooltip("落点怎么选：自己 / 动态搜人 / 固定坐标 / 血最少的队友")]
        public TargetSelectorType SelectorType = TargetSelectorType.CasterSelf;
        [Tooltip("动态搜人时看哪边：敌/友/自己")]
        public TargetRelation Relation = TargetRelation.Enemy;
        [Tooltip("搜索半径")]
        public float SearchRadius;
        [Tooltip("最多选几个")]
        public int MaxTargetCount = 1;
        [Tooltip("位置偏移")]
        public Vector3 PositionOffset;
        [Tooltip("旋转偏移（Y 是左右转）")]
        public Vector3 RotationOffset;

        public TargetSelectorData ToPayload()
        {
            return new TargetSelectorData
            {
                SelectorType = (int)SelectorType,
                Relation = (int)Relation,
                SearchRadius = SearchRadius,
                MaxTargetCount = MaxTargetCount > 0 ? MaxTargetCount : 1,
                PositionOffsetX = PositionOffset.x,
                PositionOffsetY = PositionOffset.y,
                PositionOffsetZ = PositionOffset.z,
                RotationOffsetX = RotationOffset.x,
                RotationOffsetY = RotationOffset.y,
                RotationOffsetZ = RotationOffset.z
            };
        }

        public static TargetSelectorAuthoring FromPayload(TargetSelectorData d)
        {
            if (d == null)
                return new TargetSelectorAuthoring();
            return new TargetSelectorAuthoring
            {
                SelectorType = d.Type,
                Relation = d.TargetRelation,
                SearchRadius = d.SearchRadius,
                MaxTargetCount = d.MaxTargetCount > 0 ? d.MaxTargetCount : 1,
                PositionOffset = new Vector3(d.PositionOffsetX, d.PositionOffsetY, d.PositionOffsetZ),
                RotationOffset = new Vector3(d.RotationOffsetX, d.RotationOffsetY, d.RotationOffsetZ)
            };
        }
    }

    #endregion

    #region 反馈与命中效果数据

    /// <summary>受击动作、自己这边播什么。</summary>
    [Serializable]
    public class HitFeedbackData
    {
        [Tooltip("受击动作用哪套")]
        public string AttackType = "";
        [Tooltip("自己这边播哪套反馈")]
        public string CasterFeedbackType = "";

        public HitFeedbackSignal ToPayload()
        {
            return new HitFeedbackSignal
            {
                AttackType = AttackType ?? "",
                CasterFeedbackType = CasterFeedbackType ?? ""
            };
        }

        public static HitFeedbackData FromPayload(HitFeedbackSignal s)
        {
            if (s == null) return new HitFeedbackData();
            return new HitFeedbackData
            {
                AttackType = s.AttackType ?? "",
                CasterFeedbackType = s.CasterFeedbackType ?? ""
            };
        }
    }

    /// <summary>命中效果数据：伤害 + 附加 Buff + 击退。</summary>
    [Serializable]
    public class HitEffectData
    {
        [Tooltip("打出的伤害")]
        public DamagePayloadData Damage = new DamagePayloadData();
        [Tooltip("打中后给目标套哪些 Buff")]
        public List<BuffApplyData> TargetBuffs = new List<BuffApplyData>();
        [Tooltip("击退距离（米）。0=不击退")]
        public float KnockbackDistance;

        public HitEffectPayload ToPayload()
        {
            return new HitEffectPayload
            {
                Damage = (Damage ?? new DamagePayloadData()).ToPayload(),
                TargetBuffs = SkillHitPhaseClip.ToBuffPayloads(TargetBuffs, forceSelf: false),
                KnockbackDistance = KnockbackDistance > 0f ? KnockbackDistance : 0f
            };
        }

        public static HitEffectData FromPayload(HitEffectPayload p)
        {
            if (p == null) return new HitEffectData();
            return new HitEffectData
            {
                Damage = DamagePayloadData.FromPayload(p.Damage),
                TargetBuffs = SkillHitPhaseClip.FromBuffPayloads(p.TargetBuffs),
                KnockbackDistance = p.KnockbackDistance
            };
        }
    }

    #endregion

    #region 弹道生成与 Buff 施加数据

    /// <summary>弹道：用哪条、从哪放、散射多少。</summary>
    [Serializable]
    public class ProjectileSpawnData
    {
        [Tooltip("用哪条弹道配置")]
        public int ProjectileId;
        [Tooltip("从哪放出去")]
        public TargetSelectorAuthoring Anchor = new TargetSelectorAuthoring();
        [Tooltip("散射角度")]
        public Vector3 AngleOffset;
    }

    /// <summary>套 Buff：给谁、概率、要不要已有状态。</summary>
    [Serializable]
    public class BuffApplyData
    {
        [Tooltip("用哪条 Buff 模板")]
        public int BuffId;
        [Tooltip("套给谁：敌/友/自己")]
        public TargetRelation Relation = TargetRelation.Enemy;
        [Tooltip("目标必须已有这些状态。0=不要求")]
        public int RequireStatusFlags;
        [Range(0f, 1f)]
        [Tooltip("套上的概率")]
        public float Chance = 1f;
        [Tooltip("覆盖持续时间。0=用模板默认")]
        public float OverrideDuration;
    }

    #endregion

    #region 形状判定与区域生成数据

    /// <summary>按形状找人：落点 + 形状 + 效果 + 反馈。</summary>
    [Serializable]
    public class ShapeHitData
    {
        [Tooltip("调试名")]
        public string Name = "";
        [Tooltip("从哪开始判定")]
        public TargetSelectorAuthoring Anchor = new TargetSelectorAuthoring();
        [Tooltip("判定形状")]
        public HitShapeData Shape = new HitShapeData();
        [Tooltip("打中后做什么")]
        public HitEffectData Effect = new HitEffectData();
        [Tooltip("本框反馈。空则用本段默认")]
        public HitFeedbackData Feedback = new HitFeedbackData();
    }

    /// <summary>地上持续区域：多久消失、隔多久打一次。</summary>
    [Serializable]
    public class AreaSpawnData
    {
        [Tooltip("是否启用这块地面区域")]
        public bool Enabled;
        [Tooltip("调试名")]
        public string Name = "";
        [Tooltip("生成时固定落点")]
        public TargetSelectorAuthoring Anchor = new TargetSelectorAuthoring
        {
            SelectorType = TargetSelectorType.FixedWorldPos
        };
        [Tooltip("持续秒数")]
        public float Duration = 6f;
        [Tooltip("隔多久打一次人")]
        public float TickInterval = 0.5f;
        [Tooltip("同一个人同技能再放，替换旧的")]
        public bool ReplaceSame = true;
        [Tooltip("每次打人用的形状")]
        public HitShapeData Shape = new HitShapeData
        {
            ShapeType = HitShapeType.Sphere,
            ShapeSize = new Vector3(3.5f, 1f, 0f)
        };
        [Tooltip("每次打中做什么")]
        public HitEffectData TickEffect = new HitEffectData
        {
            Damage = new DamagePayloadData { Coefficient = 0.55f }
        };
        [Tooltip("每次打中播什么反馈")]
        public HitFeedbackData TickFeedback = new HitFeedbackData();
    }

    #endregion

    #region 弹道落地二次判定数据

    /// <summary>弹道落地后再炸一次。</summary>
    [Serializable]
    public class HitImpactData
    {
        [Tooltip("落地后受击动作用哪套")]
        public string AttackType = AttackStyles.HeavyBlow;
        [Tooltip("落地后自己播哪套反馈")]
        public string CasterFeedbackType = "";
        [Tooltip("落地后再按形状找人")]
        public List<ShapeHitData> ShapeHits = new List<ShapeHitData>();

        public bool HasContent => ShapeHits != null && ShapeHits.Count > 0;

        public ProjectileImpactPayload ToPayload()
        {
            if (!HasContent) return null;
            return new ProjectileImpactPayload
            {
                AttackType = AttackType ?? "",
                CasterFeedbackType = CasterFeedbackType ?? "",
                ShapeHits = SkillHitPhaseClip.ToShapePayloads(ShapeHits)
            };
        }

        public void ApplyFromPayload(ProjectileImpactPayload impact)
        {
            AttackType = AttackStyles.HeavyBlow;
            CasterFeedbackType = "";
            ShapeHits = new List<ShapeHitData>();
            if (impact == null || !impact.HasContent) return;
            AttackType = impact.AttackType ?? AttackStyles.HeavyBlow;
            CasterFeedbackType = impact.CasterFeedbackType ?? "";
            ShapeHits = SkillHitPhaseClip.FromShapePayloads(impact.ShapeHits);
        }
    }

    #endregion

    /// <summary>逻辑轨「判定」片段：按形状找人、出弹道、出区域、给自己上 Buff，可同时有。</summary>
    [Serializable]
    public class SkillHitPhaseClip : PlayableAsset, ITimelineClipAsset
    {
        #region Inspector 字段

        [Header("本段默认反馈（下面没填就用这个）")]
        [Tooltip("本段默认受击动作")]
        public string AttackType = AttackStyles.HeavySlash;
        [Tooltip("本段默认自己这边的反馈")]
        public string CasterFeedbackType = CasterReactionIds.HeavyImpact;

        [Header("形状判定（瞬间按形状找人）")]
        public List<ShapeHitData> ShapeHits = new List<ShapeHitData>();

        [Header("弹道生成")]
        public List<ProjectileSpawnData> ProjectileSpawns = new List<ProjectileSpawnData>();

        [Header("地面区域")]
        public List<AreaSpawnData> AreaSpawns = new List<AreaSpawnData>();

        [Header("给自己套 Buff")]
        public List<BuffApplyData> CasterBuffs = new List<BuffApplyData>();

        public ClipCaps clipCaps => ClipCaps.None;

        #endregion

        #region Playable 接口实现

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return Playable.Create(graph);
        }

        #endregion

        #region 写成运行时数据 / 从配置读回来

        /// <summary>导出给运行时用。</summary>
        public SkillHitPayload ToPayload()
        {
            return new SkillHitPayload
            {
                AttackType = AttackType ?? "",
                CasterFeedbackType = CasterFeedbackType ?? "",
                ShapeHits = ToShapePayloads(ShapeHits),
                ProjectileSpawns = ToProjectilePayloads(ProjectileSpawns),
                AreaSpawns = ToAreaPayloads(AreaSpawns),
                CasterBuffs = ToBuffPayloads(CasterBuffs, forceSelf: true)
            };
        }

        /// <summary>从运行时数据反填编辑数据。</summary>
        public void ApplyFromPayload(SkillHitPayload hit)
        {
            if (hit == null) return;
            AttackType = hit.AttackType ?? AttackStyles.LightSlash;
            CasterFeedbackType = hit.CasterFeedbackType ?? "";
            ShapeHits = FromShapePayloads(hit.ShapeHits);
            ProjectileSpawns = FromProjectilePayloads(hit.ProjectileSpawns);
            AreaSpawns = FromAreaPayloads(hit.AreaSpawns);
            CasterBuffs = FromBuffPayloads(hit.CasterBuffs);
        }

        /// <summary>是否存在至少一个启用的区域生成配置。</summary>
        public bool HasAnyAreaEnabled
        {
            get
            {
                if (AreaSpawns == null) return false;
                for (int i = 0; i < AreaSpawns.Count; i++)
                {
                    if (AreaSpawns[i] != null && AreaSpawns[i].Enabled)
                        return true;
                }
                return false;
            }
        }

        #endregion

        #region 区域生成数据转换

        static SkillAreaPayload[] ToAreaPayloads(List<AreaSpawnData> list)
        {
            if (list == null || list.Count == 0)
                return Array.Empty<SkillAreaPayload>();
            var tmp = new List<SkillAreaPayload>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || !a.Enabled) continue;
                var shape = a.Shape ?? new HitShapeData();
                tmp.Add(new SkillAreaPayload
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
                });
            }
            return tmp.Count == 0 ? Array.Empty<SkillAreaPayload>() : tmp.ToArray();
        }

        static List<AreaSpawnData> FromAreaPayloads(SkillAreaPayload[] arr)
        {
            var list = new List<AreaSpawnData>();
            if (arr == null) return list;
            for (int i = 0; i < arr.Length; i++)
            {
                var a = arr[i];
                if (a == null || !a.HasContent) continue;
                var s = a.Shape;
                list.Add(new AreaSpawnData
                {
                    Enabled = true,
                    Name = a.Name ?? "",
                    Anchor = TargetSelectorAuthoring.FromPayload(
                        a.Anchor ?? TargetSelectorData.FixedWorld()),
                    Duration = a.Duration,
                    TickInterval = a.TickInterval,
                    ReplaceSame = a.ReplaceSame,
                    Shape = s == null
                        ? new HitShapeData
                        {
                            ShapeType = HitShapeType.Sphere,
                            ShapeSize = new Vector3(3.5f, 1f, 0f)
                        }
                        : new HitShapeData
                        {
                            ShapeType = s.Type,
                            ShapeSize = new Vector3(s.SizeX, s.SizeY, s.SizeZ),
                            Offset = new Vector3(s.OffsetX, s.OffsetY, s.OffsetZ)
                        },
                    TickEffect = HitEffectData.FromPayload(a.TickEffect),
                    TickFeedback = HitFeedbackData.FromPayload(a.TickFeedback)
                });
            }
            return list;
        }

        #endregion

        #region 形状判定数据转换

        public static SkillShapeHitPayload[] ToShapePayloads(List<ShapeHitData> list)
        {
            if (list == null || list.Count == 0)
                return Array.Empty<SkillShapeHitPayload>();
            var arr = new SkillShapeHitPayload[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i] ?? new ShapeHitData();
                var s = m.Shape ?? new HitShapeData();
                arr[i] = new SkillShapeHitPayload
                {
                    Name = m.Name ?? "",
                    Anchor = (m.Anchor ?? new TargetSelectorAuthoring()).ToPayload(),
                    Shape = new SkillShapePayload
                    {
                        ShapeType = (int)s.ShapeType,
                        SizeX = s.ShapeSize.x,
                        SizeY = s.ShapeSize.y,
                        SizeZ = s.ShapeSize.z,
                        OffsetX = s.Offset.x,
                        OffsetY = s.Offset.y,
                        OffsetZ = s.Offset.z
                    },
                    Effect = (m.Effect ?? new HitEffectData()).ToPayload(),
                    Feedback = (m.Feedback ?? new HitFeedbackData()).ToPayload()
                };
            }
            return arr;
        }

        public static List<ShapeHitData> FromShapePayloads(SkillShapeHitPayload[] arr)
        {
            var list = new List<ShapeHitData>();
            if (arr == null) return list;
            for (int i = 0; i < arr.Length; i++)
            {
                var mh = arr[i];
                if (mh == null) continue;
                var s = mh.Shape;
                list.Add(new ShapeHitData
                {
                    Name = mh.Name ?? "",
                    Anchor = TargetSelectorAuthoring.FromPayload(
                        mh.Anchor ?? TargetSelectorData.CasterSelf()),
                    Shape = s == null
                        ? new HitShapeData()
                        : new HitShapeData
                        {
                            ShapeType = s.Type,
                            ShapeSize = new Vector3(s.SizeX, s.SizeY, s.SizeZ),
                            Offset = new Vector3(s.OffsetX, s.OffsetY, s.OffsetZ)
                        },
                    Effect = HitEffectData.FromPayload(mh.Effect),
                    Feedback = HitFeedbackData.FromPayload(mh.Feedback)
                });
            }
            return list;
        }

        #endregion

        #region 弹道生成数据转换

        static SkillProjectilePayload[] ToProjectilePayloads(List<ProjectileSpawnData> list)
        {
            if (list == null || list.Count == 0)
                return Array.Empty<SkillProjectilePayload>();
            var arr = new SkillProjectilePayload[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i] ?? new ProjectileSpawnData();
                arr[i] = new SkillProjectilePayload
                {
                    ProjectileId = p.ProjectileId,
                    Anchor = (p.Anchor ?? new TargetSelectorAuthoring()).ToPayload(),
                    AngleOffsetX = p.AngleOffset.x,
                    AngleOffsetY = p.AngleOffset.y,
                    AngleOffsetZ = p.AngleOffset.z
                };
            }
            return arr;
        }

        static List<ProjectileSpawnData> FromProjectilePayloads(SkillProjectilePayload[] arr)
        {
            var list = new List<ProjectileSpawnData>();
            if (arr == null) return list;
            for (int i = 0; i < arr.Length; i++)
            {
                var p = arr[i];
                if (p == null) continue;
                list.Add(new ProjectileSpawnData
                {
                    ProjectileId = p.ProjectileId,
                    Anchor = TargetSelectorAuthoring.FromPayload(
                        p.Anchor ?? TargetSelectorData.CasterSelf()),
                    AngleOffset = new Vector3(p.AngleOffsetX, p.AngleOffsetY, p.AngleOffsetZ)
                });
            }
            return list;
        }

        #endregion

        #region Buff 施加数据转换

        public static BuffApplySpec[] ToBuffPayloads(List<BuffApplyData> list, bool forceSelf)
        {
            if (list == null || list.Count == 0)
                return Array.Empty<BuffApplySpec>();
            var arr = new BuffApplySpec[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i] ?? new BuffApplyData();
                arr[i] = new BuffApplySpec
                {
                    BuffId = b.BuffId,
                    Chance = b.Chance,
                    Relation = forceSelf ? (int)TargetRelation.Self : (int)b.Relation,
                    RequireStatusFlags = b.RequireStatusFlags,
                    OverrideDuration = b.OverrideDuration
                };
            }
            return arr;
        }

        public static List<BuffApplyData> FromBuffPayloads(BuffApplySpec[] arr)
        {
            var list = new List<BuffApplyData>();
            if (arr == null) return list;
            for (int i = 0; i < arr.Length; i++)
            {
                var b = arr[i];
                if (b == null) continue;
                list.Add(new BuffApplyData
                {
                    BuffId = b.BuffId,
                    Chance = b.Chance,
                    Relation = b.TargetRelation,
                    RequireStatusFlags = b.RequireStatusFlags,
                    OverrideDuration = b.OverrideDuration
                });
            }
            return list;
        }

        #endregion
    }
}
