using System;

namespace Shared
{
    /// <summary>判定从哪取锚点：自己、锁定目标、落点、或范围内搜人。</summary>
    [Serializable]
    public class TargetSelectorData
    {
        public int SelectorType;
        /// <summary>搜人时的阵营过滤。</summary>
        public int Relation = (int)TargetRelation.Enemy;
        public float SearchRadius;
        /// <summary>最多取几个锚点。</summary>
        public int MaxTargetCount = 1;
        public float PositionOffsetX;
        public float PositionOffsetY;
        public float PositionOffsetZ;
        public float RotationOffsetX;
        public float RotationOffsetY;
        public float RotationOffsetZ;

        public TargetSelectorType Type
        {
            get => (TargetSelectorType)SelectorType;
            set => SelectorType = (int)value;
        }

        public TargetRelation TargetRelation
        {
            get => (TargetRelation)Relation;
            set => Relation = (int)value;
        }

        public static TargetSelectorData CasterSelf(float ox = 0f, float oy = 0f, float oz = 0f) =>
            new TargetSelectorData
            {
                SelectorType = (int)TargetSelectorType.CasterSelf,
                MaxTargetCount = 1,
                PositionOffsetX = ox,
                PositionOffsetY = oy,
                PositionOffsetZ = oz
            };

        public static TargetSelectorData FixedWorld(float ox = 0f, float oy = 0f, float oz = 0f) =>
            new TargetSelectorData
            {
                SelectorType = (int)TargetSelectorType.FixedWorldPos,
                MaxTargetCount = 1,
                PositionOffsetX = ox,
                PositionOffsetY = oy,
                PositionOffsetZ = oz
            };
    }
}
