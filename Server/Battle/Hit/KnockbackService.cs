using System;
using Shared;

namespace Server.Battle
{
    /// <summary>把人推开，撞墙就停下，再告诉客户端新位置。</summary>
    public static class KnockbackService
    {
        /// <summary>对活人（非建筑）推一下。</summary>
        public static bool TryApply(Entity caster, Entity target, float distance)
        {
            if (caster == null || target == null || distance < 0.05f)
                return false;
            if (target.Hp <= 0f || target.IsStructure)
                return false;
            if (target.State.HasState(EntityStateTag.Invincible))
                return false;

            float dirX = target.PosX - caster.PosX;
            float dirZ = target.PosZ - caster.PosZ;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            float beforeX = target.PosX;
            float beforeZ = target.PosZ;
            target.MoveState = MovementSimulator.ApplySkillMotion(
                target.MoveState, dirX, dirZ, distance);

            float dx = target.PosX - beforeX;
            float dz = target.PosZ - beforeZ;
            if (dx * dx + dz * dz < 0.0001f)
                return false;

            SkillResultService.BroadcastForceRelocate(target, EBattle_RelocateReason.Knockback);
            Console.WriteLine(
                $"[Knockback] target={target.Id} from={caster.Id} dist={distance:F2} " +
                $"land=({target.PosX:F2},{target.PosZ:F2})");
            return true;
        }
    }
}
