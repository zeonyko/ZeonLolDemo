using Shared;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>位移轨 Clip。</summary>
    [System.Serializable]
    public class SkillMotionClip : PlayableAsset, ITimelineClipAsset
    {
        public ESkillMotionClipType MotionType = ESkillMotionClipType.Blink;
        public float Distance = 5f;
        public ESkillMotionTargetType TargetType = ESkillMotionTargetType.InputDirection;
        public ESkillMotionCollisionPolicy CollisionPolicy = ESkillMotionCollisionPolicy.PassThrough;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return Playable.Create(graph);
        }

        /// <summary>该位移类型对应的 Timeline 事件 Key。</summary>
        public string CommitKey => SkillMotionCodec.KeyFromMotionType(MotionType);

        /// <summary>编码为事件 Param 字符串，供 SkillEventClip 复用同一 Key 时写入。</summary>
        public string EncodeParam() =>
            SkillMotionCodec.Encode(Distance, TargetType, CollisionPolicy);

        /// <summary>导出给运行时用的位移数据。</summary>
        public SkillMotionPayload ToPayload()
        {
            return new SkillMotionPayload
            {
                MotionType = (int)MotionType,
                Distance = Distance,
                TargetType = (int)TargetType,
                CollisionPolicy = (int)CollisionPolicy
            };
        }

        /// <summary>从运行时数据反填；没有内容就按事件键猜位移类型。</summary>
        public void ApplyFromPayload(SkillMotionPayload motion, string keyFallback = null)
        {
            if (motion != null && motion.HasContent)
            {
                MotionType = motion.Type;
                Distance = motion.Distance;
                TargetType = (ESkillMotionTargetType)motion.TargetType;
                CollisionPolicy = (ESkillMotionCollisionPolicy)motion.CollisionPolicy;
                return;
            }

            if (!string.IsNullOrEmpty(keyFallback))
                MotionType = SkillMotionCodec.MotionTypeFromKey(keyFallback);
        }

        /// <summary>从旧版 SkillEventClip 的 Key/Param 迁移数据。</summary>
        public void ApplyFromEvent(string key, string param)
        {
            MotionType = SkillMotionCodec.MotionTypeFromKey(key);
            if (SkillMotionCodec.TryParse(param, out float d, out var target, out var collision))
            {
                Distance = d;
                TargetType = target;
                CollisionPolicy = collision;
            }
        }
    }
}
