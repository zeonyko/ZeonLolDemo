namespace Shared
{
    public struct SkillAdmitQuery
    {
        public bool CooldownReady;
        public bool CanCast;
        public bool CanAttack;
        public bool IsGrounded;
        public bool HasLockInRange;
    }

    /// <summary>两端同一套「能不能起手」。会话、血量、进行中的施法由各自再判。</summary>
    public static class SkillAdmit
    {
        public static bool CanBegin(SkillConfig def, in SkillAdmitQuery q)
        {
            if (def == null) return false;
            if (!q.CooldownReady) return false;
            if (!q.CanCast) return false;
            if (SkillRules.RequiresAttackGate(def) && !q.CanAttack) return false;
            if (def.ResolveMode == ESkillResolveMode.Jump && !q.IsGrounded) return false;
            if (def.NeedsLockTarget && !q.HasLockInRange) return false;
            return true;
        }
    }
}
