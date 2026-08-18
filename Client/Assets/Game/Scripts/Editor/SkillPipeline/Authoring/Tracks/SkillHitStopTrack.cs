using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>卡肉/顿帧区间。技能和反馈都能用；默认用普通事件片段。</summary>
    [TrackColor(0.85f, 0.25f, 0.25f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/卡肉")]
    public class SkillHitStopTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.HitStop;
    }
}
