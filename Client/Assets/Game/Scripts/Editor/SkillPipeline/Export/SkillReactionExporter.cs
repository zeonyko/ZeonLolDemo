using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Battle;
using Client.SkillAuthoring;
using Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Client.EditorTools
{
    /// <summary>反馈时间轴：只有 .playable，文件名就是反馈 Id。只管动作/音效/特效/镜头/位移/顿帧。</summary>
    public static class SkillReactionExporter
    {
        #region 路径 & 初始化
        public const string TargetEditorDir = "Assets/Game/SkillData/Reactions/TargetHit";
        public const string CasterEditorDir = "Assets/Game/SkillData/Reactions/CasterFeedback";
        public static string TargetJsonDir => BattlePaths.Config("Skills", "Reactions", "TargetHit");
        public static string CasterJsonDir => BattlePaths.Config("Skills", "Reactions", "CasterFeedback");

        public static void EnsureFolders() =>
            ExportPipeline.EnsureFolders(TargetEditorDir, CasterEditorDir, TargetJsonDir, CasterJsonDir);

        public static bool IsTargetFolder(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').Contains("/Reactions/TargetHit/");

        public static bool IsCasterFolder(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').Contains("/Reactions/CasterFeedback/");
        #endregion

        #region 新建 & 导出
        /// <summary>创建一个带默认演示 Action 的新 Reaction Timeline（未落盘 JSON）。</summary>
        public static TimelineAsset CreateReaction(string reactionId, bool casterFeedback)
        {
            EnsureFolders();
            string folder = casterFeedback ? CasterEditorDir : TargetEditorDir;
            string safeId = string.IsNullOrEmpty(reactionId) ? "Reaction_New" : reactionId;
            string tlPath = $"{folder}/{safeId}.playable";

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tlPath) != null)
                AssetDatabase.DeleteAsset(tlPath);

            var timeline = CreateOrLoadTimeline(tlPath);
            PopulateFromConfig(timeline, new SkillReactionConfig
            {
                ReactionId = safeId,
                Duration = 0.3f,
                HitStop = casterFeedback ? 0.03f : 0.05f,
                Actions = casterFeedback
                    ? new[]
                    {
                        new SkillReactionAction { Time = 0f, Type = "Audio", Param = "Sfx_Hit", Duration = 0f }
                    }
                    : new[]
                    {
                        new SkillReactionAction { Time = 0f, Type = "Vfx", Param = "Vfx_Hit", Duration = 0f },
                        new SkillReactionAction { Time = 0f, Type = "Anim", Param = "HitReact", Duration = 0.25f }
                    }
            });

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return timeline;
        }

        /// <summary>把 Timeline 上的表现轨 Clip 转为 SkillReactionConfig 并写入对应 JSON 目录。</summary>
        public static bool ExportTimeline(TimelineAsset timeline, out string error)
        {
            error = null;
            if (timeline == null)
            {
                error = "Timeline 为空";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(timeline);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "无法解析 Timeline 路径";
                return false;
            }

            bool isCaster = IsCasterFolder(assetPath);
            bool isTarget = IsTargetFolder(assetPath);
            if (!isCaster && !isTarget)
            {
                error = "请将 Reaction 放在 SkillData/Reactions/TargetHit 或 CasterFeedback";
                return false;
            }

            EnsureFolders();
            string id = Path.GetFileNameWithoutExtension(assetPath);
            var cfg = BuildConfig(timeline, id);
            string dir = isCaster ? CasterJsonDir : TargetJsonDir;
            File.WriteAllText(Path.Combine(ExportPipeline.Abs(dir), $"{id}.json"), JsonUtility.ToJson(cfg, true));
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>遍历 Timeline 各表现轨，把 Clip 逐个转成 SkillReactionAction 并统计 HitStop/总时长。</summary>
        public static SkillReactionConfig BuildConfig(TimelineAsset timeline, string reactionId)
        {
            var actions = new List<SkillReactionAction>();
            float maxEnd = 0.1f;
            float hitStop = 0f;

            if (timeline != null)
            {
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is SkillEventTrack skillTrack)) continue;
                    if (!TryMapPresentTrack(skillTrack.TrackId, out string type))
                        continue;

                    foreach (var clip in track.GetClips())
                    {
                        string param = "";
                        if (clip.asset is SkillMotionClip motionClip)
                            param = motionClip.Distance.ToString("0.###",
                                System.Globalization.CultureInfo.InvariantCulture);
                        else if (clip.asset is SkillEventClip skillClip)
                            param = skillClip.Param ?? "";

                        float dur = (float)clip.duration;

                        if (type == "HitStop")
                        {
                            hitStop = Math.Max(hitStop, dur);
                            actions.Add(new SkillReactionAction
                            {
                                Time = (float)clip.start,
                                Type = "HitStop",
                                Param = "",
                                Duration = dur
                            });
                        }
                        else
                        {
                            actions.Add(new SkillReactionAction
                            {
                                Time = (float)clip.start,
                                Type = type,
                                Param = param,
                                Duration = dur
                            });
                        }

                        maxEnd = Math.Max(maxEnd, (float)clip.end);
                    }
                }
            }

            actions.Sort((a, b) => a.Time.CompareTo(b.Time));
            return new SkillReactionConfig
            {
                ReactionId = reactionId ?? "",
                Duration = maxEnd,
                HitStop = hitStop,
                Actions = actions.ToArray()
            };
        }

        /// <summary>轨道 Id → 导出用的 Action 类型字符串；未识别的轨道返回 false。</summary>
        public static bool TryMapPresentTrack(int trackId, out string actionType)
        {
            switch (trackId)
            {
                case SkillTrackId.Anim: actionType = "Anim"; return true;
                case SkillTrackId.Audio: actionType = "Audio"; return true;
                case SkillTrackId.Vfx: actionType = "Vfx"; return true;
                case SkillTrackId.Camera: actionType = "Camera"; return true;
                case SkillTrackId.Motion: actionType = "Displacement"; return true;
                case SkillTrackId.HitStop: actionType = "HitStop"; return true;
                default:
                    actionType = null;
                    return false;
            }
        }

        /// <summary>批量导出 TargetHit + CasterFeedback 两个目录下的全部 Reaction Timeline。</summary>
        public static int ExportAll()
        {
            EnsureFolders();
            return ExportFolder(TargetEditorDir) + ExportFolder(CasterEditorDir);
        }

        private static int ExportFolder(string editorDir)
        {
            int ok = 0;
            string[] guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { editorDir });
            foreach (var g in guids)
            {
                var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (tl != null && ExportTimeline(tl, out _))
                    ok++;
            }
            return ok;
        }
        #endregion

        #region 反向同步：Reaction JSON → Timeline
        /// <summary>清空后从 JSON 全新重建 TargetHit + CasterFeedback 两个目录下的 Reaction Timeline。</summary>
        public static int SyncFromJson()
        {
            EnsureFolders();
            ClearFolderTimelines(TargetEditorDir);
            ClearFolderTimelines(CasterEditorDir);

            int ok = SyncDir(ExportPipeline.Abs(TargetJsonDir), TargetEditorDir)
                     + SyncDir(ExportPipeline.Abs(CasterJsonDir), CasterEditorDir);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"<color=cyan>[SkillReaction] SyncFromJson ok={ok}</color>");
            return ok;
        }

        /// <summary>同步前清空目录下所有旧 Timeline，避免重建后残留孤儿资源。</summary>
        private static void ClearFolderTimelines(string editorDir)
        {
            if (!AssetDatabase.IsValidFolder(editorDir)) return;
            string[] guids = AssetDatabase.FindAssets("t:TimelineAsset", new[] { editorDir });
            foreach (var g in guids)
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(g));
        }

        private static int SyncDir(string jsonDir, string editorDir)
        {
            if (!Directory.Exists(jsonDir)) return 0;
            int ok = 0;
            foreach (var path in Directory.GetFiles(jsonDir, "*.json"))
            {
                var cfg = JsonUtility.FromJson<SkillReactionConfig>(File.ReadAllText(path));
                if (cfg == null || string.IsNullOrEmpty(cfg.ReactionId)) continue;

                string tlPath = $"{editorDir}/{cfg.ReactionId}.playable";
                var timeline = CreateOrLoadTimeline(tlPath);
                PopulateFromConfig(timeline, cfg);
                EditorUtility.SetDirty(timeline);
                ok++;
            }
            return ok;
        }
        #endregion

        #region Timeline 资源 & 轨道结构
        public static TimelineAsset CreateOrLoadTimeline(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath);
            if (existing != null)
            {
                EnsurePipelineTracks(existing);
                return existing;
            }

            var tl = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(tl, assetPath);
            EnsurePipelineTracks(tl);
            EditorUtility.SetDirty(tl);
            return tl;
        }

        public static void EnsurePipelineTracks(TimelineAsset tl)
        {
            if (tl == null) return;
            foreach (var root in tl.GetRootTracks().ToList())
                tl.DeleteTrack(root);

            tl.CreateTrack<SkillHitStopTrack>(null, "卡肉");
            tl.CreateTrack<SkillAnimTrack>(null, "动画");
            tl.CreateTrack<SkillVfxTrack>(null, "特效");
            tl.CreateTrack<SkillAudioTrack>(null, "音效");
            tl.CreateTrack<SkillCameraTrack>(null, "镜头");
            tl.CreateTrack<SkillMotionTrack>(null, "位移");
        }
        #endregion

        #region Timeline 填充：SkillReactionConfig → 各轨道 Clip
        public static void PopulateFromConfig(TimelineAsset tl, SkillReactionConfig cfg)
        {
            if (tl == null || cfg == null) return;
            EnsurePipelineTracks(tl);

            foreach (var track in tl.GetOutputTracks())
            {
                foreach (var c in track.GetClips().ToList())
                    track.DeleteClip(c);
            }

            if (cfg.HitStop > 0.001f)
                PlaceOnTrack(tl, SkillTrackId.HitStop, 0f, cfg.HitStop, SkillTimelineKeys.HitStop, "");

            if (cfg.Actions != null)
            {
                foreach (var a in cfg.Actions)
                {
                    if (a == null || string.IsNullOrEmpty(a.Type) || a.Type == "HitStop") continue;

                    int trackId = a.Type switch
                    {
                        "Anim" => SkillTrackId.Anim,
                        "Audio" => SkillTrackId.Audio,
                        "Camera" => SkillTrackId.Camera,
                        "Displacement" => SkillTrackId.Motion,
                        _ => SkillTrackId.Vfx
                    };
                    float dur = a.Duration > 0.001f ? a.Duration : SkillTrackId.DefaultClipDuration;
                    PlaceOnTrack(tl, trackId, a.Time, dur, SkillTimelineKeys.Play, a.Param ?? "");
                }
            }

            double maxEnd = Math.Max(0.3, cfg.Duration);
            foreach (var track in tl.GetOutputTracks())
                foreach (var c in track.GetClips())
                    maxEnd = Math.Max(maxEnd, c.end);

            tl.durationMode = TimelineAsset.DurationMode.FixedLength;
            var so = new SerializedObject(tl);
            var durProp = so.FindProperty("m_FixedDuration");
            if (durProp != null)
            {
                durProp.doubleValue = maxEnd + 0.05;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>把单个 Action 放到目标轨道上，位移轨走 SkillMotionClip，其余走 SkillEventClip。</summary>
        private static void PlaceOnTrack(
            TimelineAsset tl, int trackId, float time, float duration, string key, string param)
        {
            string prefer = trackId switch
            {
                SkillTrackId.HitStop => "卡肉",
                SkillTrackId.Anim => "动画",
                SkillTrackId.Vfx => "特效",
                SkillTrackId.Audio => "音效",
                SkillTrackId.Camera => "镜头",
                SkillTrackId.Motion => "位移",
                _ => ExportPipeline.DefaultTrackName(trackId)
            };
            var track = ExportPipeline.FindAvailableTrack(tl, trackId, time, duration, prefer);
            if (track == null) return;

            if (trackId == SkillTrackId.Motion || track is SkillMotionTrack)
            {
                var motionClip = track.CreateClip<SkillMotionClip>();
                motionClip.start = time;
                motionClip.duration = duration > 0.001f ? duration : SkillTrackId.DefaultClipDuration;
                if (motionClip.asset is SkillMotionClip motionAsset)
                {
                    float dist = ParseDisplacementDistance(param);
                    motionAsset.MotionType = ESkillMotionClipType.Blink;
                    motionAsset.Distance = dist > 0f ? dist : 1.2f;
                    motionAsset.TargetType = ESkillMotionTargetType.InputDirection;
                    motionAsset.CollisionPolicy = ESkillMotionCollisionPolicy.PassThrough;
                    motionClip.displayName = $"击退 {motionAsset.Distance:0.##}";
                    EditorUtility.SetDirty(motionAsset);
                }
                return;
            }

            var clip = track.CreateClip<SkillEventClip>();
            clip.start = time;
            clip.duration = duration;
            clip.displayName = string.IsNullOrEmpty(param) ? key : param;
            if (clip.asset is SkillEventClip sc)
            {
                sc.Key = key ?? "";
                sc.Param = param ?? "";
                EditorUtility.SetDirty(sc);
            }
        }

        /// <summary>解析位移参数字符串（编码格式 / 纯数字 / 逗号分隔）取出击退距离。</summary>
        private static float ParseDisplacementDistance(string param)
        {
            if (SkillMotionCodec.TryParse(param, out float encoded, out _, out _))
                return encoded;
            if (string.IsNullOrEmpty(param)) return 0f;
            if (float.TryParse(param.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float plain))
                return plain;
            string[] parts = param.Split(',');
            string token = parts[parts.Length - 1].Trim();
            if (float.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float d))
                return d;
            return 0f;
        }
        #endregion

        #region 编辑器辅助
        /// <summary>在 Timeline 窗口中打开并选中指定 Timeline 资源。</summary>
        public static void OpenTimeline(TimelineAsset timeline) =>
            ExportPipeline.OpenTimeline(timeline);
        #endregion
    }

    /// <summary>Game/Skill 菜单：创建 / 同步 / 导出 Reaction Timeline。</summary>
    public static class SkillReactionMenus
    {
        [MenuItem("Game/Skill/创建受击 Reaction", false, 30)]
        public static void CreateTargetHit() => CreateAndPing(casterFeedback: false);

        [MenuItem("Game/Skill/创建施法者 Reaction", false, 31)]
        public static void CreateCasterFeedback() => CreateAndPing(casterFeedback: true);

        [MenuItem("Game/Skill/从 JSON 同步 Reaction", false, 32)]
        public static void SyncFromJson()
        {
            int ok = SkillReactionExporter.SyncFromJson();
            EditorUtility.DisplayDialog(
                "同步 Reaction",
                $"已同步 {ok} 个 Timeline\n{SkillReactionExporter.TargetEditorDir}\n{SkillReactionExporter.CasterEditorDir}",
                "OK");
        }

        [MenuItem("Game/Skill/导出全部 Reaction JSON", false, 33)]
        public static void ExportAll()
        {
            int ok = SkillReactionExporter.ExportAll();
            EditorUtility.DisplayDialog("导出 Reaction", $"已导出 {ok} 个到 Configs", "OK");
        }

        [MenuItem("Game/Skill/导出选中的 Reaction", false, 34)]
        public static void ExportSelected()
        {
            var tl = Selection.activeObject as TimelineAsset;
            if (tl == null)
            {
                EditorUtility.DisplayDialog("导出 Reaction", "请先选中一个 Reaction Timeline", "OK");
                return;
            }

            if (SkillReactionExporter.ExportTimeline(tl, out string err))
                EditorUtility.DisplayDialog("导出 Reaction", AssetDatabase.GetAssetPath(tl), "OK");
            else
                EditorUtility.DisplayDialog("导出 Reaction", err, "OK");
        }

        [MenuItem("Game/Skill/导出选中的 Reaction", true, 34)]
        public static bool ValidateExportSelected()
        {
            var tl = Selection.activeObject as TimelineAsset;
            if (tl == null) return false;
            string path = AssetDatabase.GetAssetPath(tl);
            return SkillReactionExporter.IsTargetFolder(path) || SkillReactionExporter.IsCasterFolder(path);
        }

        static void CreateAndPing(bool casterFeedback)
        {
            string folder = casterFeedback
                ? SkillReactionExporter.CasterEditorDir
                : SkillReactionExporter.TargetEditorDir;
            string prefix = casterFeedback ? "Caster" : "Hit";
            var timeline = SkillReactionExporter.CreateReaction(NextReactionId(folder, prefix), casterFeedback);
            Selection.activeObject = timeline;
            EditorGUIUtility.PingObject(timeline);
            SkillReactionExporter.OpenTimeline(timeline);
        }

        static string NextReactionId(string folder, string prefix)
        {
            int n = 1;
            while (true)
            {
                string id = $"{prefix}_{n:D2}";
                if (AssetDatabase.LoadAssetAtPath<TimelineAsset>($"{folder}/{id}.playable") == null)
                    return id;
                n++;
            }
        }
    }

    [InitializeOnLoad]
    static class SkillReactionTimelineHeader
    {
        static SkillReactionTimelineHeader()
        {
            Editor.finishedDefaultHeaderGUI += DrawExportButton;
        }

        static void DrawExportButton(Editor editor)
        {
            if (!(editor.target is TimelineAsset tl)) return;
            string path = AssetDatabase.GetAssetPath(tl);
            if (!SkillReactionExporter.IsTargetFolder(path) && !SkillReactionExporter.IsCasterFolder(path))
                return;

            EditorGUILayout.Space(4);
            if (GUILayout.Button("导出此 Reaction JSON", GUILayout.Height(24)))
            {
                if (SkillReactionExporter.ExportTimeline(tl, out string err))
                    EditorUtility.DisplayDialog("导出 Reaction", path, "OK");
                else
                    EditorUtility.DisplayDialog("导出 Reaction", err, "OK");
            }
        }
    }
}
