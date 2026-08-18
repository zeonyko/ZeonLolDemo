using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>技能时间轴：配置表读完后，这里按需整理成播放用的事件表。</summary>
    public static class SkillTimelineLoader
    {
        #region 播放表缓存
        private static readonly Dictionary<int, SkillPlaybackConfig> PlaybackMap =
            new Dictionary<int, SkillPlaybackConfig>();
        #endregion

        #region 公开 API
        /// <summary>取技能播放表；没有就现整理一份。</summary>
        public static SkillPlaybackConfig GetPlayback(int skillId)
        {
            if (PlaybackMap.TryGetValue(skillId, out var cfg))
                return cfg;
            return TryBuildPlayback(skillId);
        }

        /// <summary>配置表重载后清播放表，下次再整理。</summary>
        public static void ResetPlayback() => PlaybackMap.Clear();
        #endregion

        #region 整理播放表
        /// <summary>合并表现事件和逻辑位移提交，整理成一份播放表。</summary>
        private static SkillPlaybackConfig TryBuildPlayback(int skillId)
        {
            var logic = SkillCatalog.Get(skillId);
            if (logic == null) return null;

            SkillPresentationCatalog.TryGet(skillId, out var presentation);
            var events = new List<PlaybackEvent>(32);
            AppendPlaybackEvents(events, presentation?.Clips);
            // 逻辑位移提交也要进播放表，否则多段冲刺本地只靠跟服务器对齐，看起来像一段
            AppendLogicMotionCommitEvents(events, logic.Clips);
            AppendLogicRecoveryEvents(events, logic.Clips);

            events.Sort((a, b) =>
            {
                int byTime = a.Time.CompareTo(b.Time);
                return byTime != 0 ? byTime : ((int)a.Track).CompareTo((int)b.Track);
            });

            var logicEvents = SkillClipUtil.ToTimelineEvents(logic.Clips);
            BakePhaseWindows(logicEvents, out var accept, out var cancel);

            var cfg = new SkillPlaybackConfig
            {
                SkillId = logic.SkillId,
                Name = logic.Name,
                CastRange = logic.CastRange,
                Cooldown = logic.Cooldown,
                CostMana = logic.CostMana,
                Presentation = presentation,
                Events = events.ToArray(),
                AcceptWindows = accept,
                CancelWindows = cancel
            };
            PlaybackMap[skillId] = cfg;
            return cfg;
        }

        /// <summary>把表现片段转成播放事件，追加到列表。</summary>
        static void AppendPlaybackEvents(List<PlaybackEvent> list, SkillClip[] clips)
        {
            if (clips == null || list == null) return;
            var nodes = SkillClipUtil.ToTimelineEvents(clips);
            for (int i = 0; i < nodes.Length; i++)
            {
                var e = nodes[i];
                list.Add(new PlaybackEvent(
                    e.Time,
                    (PlaybackTrack)e.Track,
                    e.Key,
                    e.Param,
                    e.Duration));
            }
        }

        /// <summary>把逻辑里的冲刺/闪现/跳跃交给位移播放，方便本地先动。表现里已有同时间同事件则跳过。</summary>
        static void AppendLogicMotionCommitEvents(List<PlaybackEvent> list, SkillClip[] logicClips)
        {
            if (logicClips == null || list == null) return;
            for (int i = 0; i < logicClips.Length; i++)
            {
                var c = logicClips[i];
                if (c == null) continue;

                string key = c.Key;
                if (string.IsNullOrEmpty(key) && c.Motion != null && c.Motion.HasContent)
                    key = SkillMotionCodec.KeyFromMotionType(c.Motion.Type);
                if (!SkillMotionCodec.IsMotionCommitKey(key)) continue;

                string param = c.Param;
                if (!SkillMotionCodec.TryParse(param, out _, out _, out _)
                    && c.Motion != null && c.Motion.HasContent)
                {
                    param = SkillMotionCodec.Encode(
                        c.Motion.Distance,
                        (ESkillMotionTargetType)c.Motion.TargetType,
                        (ESkillMotionCollisionPolicy)c.Motion.CollisionPolicy);
                }

                if (!SkillMotionCodec.TryParse(param, out _, out _, out _))
                    continue;

                if (HasPlaybackEvent(list, c.Time, key))
                    continue;

                float dur = c.Duration > 0f ? c.Duration : SkillTrackId.DefaultClipDuration;
                list.Add(new PlaybackEvent(c.Time, PlaybackTrack.Motion, key, param, dur));
            }
        }

        /// <summary>逻辑后摇进播放表，到点通知施法进入 Recovery（交还朝向，走位可切出技能态）。</summary>
        static void AppendLogicRecoveryEvents(List<PlaybackEvent> list, SkillClip[] logicClips)
        {
            if (logicClips == null || list == null) return;
            for (int i = 0; i < logicClips.Length; i++)
            {
                var c = logicClips[i];
                if (c == null || c.Key != SkillTimelineKeys.Recovery) continue;
                if (HasPlaybackEventOnTrack(list, PlaybackTrack.Phase, c.Time, c.Key))
                    continue;
                float dur = c.Duration > 0f ? c.Duration : SkillTrackId.DefaultClipDuration;
                list.Add(new PlaybackEvent(c.Time, PlaybackTrack.Phase, c.Key, c.Param ?? "", dur));
            }
        }

        /// <summary>是否已有同时间、同事件的位移轨，避免播两次。</summary>
        static bool HasPlaybackEvent(List<PlaybackEvent> list, float time, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Track != PlaybackTrack.Motion) continue;
                if (e.Key != key) continue;
                if (Mathf.Abs(e.Time - time) <= 0.001f)
                    return true;
            }
            return false;
        }

        static bool HasPlaybackEventOnTrack(List<PlaybackEvent> list, PlaybackTrack track, float time, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Track != track) continue;
                if (e.Key != key) continue;
                if (Mathf.Abs(e.Time - time) <= 0.001f)
                    return true;
            }
            return false;
        }

        /// <summary>从逻辑事件里抽出接招窗和取消窗。</summary>
        private static void BakePhaseWindows(
            SkillTimelineEventNode[] logicEvents,
            out CastTimeWindow[] accept,
            out CastTimeWindow[] cancel)
        {
            var acceptList = new List<CastTimeWindow>(2);
            var cancelList = new List<CastTimeWindow>(2);
            if (logicEvents == null)
            {
                accept = System.Array.Empty<CastTimeWindow>();
                cancel = System.Array.Empty<CastTimeWindow>();
                return;
            }

            for (int i = 0; i < logicEvents.Length; i++)
            {
                var e = logicEvents[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                if (e.Track != SkillTrackId.Phase
                    && e.Track != SkillTrackId.WindowCancel
                    && e.Track != SkillTrackId.WindowAccept)
                    continue;

                float start = e.Time;
                float end = start + (e.Duration > 0f ? e.Duration : SkillTrackId.DefaultClipDuration);
                var window = new CastTimeWindow { Start = start, End = end };
                if (SkillTimelineKeys.IsAcceptWindow(e.Key))
                    acceptList.Add(window);
                else if (SkillTimelineKeys.IsCancelWindow(e.Key))
                    cancelList.Add(window);
            }

            accept = acceptList.Count > 0 ? acceptList.ToArray() : System.Array.Empty<CastTimeWindow>();
            cancel = cancelList.Count > 0 ? cancelList.ToArray() : System.Array.Empty<CastTimeWindow>();
        }
        #endregion
    }
}
