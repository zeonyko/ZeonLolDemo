using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>技能时间轴事件轨基类：标明这是哪条轨。</summary>
    public abstract class SkillEventTrack : TrackAsset
    {
        public abstract int TrackId { get; }
    }
}
