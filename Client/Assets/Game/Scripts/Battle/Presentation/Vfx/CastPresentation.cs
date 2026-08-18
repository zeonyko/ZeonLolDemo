using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>施法瞬间：轮廓闪白 + 脚下爆发。不改判定。</summary>
    public static class CastPresentation
    {
        /// <summary>施法瞬间：闪白 + 脚下能量爆发特效。</summary>
        public static void PlayCastBurst(Entity caster, int skillId, Vector3 castDir)
        {
            if (caster == null || VfxManager.Instance == null) return;

            var feet = caster.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            feet.y = GameConstants.GroundY;
            if (!VfxLod.AllowVfxSpawn(feet)) return;

            if (SkillPresentationModules.ShouldSuppressCastBurst(caster, skillId)) return;

            Vector3 forward = castDir;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            else forward.Normalize();

            Color burst = ResolveCastColor(skillId);
            caster.GetComponent<CombatViewComponent>()?.PlayCastFlash(burst);

            VfxLibrary.SpawnTransient(
                "Vfx_CastBurst",
                feet + Vector3.up * 0.08f + forward * 0.35f,
                forward,
                durationOverride: 0.28f,
                worldVelocity: Vector3.up * 0.25f,
                tint: burst);
        }

        /// <summary>按技能伤害类型推断爆发特效颜色。</summary>
        static Color ResolveCastColor(int skillId)
        {
            var type = CombatTextUtil.InferDamageTypeFromSkill(skillId);
            return type switch
            {
                DamageType.Magical => new Color(0.55f, 0.75f, 1f, 1f),
                DamageType.True => new Color(0.95f, 0.95f, 1f, 1f),
                _ => new Color(1f, 0.85f, 0.4f, 1f),
            };
        }
    }
}
