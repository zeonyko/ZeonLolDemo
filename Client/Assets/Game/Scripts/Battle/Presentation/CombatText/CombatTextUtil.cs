using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>飘字分类和文案拼装。只改显示，不改数值。</summary>
    public static class CombatTextUtil
    {
        #region Kind 判定
        /// <summary>按伤害正负 / 命中标记 / 伤害类型判定飘字 Kind（负数视为治疗）。</summary>
        public static CombatTextKind ResolveDamageKind(float damage, uint hitFlags, DamageType damageType)
        {
            if (damage < 0f)
                return CombatTextKind.Heal;
            if ((hitFlags & (uint)EBattle_HitFlags.Dodge) != 0)
                return CombatTextKind.Miss;
            if ((hitFlags & (uint)EBattle_HitFlags.Crit) != 0)
                return CombatTextKind.Critical;

            switch (damageType)
            {
                case DamageType.Magical: return CombatTextKind.Magical;
                case DamageType.True: return CombatTextKind.True;
                default: return CombatTextKind.Physical;
            }
        }

        #endregion

        #region 伤害类型推断
        /// <summary>从技能主命中配置中推断伤害类型：形状命中 → 弹道命中 → 区域命中，均无则默认物理。</summary>
        public static DamageType InferDamageTypeFromSkill(int skillId)
        {
            if (skillId <= 0) return DamageType.Physical;
            var def = SkillCatalog.Get(skillId);
            var hit = def?.PrimaryHit;
            if (hit == null) return DamageType.Physical;

            // 优先形状命中伤害类型
            if (hit.ShapeHits != null)
            {
                for (int i = 0; i < hit.ShapeHits.Length; i++)
                {
                    var dmg = hit.ShapeHits[i]?.Effect?.Damage;
                    if (dmg != null) return dmg.Type;
                }
            }
            if (hit.ProjectileSpawns != null)
            {
                for (int i = 0; i < hit.ProjectileSpawns.Length; i++)
                {
                    int pid = hit.ProjectileSpawns[i]?.ProjectileId ?? 0;
                    if (pid <= 0) continue;
                    if (ProjectileCatalog.TryGet(pid, out var pcfg) && pcfg?.HitEffect?.Damage != null)
                        return pcfg.HitEffect.Damage.Type;
                }
            }
            if (hit.AreaSpawns != null)
            {
                for (int i = 0; i < hit.AreaSpawns.Length; i++)
                {
                    var dmg = hit.AreaSpawns[i]?.TickEffect?.Damage;
                    if (dmg != null) return dmg.Type;
                }
            }
            return DamageType.Physical;
        }

        #endregion

        #region 文案拼装
        /// <summary>拼伤害数字：治疗直接加前后缀；暴击已有前缀；其余前面加负号。</summary>
        public static string FormatDamage(float damage, CombatTextStyleEntry style)
        {
            int abs = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(damage)));
            string prefix = style?.Prefix ?? "";
            string suffix = style?.Suffix ?? "";
            if (damage < 0f)
                return $"{prefix}{abs}{suffix}";
            // 伤害默认显示绝对值；暴击前缀已写在样式里
            if (!string.IsNullOrEmpty(prefix) && prefix.IndexOf("CRITICAL", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return $"{prefix}{abs}{suffix}";
            return $"-{abs}{suffix}";
        }

        /// <summary>拼装状态文案：按样式补感叹号后缀，统一转大写。</summary>
        public static string FormatStatus(string label, CombatTextStyleEntry style)
        {
            if (string.IsNullOrEmpty(label)) return "";
            string text = label.Trim();
            string suffix = style?.Suffix ?? "";
            if (!string.IsNullOrEmpty(suffix) && !text.EndsWith(suffix))
                text += suffix;
            return text.ToUpperInvariant();
        }
        #endregion
    }
}
