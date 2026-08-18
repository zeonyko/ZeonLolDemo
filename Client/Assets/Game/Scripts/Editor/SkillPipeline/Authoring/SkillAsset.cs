using Shared;
using UnityEngine;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>技能编辑资产：名字、条件、消耗、时间轴引用。</summary>
    [CreateAssetMenu(fileName = "Skill_New", menuName = "Battle/Skill Asset", order = 10)]
    public class SkillAsset : ScriptableObject
    {
        [Header("--- 1. 基础信息 ---")]
        public int SkillId = 2000;
        public string SkillName = "NewSkill";
        public SkillType Type = SkillType.Direct;

        [Header("--- 2. 施法条件与消耗 ---")]
        public TargetType AllowedTargets = TargetType.Direction;
        public float CastRange;
        public float Cooldown = 1f;
        public int CostMana;
        public bool CanCastInStun;
        public bool OrientToTargetOnCast = true;
        public int Damage;
        [Tooltip("勾上表示这是普攻，能打只吃普攻的建筑")]
        public bool IsAutoAttack;

        [Header("--- 3. Timeline 表现与时序 ---")]
        public TimelineAsset Timeline;
        [Tooltip("不播英雄脚下爆发/闪白")]
        public bool SuppressCastBurst;
        public string CastVfxModule = "";
        public string HitImpactModule = "";
        public string DefaultAnimKey = "";

        /// <summary>导出为运行时技能配置。</summary>
        public SkillConfig ToConfig(SkillClip[] clips = null)
        {
            return new SkillConfig
            {
                SkillId = SkillId,
                Name = SkillName ?? "",
                Type = (int)Type,
                AllowedTargets = (int)AllowedTargets,
                CastRange = CastRange,
                Cooldown = Cooldown,
                CostMana = CostMana,
                CanCastInStun = CanCastInStun,
                OrientToTargetOnCast = OrientToTargetOnCast,
                Damage = Damage,
                IsAutoAttack = IsAutoAttack,
                Clips = clips ?? System.Array.Empty<SkillClip>()
            };
        }

        /// <summary>从运行时技能配置反填编辑资产。</summary>
        public void ApplyFromConfig(SkillConfig cfg, SkillPresentationConfig presentation = null)
        {
            if (cfg == null) return;
            SkillId = cfg.SkillId;
            SkillName = cfg.Name ?? "";
            Type = cfg.SkillType;
            AllowedTargets = cfg.TargetMode;
            CastRange = cfg.CastRange;
            Cooldown = cfg.Cooldown;
            CostMana = cfg.CostMana;
            CanCastInStun = cfg.CanCastInStun;
            OrientToTargetOnCast = cfg.OrientToTargetOnCast;
            Damage = cfg.Damage;
            IsAutoAttack = cfg.IsAutoAttack;
            if (presentation == null) return;
            SuppressCastBurst = presentation.SuppressCastBurst;
            CastVfxModule = presentation.CastVfxModule ?? "";
            HitImpactModule = presentation.HitImpactModule ?? "";
            DefaultAnimKey = presentation.DefaultAnimKey ?? "";
        }
    }
}
