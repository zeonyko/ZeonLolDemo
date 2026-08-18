using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>可接招窗口：这段时间允许接下一个技能。</summary>
    [TrackColor(0.45f, 0.85f, 0.55f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/可接招")]
    public class SkillAcceptWindowTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.WindowAccept;
    }
}
