using System;

namespace Shared
{
    /// <summary>地面上的盒 / 圆 / 扇是否罩住某点。命中扫人仍由各自世界去做。</summary>
    public static class ShapeOverlap
    {
        public static bool ContainsWorldPoint(
            SkillShapePayload shape,
            float originX, float originY, float originZ,
            float dirX, float dirZ,
            float px, float pz)
        {
            if (shape == null) return false;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            float rightX = dirZ;
            float rightZ = -dirX;
            float ox = originX + shape.OffsetX * rightX + shape.OffsetZ * dirX;
            float oz = originZ + shape.OffsetX * rightZ + shape.OffsetZ * dirZ;
            return ContainsLocal(shape, ox, oz, dirX, dirZ, rightX, rightZ, px, pz);
        }

        public static bool ContainsLocal(
            SkillShapePayload shape,
            float ox, float oz,
            float dirX, float dirZ,
            float rightX, float rightZ,
            float px, float pz)
        {
            if (shape == null) return false;

            float dx = px - ox;
            float dz = pz - oz;
            float localX = dx * rightX + dz * rightZ;
            float localZ = dx * dirX + dz * dirZ;

            switch (shape.Type)
            {
                case HitShapeType.Sphere:
                {
                    float r = shape.SizeX > 0f ? shape.SizeX : 1f;
                    return localX * localX + localZ * localZ <= r * r;
                }
                case HitShapeType.Sector:
                {
                    float radius = shape.SizeX > 0f ? shape.SizeX : 1f;
                    float angleDeg = shape.SizeY > 0f ? shape.SizeY : 60f;
                    float distSq = localX * localX + localZ * localZ;
                    if (distSq > radius * radius) return false;
                    if (distSq < 0.0001f) return true;
                    float dist = (float)Math.Sqrt(distSq);
                    float cos = localZ / dist;
                    float half = (angleDeg * 0.5f) * (float)(Math.PI / 180.0);
                    return cos >= (float)Math.Cos(half);
                }
                default:
                {
                    float halfLen = (shape.SizeX > 0f ? shape.SizeX : 1f) * 0.5f;
                    float halfWid = (shape.SizeY > 0f ? shape.SizeY : 1f) * 0.5f;
                    float centeredZ = localZ - halfLen;
                    return Math.Abs(localX) <= halfWid && Math.Abs(centeredZ) <= halfLen;
                }
            }
        }
    }
}
