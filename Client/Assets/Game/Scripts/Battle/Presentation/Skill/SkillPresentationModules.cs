using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>技能额外表现：按配置播施法/命中附加特效和默认动画。网络和播放器不要再按技能 Id 写死。</summary>
    public static class SkillPresentationModules
    {
        #region 施法阶段模块
        /// <summary>施法额外模块（充能光束等）。</summary>
        public static void PlayCastExtras(Entity caster, int skillId, long targetId, Vector3 targetPos)
        {
            if (caster == null || skillId <= 0) return;
            if (!SkillPresentationCatalog.TryGet(skillId, out var pres) || pres == null)
                return;
            if (string.IsNullOrEmpty(pres.CastVfxModule)) return;

            switch (pres.CastVfxModule)
            {
                case SkillCastVfxModules.TowerLockBeam:
                    TowerAttackVfx.PlayCast(caster, targetId, targetPos);
                    break;
            }
        }

        #endregion

        #region 命中阶段模块
        /// <summary>命中地面/强化 Impact；未配置模块时走默认溅射。</summary>
        public static void PlayHitImpact(int skillId, Vector3 feet)
        {
            string module = null;
            if (SkillPresentationCatalog.TryGet(skillId, out var pres) && pres != null)
                module = pres.HitImpactModule;

            if (string.IsNullOrEmpty(module))
            {
                VfxManager.Instance?.PlayGroundSplash(feet);
                return;
            }

            switch (module)
            {
                case SkillHitImpactModules.TowerImpact:
                    TowerAttackVfx.PlayHitImpact(feet);
                    break;
                default:
                    VfxManager.Instance?.PlayGroundSplash(feet);
                    break;
            }
        }

        #endregion

        #region 表现开关与默认值查询
        /// <summary>要不要关掉英雄起手闪光（单位或技能配置说了算）。</summary>
        public static bool ShouldSuppressCastBurst(Entity caster, int skillId)
        {
            if (caster != null && CombatFeedbackRules.SuppressCasterFx(caster.Id))
                return true;
            if (SkillPresentationCatalog.TryGet(skillId, out var pres) && pres != null && pres.SuppressCastBurst)
                return true;
            return false;
        }

        /// <summary>默认施法动画：技能配置 → 第一条动画轨 → Attack3。</summary>
        public static string ResolveDefaultAnimKey(int skillId)
        {
            if (SkillPresentationCatalog.TryGet(skillId, out var pres) && pres != null)
            {
                if (!string.IsNullOrEmpty(pres.DefaultAnimKey))
                    return pres.DefaultAnimKey;

                if (pres.Clips != null)
                {
                    for (int i = 0; i < pres.Clips.Length; i++)
                    {
                        var c = pres.Clips[i];
                        if (c == null || c.Track != SkillTrackId.Anim) continue;
                        string key = !string.IsNullOrEmpty(c.Param) ? c.Param : c.Key;
                        if (!string.IsNullOrEmpty(key))
                            return key;
                    }
                }
            }

            return AnimationComponent.StateAttack3;
        }
        #endregion
    }
}
