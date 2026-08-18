using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>镜头轨：震屏、推镜等。</summary>
    [TrackColor(0.48f, 0.52f, 0.78f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/镜头")]
    public class SkillCameraTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Camera;
    }
}
