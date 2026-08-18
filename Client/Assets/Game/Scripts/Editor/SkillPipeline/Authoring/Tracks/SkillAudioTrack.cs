using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>音频轨：到点播音效。</summary>
    [TrackColor(0.28f, 0.78f, 0.48f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/音频")]
    public class SkillAudioTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Audio;
    }
}
