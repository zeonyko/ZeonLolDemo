using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>位移轨：闪现、冲刺、跳跃等。</summary>
    [TrackColor(0.92f, 0.78f, 0.22f)]
    [TrackClipType(typeof(SkillMotionClip))]
    [System.ComponentModel.DisplayName("技能/位移")]
    public class SkillMotionTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Motion;
    }
}
