using Shared;

namespace Server.Battle
{
    /// <summary>算伤害数字，再交给扣血。</summary>
    public static class DamageSystem
    {
        public static SkillHitEntry Apply(
            Entity caster,
            Entity target,
            int skillId,
            DamagePayload payload,
            string attackType,
            float knockbackDistance = 0f)
        {
            if (target == null)
                return EmptyHit();

            var damage = payload ?? DamagePayload.DefaultInstant();
            float amount = damage.ResolveAmount(SkillRules.GetAttackDamage(skillId));
            amount = BuffService.Instance.ScaleIncomingDamage(target, amount);
            var hit = SkillResultService.ApplyDamage(
                caster, target, skillId, amount, attackType);

            // 没打出伤害或人已经死了，就不再击退
            if (knockbackDistance <= 0.05f || IsBlocked(hit.HitFlags))
                return hit;

            KnockbackService.TryApply(caster, target, knockbackDistance);
            return hit;
        }

        private static bool IsBlocked(uint hitFlags)
        {
            uint blocked = (uint)EBattle_HitFlags.Invincible | (uint)EBattle_HitFlags.Lethal;
            return (hitFlags & blocked) != 0;
        }

        private static SkillHitEntry EmptyHit()
        {
            return new SkillHitEntry
            {
                TargetId = 0,
                Damage = 0,
                HitFlags = 0,
                KnockbackDir = Vector3Data.Zero,
                AttackType = ""
            };
        }
    }
}
