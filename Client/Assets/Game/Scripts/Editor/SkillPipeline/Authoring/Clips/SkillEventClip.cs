using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Client.SkillAuthoring
{
    /// <summary>通用事件片段：用键和参数描述一次动画/音效/特效/镜头/卡肉。</summary>
    [Serializable]
    public class SkillEventClip : PlayableAsset, ITimelineClipAsset
    {
        public string Key = "Play";
        /// <summary>特效参数示例：路径，可选挂点和偏移。</summary>
        public string Param = "";

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return Playable.Create(graph);
        }
    }
}
