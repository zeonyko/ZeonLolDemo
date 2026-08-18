using Client.SkillAuthoring;
using Shared;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Client.EditorTools
{
    /// <summary>各轨道可选事件 Key 的枚举表，供 Inspector 下拉框使用。</summary>
    public static class SkillEventKeys
    {
        public static readonly string[] Logic =
        {
            SkillTimelineKeys.Windup,
            SkillTimelineKeys.Hit,
            SkillTimelineKeys.Recovery,
            SkillTimelineKeys.End,
            SkillTimelineKeys.ChannelStart,
            SkillTimelineKeys.ChannelEnd,
            SkillTimelineKeys.ChargeStart,
            SkillTimelineKeys.ChargeRelease,
        };

        public static readonly string[] WindowCancel = { SkillTimelineKeys.AllowCancel };
        public static readonly string[] WindowAccept = { SkillTimelineKeys.AcceptInput };

        public static readonly string[] Motion =
        {
            SkillTimelineKeys.CommitBlink,
            SkillTimelineKeys.CommitDash,
            SkillTimelineKeys.CommitJump,
        };

        public static readonly string[] Present = { SkillTimelineKeys.Play };
        public static readonly string[] HitStop = { SkillTimelineKeys.HitStop };

        /// <summary>轨道 Id → 该轨道允许选择的事件 Key 列表；未匹配的轨道回退到表现类 Present。</summary>
        public static string[] ForTrackId(int trackId)
        {
            if (trackId == SkillTrackId.Phase) return Logic;
            if (trackId == SkillTrackId.WindowCancel) return WindowCancel;
            if (trackId == SkillTrackId.WindowAccept) return WindowAccept;
            if (trackId == SkillTrackId.HitStop) return HitStop;
            return Present;
        }

        public static string ClipTitle(string key, string param)
            => SkillEventLabels.ClipTitle(key, param);
    }

    /// <summary>Timeline 轨道上 SkillEventClip 的显示名/Tooltip 渲染。</summary>
    [CustomTimelineEditor(typeof(SkillEventClip))]
    public class SkillEventClipTimelineEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            var options = base.GetClipOptions(clip);
            if (clip.asset is SkillEventClip skill)
            {
                string title = SkillEventKeys.ClipTitle(skill.Key, skill.Param);
                if (!string.IsNullOrEmpty(title))
                    clip.displayName = title;
                options.displayClipName = true;
                options.tooltip = string.IsNullOrEmpty(skill.Param)
                    ? SkillEventLabels.KeyLabel(skill.Key)
                    : $"{SkillEventLabels.KeyLabel(skill.Key)}\n{skill.Param}";
            }
            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (!(clip.asset is SkillEventClip skill)) return;
            string title = SkillEventKeys.ClipTitle(skill.Key, skill.Param);
            if (!string.IsNullOrEmpty(title))
                clip.displayName = title;
        }
    }

    /// <summary>SkillEventClip 的 Inspector：按所在轨道过滤事件 Key 下拉框 + 参数字段。</summary>
    [CustomEditor(typeof(SkillEventClip))]
    public class SkillEventClipEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var keyProp = serializedObject.FindProperty("Key");
            var paramProp = serializedObject.FindProperty("Param");
            int trackId = ResolveTrackId();
            string[] options = SkillEventKeys.ForTrackId(trackId);
            string[] labels = SkillEventLabels.PopupLabels(options);

            int index = IndexOf(options, keyProp.stringValue);
            if (index < 0)
            {
                index = 0;
                keyProp.stringValue = options[0];
            }

            int next = EditorGUILayout.Popup("事件", index, labels);
            if (next != index)
                keyProp.stringValue = options[next];

            EditorGUILayout.PropertyField(paramProp, new GUIContent("参数"));
            if (trackId == SkillTrackId.Vfx)
            {
                EditorGUILayout.HelpBox(
                    "特效参数：Vfx_Slash | Vfx_Slash,Foot | Vfx_Slash,Foot,0,0.2,0",
                    MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>取当前选中 Clip 所在轨道的 TrackId；未选中或未挂载轨道时回退到逻辑轨。</summary>
        private int ResolveTrackId()
        {
            var selected = TimelineEditor.selectedClip;
            if (selected != null && selected.asset == target
                && selected.GetParentTrack() is SkillEventTrack st)
                return st.TrackId;
            return SkillTrackId.Phase;
        }

        private static int IndexOf(string[] options, string value)
        {
            if (options == null) return -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == value) return i;
            }
            return -1;
        }
    }
}
