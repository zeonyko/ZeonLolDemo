using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>特效轨：到点播特效。</summary>
    [TrackColor(0.88f, 0.42f, 0.72f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/特效")]
    public class SkillVfxTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Vfx;
    }
}
