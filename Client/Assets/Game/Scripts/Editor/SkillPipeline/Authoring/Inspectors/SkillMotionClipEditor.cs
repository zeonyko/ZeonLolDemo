using Client.SkillAuthoring;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Client.EditorTools
{
    /// <summary>Timeline 轨道上 SkillMotionClip 的显示名/Tooltip 渲染。</summary>
    [CustomTimelineEditor(typeof(SkillMotionClip))]
    public class SkillMotionClipTimelineEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            var options = base.GetClipOptions(clip);
            if (clip.asset is SkillMotionClip motion)
            {
                clip.displayName = $"{motion.CommitKey} {motion.Distance:0.##}";
                options.displayClipName = true;
                options.tooltip = motion.EncodeParam();
            }
            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip.asset is SkillMotionClip motion)
                clip.displayName = $"{motion.CommitKey} {motion.Distance:0.##}";
        }
    }
}
