using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>在场景里按形状扫人。点在不在形状里走 ShapeOverlap。</summary>
    public static class ShapeHitService
    {
        public struct HitCandidate
        {
            public Entity Target;
        }

        public static List<HitCandidate> Collect(
            Entity caster,
            Scene scene,
            SkillShapePayload[] shapes,
            float originX,
            float originY,
            float originZ,
            float dirX,
            float dirZ,
            int skillId = 0)
        {
            var result = new List<HitCandidate>(8);
            if (caster == null || scene == null || shapes == null || shapes.Length == 0)
                return result;

            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            float rightX = dirZ;
            float rightZ = -dirX;

            var seen = new HashSet<long>();

            for (int s = 0; s < shapes.Length; s++)
            {
                var shape = shapes[s];
                if (shape == null) continue;

                float ox = originX + shape.OffsetX * rightX + shape.OffsetZ * dirX;
                float oz = originZ + shape.OffsetX * rightZ + shape.OffsetZ * dirZ;

                foreach (var entity in scene.GetAllEntities())
                {
                    if (entity == null || entity.Id == caster.Id) continue;
                    if (entity.Hp <= 0f) continue;
                    if (!SkillResultService.IsValidSkillTarget(caster, entity, skillId)) continue;
                    if (seen.Contains(entity.Id)) continue;

                    if (!ShapeOverlap.ContainsLocal(
                            shape, ox, oz, dirX, dirZ, rightX, rightZ, entity.PosX, entity.PosZ))
                        continue;

                    seen.Add(entity.Id);
                    result.Add(new HitCandidate { Target = entity });
                }
            }

            return result;
        }
    }
}
