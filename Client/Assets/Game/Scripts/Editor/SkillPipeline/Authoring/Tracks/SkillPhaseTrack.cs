using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>逻辑阶段轨：前摇 / 判定 / 后摇 / 结束。</summary>
    [TrackColor(0.86f, 0.52f, 0.18f)]
    [TrackClipType(typeof(SkillEventClip))]
    [TrackClipType(typeof(SkillHitPhaseClip))]
    [DisplayName("技能/逻辑")]
    public class SkillPhaseTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.Phase;
    }
}
