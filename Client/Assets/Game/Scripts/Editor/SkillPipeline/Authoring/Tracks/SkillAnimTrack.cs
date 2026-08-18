using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>动画轨：到点播角色动作。</summary>
    [TrackColor(0.30f, 0.62f, 0.92f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/动画")]
    public class SkillAnimTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Anim;
    }
}
