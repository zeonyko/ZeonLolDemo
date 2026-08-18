using System;

namespace Shared
{
    /// <summary>技能时间轴轨道编号。表现轨 0~4，逻辑轨 10+。</summary>
    public static class SkillTrackId
    {
        // 表现轨（动画/音效/特效/镜头/顿帧）：只给客户端。
        public const int Anim = 0;
        public const int Audio = 1;
        public const int Vfx = 2;
        public const int Camera = 3;
        public const int HitStop = 4;

        // 逻辑轨（阶段/位移/输入窗口）：两端都用，用来判定。
        public const int Phase = 10;
        public const int Motion = 12;
        public const int WindowCancel = 13;
        public const int WindowAccept = 14;

        /// <summary>一条事件默认持续多久（秒）。</summary>
        public const float DefaultClipDuration = 0.05f;
    }

    /// <summary>时间轴上的一个事件。不含命中和位移。</summary>
    [Serializable]
    public class SkillTimelineEventNode
    {
        /// <summary>触发时刻（秒）。</summary>
        public float Time;
        /// <summary>持续时长（秒）。</summary>
        public float Duration;
        public int Track;
        public string Key = "";
        public string Param = "";
    }

    /// <summary>时间轴事件名字。放技能和播动画用同一套。</summary>
    public static class SkillTimelineKeys
    {
        // 施法阶段
        public const string Windup = "Windup";
        public const string Hit = "Hit";
        public const string Recovery = "Recovery";
        public const string End = "End";
        public const string ChannelStart = "ChannelStart";
        public const string ChannelEnd = "ChannelEnd";
        public const string ChargeStart = "ChargeStart";
        public const string ChargeRelease = "ChargeRelease";

        // 能不能接下一段 / 能不能取消
        public const string AcceptInput = "AcceptInput";
        public const string AllowCancel = "AllowCancel";

        // 位移真正生效
        public const string CommitBlink = "CommitBlink";
        public const string CommitDash = "CommitDash";
        public const string CommitJump = "CommitJump";

        // 表现用
        public const string Play = "Play";
        public const string HitStop = "HitStop";

        /// <summary>是不是出手或位移生效那一拍。</summary>
        public static bool IsHitMarker(string key) =>
            key == Hit || key == CommitDash || key == CommitBlink || key == CommitJump;

        /// <summary>是不是施法阶段标记。</summary>
        public static bool IsPhaseClip(string key) =>
            key == Windup || key == Hit || key == Recovery || key == End
            || key == ChannelStart || key == ChannelEnd
            || key == ChargeStart || key == ChargeRelease;

        public static bool IsAcceptWindow(string key) => key == AcceptInput;
        public static bool IsCancelWindow(string key) => key == AllowCancel;

        /// <summary>持续时长。没填就用默认。</summary>
        public static float ResolveClipDuration(float duration) =>
            duration > 0f ? duration : SkillTrackId.DefaultClipDuration;

        /// <summary>按事件名字把轨道和时长补齐。</summary>
        public static void NormalizeCastEvents(SkillTimelineEventNode[] events)
        {
            if (events == null) return;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                if (IsPhaseClip(e.Key)) e.Track = SkillTrackId.Phase;
                if (e.Key == AllowCancel) e.Track = SkillTrackId.WindowCancel;
                else if (e.Key == AcceptInput) e.Track = SkillTrackId.WindowAccept;
                if (e.Duration <= 0f)
                    e.Duration = SkillTrackId.DefaultClipDuration;
            }
        }
    }
}
