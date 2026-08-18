using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>服务器准入：会话和进行中的施法在这里判，其余走 SkillAdmit。</summary>
    public static class SkillCastValidator
    {
        public struct AdmitResult
        {
            public Entity Caster;
            public SkillConfig Def;
            public Scene Scene;
            public long TargetEntityId;
        }

        public static bool TryAdmitCast(
            ClientSession session,
            C2S_Battle_SkillCastCmd req,
            out AdmitResult result)
        {
            result = default;

            if (session == null || session.PlayerEntityId == 0)
                return false;

            var def = SkillCatalog.Get(req.SkillId);
            if (def == null) return false;

            var scene = WorldManager.Instance?.GetDefaultScene();
            var caster = scene?.GetEntity(session.PlayerEntityId);
            if (caster == null) return false;
            if (scene.MatchEnded || caster.Hp <= 0f)
                return false;

            if (PendingCastService.Instance.HasPending(caster.Id))
            {
                if (!SkillRules.IsCancelSkill(def))
                    return false;
                PendingCastService.Instance.Cancel(
                    caster.Id, broadcast: true, EBattle_SkillCancelNotifyReason.ActiveCancel);
            }

            long targetEntityId = 0;
            bool hasLockInRange = true;
            if (def.NeedsLockTarget)
            {
                var lockTarget = scene.GetEntity(req.TargetId);
                hasLockInRange = lockTarget != null
                    && SkillResultService.IsValidSkillTarget(caster, lockTarget, def.SkillId)
                    && SkillRules.IsInSkillRange(
                        caster.PosX, caster.PosY, caster.PosZ,
                        lockTarget.PosX, lockTarget.PosY, lockTarget.PosZ,
                        def.SkillId);
                if (hasLockInRange)
                    targetEntityId = req.TargetId;
            }
            else if (req.TargetId != 0)
            {
                var optional = scene.GetEntity(req.TargetId);
                if (optional != null && SkillResultService.IsValidSkillTarget(caster, optional, def.SkillId))
                    targetEntityId = req.TargetId;
            }

            float serverTime = BattleSystem.Instance?.ServerTime ?? 0f;
            var q = new SkillAdmitQuery
            {
                CooldownReady = SkillRules.IsCooldownReady(
                    serverTime, caster.GetLastCastTime(def.SkillId), def.SkillId),
                CanCast = caster.State.CanCastSkill(def.IsProjectile, def.AllowsCastWhileRooted),
                CanAttack = caster.State.CanAttack,
                IsGrounded = caster.MoveState.IsGrounded,
                HasLockInRange = hasLockInRange
            };
            if (!SkillAdmit.CanBegin(def, q))
                return false;

            result = new AdmitResult
            {
                Caster = caster,
                Def = def,
                Scene = scene,
                TargetEntityId = targetEntityId
            };
            return true;
        }
    }
}
