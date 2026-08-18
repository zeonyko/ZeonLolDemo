using System.ComponentModel;
using Shared;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>可取消窗口：这段时间允许取消当前技能。</summary>
    [TrackColor(0.35f, 0.75f, 0.85f)]
    [TrackClipType(typeof(SkillEventClip))]
    [DisplayName("技能/可取消")]
    public class SkillCancelWindowTrack : SkillEventTrack
    {
        public override int TrackId => SkillTrackId.WindowCancel;
    }
}
