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
    /// <summary>技能时间轴 ↔ Skill_{id}.json。写入仓库 Config/Skills/，再 Tools/sync_config-同步配置表.bat 分发到 Client / Server。</summary>
    public static class SkillExporter
    {
        #region 路径 & 初始化
        public const string SkillsDir = "Assets/Game/SkillData/Skills";
        public static string AuthoritativeJsonDir => BattlePaths.RepoConfig("Skills");
        public static string ClientJsonDir => AuthoritativeJsonDir;

        public static void EnsureFolders() =>
            ExportPipeline.EnsureFolders(SkillsDir, ClientJsonDir);
        #endregion

        #region 配置构建：SkillAsset + Timeline → SkillConfig
        /// <summary>从 SkillAsset 字段与 Timeline 上的 Clip 合并出完整 SkillConfig。</summary>
        public static SkillConfig BuildConfigFromAsset(SkillAsset asset)
        {
            var clips = CollectClips(asset.Timeline);
            var cfg = asset.ToConfig(clips);
            SkillClipUtil.Normalize(cfg);
            return cfg;
        }

        /// <summary>遍历 Timeline 各轨道，把 Hit/Motion/普通事件 Clip 逐个转成 SkillClip 并按时间排序。</summary>
        public static SkillClip[] CollectClips(TimelineAsset timeline)
        {
            var list = new List<SkillClip>();
            if (timeline == null)
                return Array.Empty<SkillClip>();

            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is SkillEventTrack skillTrack))
                    continue;

                int trackId = skillTrack.TrackId;
                foreach (var clip in track.GetClips())
                    CollectOneClip(list, clip, trackId);
            }

            list.Sort((a, b) =>
            {
                int byTime = a.Time.CompareTo(b.Time);
                return byTime != 0 ? byTime : a.Track.CompareTo(b.Track);
            });
            return list.ToArray();
        }

        /// <summary>按 Clip 类型分发：判定 / 位移 / 普通事件。</summary>
        private static void CollectOneClip(List<SkillClip> list, TimelineClip clip, int trackId)
        {
            if (clip.asset is SkillHitPhaseClip hitPhase)
            {
                CollectHitClip(list, clip, hitPhase);
                return;
            }

            if (clip.asset is SkillMotionClip motionClip)
            {
                CollectMotionClip(list, clip, motionClip);
                return;
            }

            CollectEventClip(list, clip, trackId);
        }

        private static void CollectHitClip(List<SkillClip> list, TimelineClip clip, SkillHitPhaseClip hitPhase)
        {
            list.Add(new SkillClip
            {
                Time = (float)clip.start,
                Duration = (float)clip.duration,
                Track = SkillTrackId.Phase,
                Key = SkillTimelineKeys.Hit,
                Param = "",
                Hit = hitPhase.ToPayload()
            });
        }

        private static void CollectMotionClip(List<SkillClip> list, TimelineClip clip, SkillMotionClip motionClip)
        {
            list.Add(new SkillClip
            {
                Time = (float)clip.start,
                Duration = (float)clip.duration,
                Track = SkillTrackId.Motion,
                Key = motionClip.CommitKey,
                Param = motionClip.EncodeParam(),
                Motion = motionClip.ToPayload()
            });
        }

        private static void CollectEventClip(List<SkillClip> list, TimelineClip clip, int trackId)
        {
            var skillClip = clip.asset as SkillEventClip;
            list.Add(new SkillClip
            {
                Time = (float)clip.start,
                Duration = (float)clip.duration,
                Track = trackId,
                Key = skillClip != null ? (skillClip.Key ?? "") : "",
                Param = skillClip != null ? (skillClip.Param ?? "") : ""
            });
        }
        #endregion

        #region 导出：SkillAsset → Skill_{id}.json
        /// <summary>构建配置，拆开逻辑和表现，写入技能配置表。</summary>
        public static bool ExportAsset(SkillAsset asset, out string error)
        {
            error = null;
            if (asset == null)
            {
                error = "SkillAsset is null";
                return false;
            }

            EnsureFolders();
            var cfg = BuildConfigFromAsset(asset);
            var split = LogicPresentationSplit.SplitSkill(cfg);
            if (split?.Presentation != null)
            {
                split.Presentation.SuppressCastBurst = asset.SuppressCastBurst;
                split.Presentation.CastVfxModule = asset.CastVfxModule ?? "";
                split.Presentation.HitImpactModule = asset.HitImpactModule ?? "";
                split.Presentation.DefaultAnimKey = asset.DefaultAnimKey ?? "";
            }
            string fileName = $"Skill_{cfg.SkillId}.json";

            string clientJson = SkillConfigWriter.ToPrettyJsonClient(split.Logic, split.Presentation);

            ExportPipeline.WriteJson(ClientJsonDir, fileName, clientJson);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }
        #endregion

        #region 反向同步：Skill_{id}.json → SkillAsset + Timeline
        /// <summary>从配置 JSON 重建时间轴。默认不同步完立刻回写，免得空片段把表写空。</summary>
        public static int SyncAssetsFromJson(bool reExport = false)
        {
            EnsureFolders();
            ClearSkillBundles(SkillsDir);

            string jsonDir = ExportPipeline.Abs(ClientJsonDir);
            if (!Directory.Exists(jsonDir))
            {
                Debug.LogError("[SkillSync] JSON 目录不存在: " + jsonDir);
                return 0;
            }

            var files = Directory.GetFiles(jsonDir, "Skill_*.json");
            Array.Sort(files);
            int ok = 0;
            int hitClips = 0;

            foreach (var path in files)
            {
                var file = JsonUtility.FromJson<SkillConfigFile>(File.ReadAllText(path));
                if (file?.Logic == null || file.Logic.SkillId <= 0)
                {
                    var flat = JsonUtility.FromJson<SkillConfig>(File.ReadAllText(path));
                    if (flat == null || flat.SkillId <= 0) continue;
                    file = LogicPresentationSplit.SplitSkill(flat);
                }

                var cfg = file.Logic;
                var mergedClips = LogicPresentationSplit.MergeSkillClips(
                    file.Logic?.Clips,
                    file.Presentation?.Clips);
                cfg.Clips = mergedClips;

                SkillClipUtil.Normalize(cfg);

                int id = cfg.SkillId;
                int expectHits = CountHitPayloads(cfg.Clips);
                string assetPath = $"{SkillsDir}/Skill_{id}.asset";
                string tlPath = $"{SkillsDir}/Skill_{id}.playable";

                var tlAsset = CreateOrLoadTimeline(tlPath);
                var asset = ScriptableObject.CreateInstance<SkillAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);

                asset.ApplyFromConfig(cfg, file.Presentation);
                asset.Timeline = tlAsset;
                int placed = PopulateTimeline(tlAsset, cfg.Clips);
                hitClips += placed;

                if (expectHits > 0 && placed < expectHits)
                {
                    Debug.LogWarning(
                        $"[SkillSync] Skill_{id} JSON 含 {expectHits} 段 Hit，Timeline 仅放入 {placed}。" +
                        "请检查逻辑轨 SkillHitPhaseClip。");
                }

                EditorUtility.SetDirty(tlAsset);
                EditorUtility.SetDirty(asset);

                if (reExport)
                    ExportAsset(asset, out _);

                ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"<color=cyan>[SkillSync] 全新重建 {ok}/{files.Length}，判定 Clip×{hitClips} → {SkillsDir}</color>");
            return ok;
        }

        /// <summary>统计 Clip 数组中判定(Hit)段的数量，用于同步后校验 Timeline 是否漏放。</summary>
        static int CountHitPayloads(SkillClip[] clips)
        {
            if (clips == null) return 0;
            int n = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null) continue;
                if (c.Hit != null && c.Hit.HasContent) n++;
                else if (c.Key == SkillTimelineKeys.Hit) n++;
            }
            return n;
        }

        /// <summary>同步前清空旧的 Skill_*.asset / .playable，避免重建后残留孤儿资源。</summary>
        private static void ClearSkillBundles(string assetsDir)
        {
            string abs = ExportPipeline.Abs(assetsDir);
            if (!Directory.Exists(abs)) return;

            foreach (var f in Directory.GetFiles(abs, "Skill_*.*"))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
                    continue;
                AssetDatabase.DeleteAsset($"{assetsDir}/{name}");
            }
        }
        #endregion

        #region 新建技能资源
        /// <summary>创建一个带默认 Clip 模板的新 SkillAsset + Timeline。</summary>
        public static SkillAsset CreateSkillBundle(int skillId, string name)
        {
            EnsureFolders();
            string assetPath = $"{SkillsDir}/Skill_{skillId}.asset";
            string tlPath = $"{SkillsDir}/Skill_{skillId}.playable";

            var timeline = CreateOrLoadTimeline(tlPath);
            PopulateTimeline(timeline, DefaultClips());

            var asset = ScriptableObject.CreateInstance<SkillAsset>();
            asset.SkillId = skillId;
            asset.SkillName = name ?? $"Skill_{skillId}";
            asset.Timeline = timeline;
            AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }
        #endregion

        #region Timeline 资源 & 轨道结构
        public static TimelineAsset CreateOrLoadTimeline(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath);
            if (existing != null)
            {
                EnsureStandardTracks(existing);
                return existing;
            }

            var tl = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(tl, assetPath);
            EnsureStandardTracks(tl);
            EditorUtility.SetDirty(tl);
            return tl;
        }

        public static void EnsureStandardTracks(TimelineAsset tl)
        {
            if (tl == null) return;

            foreach (var root in tl.GetRootTracks().ToList())
                tl.DeleteTrack(root);

            tl.CreateTrack<SkillPhaseTrack>(null, "逻辑");
            tl.CreateTrack<SkillPhaseTrack>(null, "判定");
            tl.CreateTrack<SkillCancelWindowTrack>(null, "可取消");
            tl.CreateTrack<SkillAcceptWindowTrack>(null, "可接招");
            tl.CreateTrack<SkillMotionTrack>(null, "位移");
            tl.CreateTrack<SkillAnimTrack>(null, "动画");
            tl.CreateTrack<SkillAudioTrack>(null, "音频");
            tl.CreateTrack<SkillVfxTrack>(null, "特效");
            tl.CreateTrack<SkillCameraTrack>(null, "镜头");
        }
        #endregion

        #region Timeline 填充：SkillClip[] → 各轨道 Clip
        /// <returns>成功放入的 SkillHitPhaseClip 数量。</returns>
        public static int PopulateTimeline(TimelineAsset tl, SkillClip[] clips)
        {
            if (tl == null) return 0;
            EnsureStandardTracks(tl);

            foreach (var track in tl.GetOutputTracks())
            {
                foreach (var c in track.GetClips().ToList())
                    track.DeleteClip(c);
            }

            int hitPlaced = PlaceClips(tl, clips);

            double maxEnd = 0.5;
            foreach (var track in tl.GetOutputTracks())
                foreach (var c in track.GetClips())
                    maxEnd = Math.Max(maxEnd, c.end);

            tl.durationMode = TimelineAsset.DurationMode.FixedLength;
            var so = new SerializedObject(tl);
            var durProp = so.FindProperty("m_FixedDuration");
            if (durProp != null)
            {
                durProp.doubleValue = maxEnd + 0.1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return hitPlaced;
        }

        /// <summary>按 JSON 原时刻落盘。判定单独一条轨；同轨重叠则再开一条。</summary>
        private static int PlaceClips(TimelineAsset tl, SkillClip[] clips)
        {
            int hitPlaced = 0;
            if (clips == null) return 0;

            for (int i = 0; i < clips.Length; i++)
            {
                var src = clips[i];
                if (src == null || string.IsNullOrEmpty(src.Key)) continue;
                float time = src.Time;
                float dur = src.Duration > 0.001f ? src.Duration : SkillTrackId.DefaultClipDuration;
                bool isHit = (src.Track == SkillTrackId.Phase && src.Key == SkillTimelineKeys.Hit)
                             || (src.Hit != null && src.Hit.HasContent)
                             || src.Key == SkillTimelineKeys.Hit;

                if (isHit)
                {
                    if (PlaceHitClip(tl, src, time, dur))
                        hitPlaced++;
                    continue;
                }

                if (src.Track == SkillTrackId.Motion || SkillMotionCodec.IsMotionCommitKey(src.Key))
                {
                    PlaceMotionClip(tl, src, time, dur);
                    continue;
                }

                PlaceEventClip(tl, src, time, dur);
            }
            return hitPlaced;
        }

        /// <summary>在「判定」轨创建 SkillHitPhaseClip；成功写入返回 true。</summary>
        private static bool PlaceHitClip(TimelineAsset tl, SkillClip src, float time, float dur)
        {
            var phaseTrack = ExportPipeline.FindAvailableTrack(
                tl, SkillTrackId.Phase, time, dur, "判定");
            if (phaseTrack == null)
            {
                Debug.LogError("[SkillSync] 判定轨不存在，跳过判定 Clip");
                return false;
            }

            var hitClip = phaseTrack.CreateClip<SkillHitPhaseClip>();
            hitClip.start = time;
            hitClip.duration = dur;
            if (!(hitClip.asset is SkillHitPhaseClip hitAsset))
                return false;

            if (src.Hit != null && src.Hit.HasContent)
                hitAsset.ApplyFromPayload(src.Hit);
            else if ((hitAsset.ShapeHits == null || hitAsset.ShapeHits.Count == 0)
                     && (hitAsset.ProjectileSpawns == null || hitAsset.ProjectileSpawns.Count == 0)
                     && !hitAsset.HasAnyAreaEnabled)
            {
                Debug.LogWarning(
                    $"[SkillSync] t={time:F2} Key=Hit 但 JSON.Hit 为空，写入默认 Box 占位");
                hitAsset.ShapeHits.Add(new ShapeHitData
                {
                    Shape = new HitShapeData
                    {
                        ShapeType = HitShapeType.Box,
                        ShapeSize = new Vector3(3f, 2f, 0f)
                    }
                });
            }

            hitClip.displayName = FormatHitDisplayName(hitAsset);
            EditorUtility.SetDirty(hitAsset);
            return true;
        }

        /// <summary>在位移轨创建 SkillMotionClip。</summary>
        private static void PlaceMotionClip(TimelineAsset tl, SkillClip src, float time, float dur)
        {
            var motionTrack = ExportPipeline.FindAvailableTrack(
                tl, SkillTrackId.Motion, time, dur, "位移");
            if (motionTrack == null) return;

            var motionClip = motionTrack.CreateClip<SkillMotionClip>();
            motionClip.start = time;
            motionClip.duration = dur;
            if (!(motionClip.asset is SkillMotionClip motionAsset))
                return;

            if (src.Motion != null && src.Motion.HasContent)
                motionAsset.ApplyFromPayload(src.Motion, src.Key);
            else
                motionAsset.ApplyFromEvent(src.Key, src.Param);
            if (motionAsset.Distance <= 0f)
                motionAsset.Distance = 5f;
            motionClip.displayName = motionAsset.CommitKey;
            EditorUtility.SetDirty(motionAsset);
        }

        /// <summary>在对应事件轨创建 SkillEventClip。</summary>
        private static void PlaceEventClip(TimelineAsset tl, SkillClip src, float time, float dur)
        {
            string prefer = src.Track == SkillTrackId.Phase
                ? "逻辑"
                : ExportPipeline.DefaultTrackName(src.Track);
            var track = ExportPipeline.FindAvailableTrack(tl, src.Track, time, dur, prefer);
            if (track == null) return;

            var clip = track.CreateClip<SkillEventClip>();
            clip.start = time;
            clip.duration = dur;
            clip.displayName = SkillEventKeys.ClipTitle(src.Key, src.Param);
            if (clip.asset is SkillEventClip skillClip)
            {
                skillClip.Key = src.Key ?? "";
                skillClip.Param = src.Param ?? "";
                EditorUtility.SetDirty(skillClip);
            }
        }

        /// <summary>按判定内容（区域/弹道/普通攻击类型）拼出 Timeline 上显示的判定名。</summary>
        static string FormatHitDisplayName(SkillHitPhaseClip hit)
        {
            if (hit == null) return "判定";
            if (hit.HasAnyAreaEnabled)
                return string.IsNullOrEmpty(hit.AttackType) ? "判定/区域" : $"判定/区域/{hit.AttackType}";
            if (hit.ProjectileSpawns != null && hit.ProjectileSpawns.Count > 0
                && (hit.ShapeHits == null || hit.ShapeHits.Count == 0))
                return string.IsNullOrEmpty(hit.AttackType) ? "判定/弹道" : $"判定/弹道/{hit.AttackType}";
            return string.IsNullOrEmpty(hit.AttackType) ? "判定" : $"判定/{hit.AttackType}";
        }
        #endregion

        #region 默认技能模板
        private static SkillClip[] DefaultClips() => new[]
        {
            Clip(0f, 0.2f, SkillTrackId.Phase, SkillTimelineKeys.Windup),
            Clip(0f, 0.55f, SkillTrackId.Anim, SkillTimelineKeys.Play, "Attack3"),
            new SkillClip
            {
                Time = 0.2f,
                Duration = SkillTrackId.DefaultClipDuration,
                Track = SkillTrackId.Phase,
                Key = SkillTimelineKeys.Hit,
                Hit = new SkillHitPayload
                {
                    AttackType = AttackStyles.HeavySlash,
                    CasterFeedbackType = CasterReactionIds.HeavyImpact,
                    ShapeHits = new[]
                    {
                        new SkillShapeHitPayload
                        {
                            Anchor = TargetSelectorData.CasterSelf(),
                            Shape = new SkillShapePayload
                            {
                                ShapeType = (int)HitShapeType.Box,
                                SizeX = 3f,
                                SizeY = 2f
                            },
                            Effect = new HitEffectPayload
                            {
                                Damage = DamagePayload.DefaultInstant(1f),
                                TargetBuffs = System.Array.Empty<BuffApplySpec>()
                            },
                            Feedback = new HitFeedbackSignal
                            {
                                AttackType = AttackStyles.HeavySlash,
                                CasterFeedbackType = CasterReactionIds.HeavyImpact
                            }
                        }
                    }
                }
            },
            Clip(0.2f, 0.35f, SkillTrackId.Phase, SkillTimelineKeys.Recovery),
            Clip(0.3f, 0.25f, SkillTrackId.WindowAccept, SkillTimelineKeys.AcceptInput),
            Clip(0.35f, 0.25f, SkillTrackId.WindowCancel, SkillTimelineKeys.AllowCancel),
            Clip(0.55f, SkillTrackId.DefaultClipDuration, SkillTrackId.Phase, SkillTimelineKeys.End),
        };

        private static SkillClip Clip(float time, float duration, int track, string key, string param = "")
        {
            return new SkillClip
            {
                Time = time,
                Duration = duration,
                Track = track,
                Key = key ?? "",
                Param = param ?? ""
            };
        }
        #endregion

        #region 编辑器辅助
        /// <summary>在 Timeline 窗口中打开并选中指定 Timeline 资源。</summary>
        public static void OpenTimeline(TimelineAsset timeline) =>
            ExportPipeline.OpenTimeline(timeline);
        #endregion
    }

    /// <summary>SkillAsset 的 Inspector：默认字段 + 打开 Timeline / 导出 JSON 按钮。</summary>
    [CustomEditor(typeof(SkillAsset))]
    public class SkillAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (SkillAsset)target;

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "规则字段 + Timeline。判定用「判定」轨上的 SkillHitPhaseClip，位移用「位移」轨。\n" +
                "双向：Game/Skill → 从 Config 同步全部 / 导出全部到 Config。",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(asset.Timeline == null))
            {
                if (GUILayout.Button("打开 Timeline", GUILayout.Height(30)))
                    SkillExporter.OpenTimeline(asset.Timeline);
            }

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.35f, 0.75f, 0.45f);
            if (GUILayout.Button("导出此技能 JSON", GUILayout.Height(32)))
            {
                if (SkillExporter.ExportAsset(asset, out string err))
                    EditorUtility.DisplayDialog("导出", $"Skill_{asset.SkillId}.json", "OK");
                else
                    EditorUtility.DisplayDialog("导出", err, "OK");
            }
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>Game/Skill 菜单：创建新技能 / JSON↔Timeline 双向同步 / 批量导出。</summary>
    public static class SkillTimelineMenus
    {
        [MenuItem("Game/Skill/创建新技能", false, 0)]
        public static void CreateNewSkill()
        {
            SkillExporter.EnsureFolders();
            int id = 2000;
            while (AssetDatabase.LoadAssetAtPath<SkillAsset>(
                       $"{SkillExporter.SkillsDir}/Skill_{id}.asset") != null)
                id++;

            var asset = SkillExporter.CreateSkillBundle(id, $"新技能_{id}");
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            SkillExporter.OpenTimeline(asset.Timeline);
        }

        /// <summary>从 Configs JSON 全新重建所有技能资源（Editor 菜单入口）。</summary>
        [MenuItem("Game/Skill/从 JSON 同步技能", false, 1)]
        public static void SyncAssetsFromJsonMenu()
        {
            int ok = SkillExporter.SyncAssetsFromJson(reExport: false);
            EditorUtility.DisplayDialog(
                "同步技能",
                $"已全新重建 {ok} 个技能资源\n目录：{SkillExporter.SkillsDir}\n\n" +
                "判定在「判定」轨。同刻的特效会自动分到「特效 2」。\n" +
                "改完用「Game/Skill/导出全部到 Config」写回权威源。",
                "OK");
        }

        /// <summary>批量导出 SkillsDir 下全部 SkillAsset 到 Skill_{id}.json。</summary>
        [MenuItem("Game/Skill/导出全部技能 JSON", false, 2)]
        public static void ExportAllSkills()
        {
            int ok = ExportPipeline.ExportAllAssets<SkillAsset>(
                SkillExporter.SkillsDir, "t:SkillAsset",
                a => SkillExporter.ExportAsset(a, out _));
            EditorUtility.DisplayDialog("导出技能", $"已导出 {ok} 个", "OK");
        }

        /// <summary>供 -batchmode -executeMethod 调用；失败时抛异常使批处理返回非 0。</summary>
        public static void SyncAssetsFromJsonBatch()
        {
            int ok = SkillExporter.SyncAssetsFromJson();
            if (ok <= 0)
                throw new Exception("[SkillSync] failed");
            Debug.Log($"[SkillSync] batch ok={ok}");
        }
    }
}
