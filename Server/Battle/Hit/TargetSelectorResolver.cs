using System;
using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>按选人规则算出技能落点。</summary>
    public static class TargetSelectorResolver
    {
        public struct AnchorPose
        {
            public float PosX;
            public float PosY;
            public float PosZ;
            public float DirX;
            public float DirZ;
            public Entity Entity;
        }

        public static void ResolveAnchors(
            Entity caster,
            Scene scene,
            PendingCastService.PendingCastState cast,
            TargetSelectorData selector,
            List<AnchorPose> into)
        {
            into?.Clear();
            if (into == null || caster == null) return;

            var sel = selector ?? TargetSelectorData.CasterSelf();
            float dirX = cast?.DirX ?? 0f;
            float dirZ = cast?.DirZ ?? 1f;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            ApplyYaw(ref dirX, ref dirZ, sel.RotationOffsetY);

            switch (sel.Type)
            {
                case TargetSelectorType.FixedWorldPos:
                    ResolveFixedWorld(caster, cast, sel, dirX, dirZ, into);
                    break;
                case TargetSelectorType.DynamicTargets:
                    ResolveDynamic(caster, scene, sel, dirX, dirZ, into);
                    break;
                case TargetSelectorType.LowestHpAllies:
                    ResolveLowestHpAllies(caster, scene, sel, dirX, dirZ, into);
                    break;
                case TargetSelectorType.CasterSelf:
                default:
                    AddPose(into, caster.PosX, caster.PosY, caster.PosZ, dirX, dirZ, sel, caster);
                    break;
            }

            if (into.Count == 0)
                AddPose(into, caster.PosX, caster.PosY, caster.PosZ, dirX, dirZ, sel, caster);
        }

        static void ResolveFixedWorld(
            Entity caster,
            PendingCastService.PendingCastState cast,
            TargetSelectorData sel,
            float dirX, float dirZ,
            List<AnchorPose> into)
        {
            float x = caster.PosX;
            float y = caster.PosY;
            float z = caster.PosZ;
            if (cast?.TargetPosition != null)
            {
                x = cast.TargetPosition.X;
                y = cast.TargetPosition.Y;
                z = cast.TargetPosition.Z;
            }
            AddPose(into, x, y, z, dirX, dirZ, sel, null);
        }

        static void ResolveDynamic(
            Entity caster,
            Scene scene,
            TargetSelectorData sel,
            float dirX, float dirZ,
            List<AnchorPose> into)
        {
            if (scene == null) return;
            float radius = sel.SearchRadius > 0.05f ? sel.SearchRadius : 5f;
            int max = sel.MaxTargetCount > 0 ? sel.MaxTargetCount : 1;
            float r2 = radius * radius;
            var relation = sel.TargetRelation;

            foreach (var e in scene.GetAllEntities())
            {
                if (e == null || e.Hp <= 0f) continue;
                if (!BuffApplyFilter.RelationMatches(
                        relation, caster.Id, caster.TeamId, e.Id, e.TeamId))
                    continue;
                float dx = e.PosX - caster.PosX;
                float dz = e.PosZ - caster.PosZ;
                if (dx * dx + dz * dz > r2) continue;
                AddPose(into, e.PosX, e.PosY, e.PosZ, dirX, dirZ, sel, e);
                if (into.Count >= max) break;
            }
        }

        static void ResolveLowestHpAllies(
            Entity caster,
            Scene scene,
            TargetSelectorData sel,
            float dirX, float dirZ,
            List<AnchorPose> into)
        {
            if (scene == null) return;
            float radius = sel.SearchRadius > 0.05f ? sel.SearchRadius : 12f;
            int max = sel.MaxTargetCount > 0 ? sel.MaxTargetCount : 1;
            float r2 = radius * radius;
            var list = new List<Entity>(8);

            foreach (var e in scene.GetAllEntities())
            {
                if (e == null || e.Hp <= 0f) continue;
                if (!BuffApplyFilter.RelationMatches(
                        TargetRelation.Ally, caster.Id, caster.TeamId, e.Id, e.TeamId)
                    && e.Id != caster.Id)
                    continue;
                // 自己和队友都算
                bool selfOrAlly = e.Id == caster.Id
                    || BuffApplyFilter.RelationMatches(
                        TargetRelation.Ally, caster.Id, caster.TeamId, e.Id, e.TeamId);
                if (!selfOrAlly) continue;
                float dx = e.PosX - caster.PosX;
                float dz = e.PosZ - caster.PosZ;
                if (dx * dx + dz * dz > r2) continue;
                list.Add(e);
            }

            list.Sort((a, b) => a.Hp.CompareTo(b.Hp));
            for (int i = 0; i < list.Count && into.Count < max; i++)
            {
                var e = list[i];
                AddPose(into, e.PosX, e.PosY, e.PosZ, dirX, dirZ, sel, e);
            }
        }

        static void AddPose(
            List<AnchorPose> into,
            float x, float y, float z,
            float dirX, float dirZ,
            TargetSelectorData sel,
            Entity entity)
        {
            float rightX = dirZ;
            float rightZ = -dirX;
            float ox = x + sel.PositionOffsetX * rightX + sel.PositionOffsetZ * dirX;
            float oy = y + sel.PositionOffsetY;
            float oz = z + sel.PositionOffsetX * rightZ + sel.PositionOffsetZ * dirZ;
            into.Add(new AnchorPose
            {
                PosX = ox,
                PosY = oy,
                PosZ = oz,
                DirX = dirX,
                DirZ = dirZ,
                Entity = entity
            });
        }

        static void ApplyYaw(ref float dirX, ref float dirZ, float yawDegrees)
        {
            if (Math.Abs(yawDegrees) < 0.01f) return;
            double rad = yawDegrees * (Math.PI / 180.0);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            float nx = dirX * cos - dirZ * sin;
            float nz = dirX * sin + dirZ * cos;
            dirX = nx;
            dirZ = nz;
        }
    }
}
