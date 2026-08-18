using Shared;

namespace Client.Battle
{
    /// <summary>客户端准入：把本地状态填进 SkillAdmit。</summary>
    public static class SkillCastGate
    {
        public static bool CanBegin(
            int skillId,
            SkillCooldownService cooldown,
            StateComponent stateComp,
            TransformComponent transformComp,
            long ownerId,
            out SkillConfig data)
        {
            data = SkillCatalog.Get(skillId);
            if (data == null) return false;

            var q = new SkillAdmitQuery
            {
                CooldownReady = cooldown == null || cooldown.IsCooldownReady(skillId),
                CanCast = stateComp == null
                          || stateComp.CanCastSkill(data.IsProjectile, data.AllowsCastWhileRooted),
                CanAttack = stateComp == null || stateComp.CanAttack,
                IsGrounded = transformComp != null && transformComp.IsGrounded,
                HasLockInRange = !data.NeedsLockTarget
                                 || SkillTargeting.HasLockInRange(ownerId, skillId)
            };
            return SkillAdmit.CanBegin(data, q);
        }
    }
}
