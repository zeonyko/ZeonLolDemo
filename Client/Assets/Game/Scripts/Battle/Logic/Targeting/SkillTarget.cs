using UnityEngine;

namespace Client.Battle
{
    /// <summary>输入给索敌的提示，都可以空。</summary>
    public struct SkillAim
    {
        public Vector3 Dir;
        public Vector3 Point;
        public bool HasPoint;
        public long EntityId;
        public float StickMag;
    }

    /// <summary>技能真正吃的目标：谁、朝哪、落点、在不在射程。</summary>
    public struct SkillTarget
    {
        public long EntityId;
        public Vector3 Dir;
        public Vector3 Point;
        public bool HasPoint;
        public float Dist;
        public float Range;
        public bool InRange;

        public SkillAim ToAim()
        {
            return new SkillAim
            {
                Dir = Dir,
                Point = Point,
                HasPoint = HasPoint,
                EntityId = EntityId
            };
        }
    }
}
