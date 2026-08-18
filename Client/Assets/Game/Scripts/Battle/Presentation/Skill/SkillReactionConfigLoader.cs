using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>受击/反馈配置：从 JSON 整理成播放表。</summary>
    public static class SkillReactionConfigLoader
    {
        #region 缓存
        private static readonly Dictionary<string, SkillReactionPlayConfig> Map =
            new Dictionary<string, SkillReactionPlayConfig>();
        #endregion

        #region 公开 API
        /// <summary>按反馈 Id 查播放表；没有返回 null。</summary>
        public static SkillReactionPlayConfig Get(string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId)) return null;
            Map.TryGetValue(reactionId, out var cfg);
            return cfg;
        }

        /// <summary>重载全部：分别扫目标受击和施法者反馈两个目录。</summary>
        public static int LoadAll()
        {
            Map.Clear();
            int n = 0;
            n += LoadDir(ConfigPath.Combine("Skills", "Reactions", "TargetHit"));
            n += LoadDir(ConfigPath.Combine("Skills", "Reactions", "CasterFeedback"));
            Debug.Log($"<color=cyan>[SkillReaction] loaded={n}</color>");
            return n;
        }
        #endregion

        #region 加载并整理
        /// <summary>加载一个目录下的全部反馈 JSON，整理后写入缓存。</summary>
        private static int LoadDir(string relativeDir)
        {
            int n = 0;
            foreach (var (path, raw) in ConfigService.LoadFilesWithPath<SkillReactionConfig>(
                         relativeDir, "*.json"))
            {
                if (raw == null || string.IsNullOrEmpty(raw.ReactionId))
                    continue;

                var cfg = Bake(raw);
                Map[cfg.ReactionId] = cfg;
                n++;
                Debug.Log($"[SkillReaction] {ConfigPath.FileName(path)} id={cfg.ReactionId}");
            }
            return n;
        }

        /// <summary>把顿帧和动作列表整理成按时间排序的播放事件。</summary>
        private static SkillReactionPlayConfig Bake(SkillReactionConfig raw)
        {
            var list = new List<PlaybackEvent>(8);

            if (raw.HitStop > 0f)
            {
                list.Add(new PlaybackEvent(
                    0f, PlaybackTrack.HitStop, SkillTimelineKeys.HitStop, "", raw.HitStop));
            }

            if (raw.Actions != null)
            {
                for (int i = 0; i < raw.Actions.Length; i++)
                {
                    var a = raw.Actions[i];
                    if (a == null || string.IsNullOrEmpty(a.Type) || a.Type == "HitStop")
                        continue;
                    if (!TryMapTrack(a.Type, out var track))
                        continue;

                    // Airborne / StatusPopup 用 Type 作 Key，供 VfxHandler 分发
                    string key = IsModuleAction(a.Type) ? a.Type : SkillTimelineKeys.Play;
                    list.Add(new PlaybackEvent(
                        a.Time,
                        track,
                        key,
                        a.Param ?? "",
                        a.Duration > 0f
                            ? a.Duration
                            : Mathf.Max(SkillTrackId.DefaultClipDuration, raw.Duration - a.Time)));
                }
            }

            list.Sort((x, y) =>
            {
                int byTime = x.Time.CompareTo(y.Time);
                return byTime != 0 ? byTime : ((int)x.Track).CompareTo((int)y.Track);
            });
            return new SkillReactionPlayConfig
            {
                ReactionId = raw.ReactionId,
                Duration = raw.Duration,
                Events = list.ToArray()
            };
        }

        #endregion

        #region Action 类型 → Track 映射
        /// <summary>把配表动作类型对应到播放轨道；不认识返回 false。</summary>
        private static bool TryMapTrack(string type, out PlaybackTrack track)
        {
            switch (type)
            {
                case "Anim": track = PlaybackTrack.Anim; return true;
                case "Audio": track = PlaybackTrack.Audio; return true;
                case "Vfx": track = PlaybackTrack.Vfx; return true;
                case "Camera": track = PlaybackTrack.Camera; return true;
                case "Displacement": track = PlaybackTrack.Motion; return true;
                case "Airborne": track = PlaybackTrack.Vfx; return true;
                case "StatusPopup": track = PlaybackTrack.Vfx; return true;
                default:
                    track = default;
                    return false;
            }
        }

        /// <summary>击飞/状态飘字这类模块，用类型名本身当播放键。</summary>
        static bool IsModuleAction(string type) =>
            type == "Airborne" || type == "StatusPopup";
        #endregion
    }
}
