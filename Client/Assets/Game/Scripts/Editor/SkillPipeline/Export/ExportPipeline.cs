using System;
using System.Collections.Generic;
using System.IO;
using Client.SkillAuthoring;
using Shared;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Client.EditorTools
{
    /// <summary>编辑器导出公用读写：路径、写 JSON、批量导出、开时间轴窗口。Buff/弹道走完整流程；技能/反馈只复用读写。</summary>
    public static class ExportPipeline
    {
        #region 路径

        /// <summary>相对 Assets/ 或已是绝对路径，都转成磁盘绝对路径。</summary>
        public static string Abs(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            return Path.GetFullPath(Path.Combine(Application.dataPath, path));
        }

        public static void EnsureFolders(params string[] dirs)
        {
            if (dirs == null) return;
            for (int i = 0; i < dirs.Length; i++)
            {
                string d = dirs[i];
                if (!string.IsNullOrEmpty(d))
                    Directory.CreateDirectory(Abs(d));
            }
        }

        #endregion

        #region 写 JSON / 批量导出

        public static void WriteJson(string dir, string fileName, string json)
        {
            string absDir = Abs(dir);
            Directory.CreateDirectory(absDir);
            File.WriteAllText(Path.Combine(absDir, fileName), json);
        }

        /// <summary>按类型过滤器导出某目录下全部 Asset；export 返回是否成功。</summary>
        public static int ExportAllAssets<TAsset>(
            string assetsDir, string typeFilter, Func<TAsset, bool> export)
            where TAsset : UnityEngine.Object
        {
            EnsureFolders(assetsDir);
            string[] guids = AssetDatabase.FindAssets(typeFilter, new[] { assetsDir });
            int ok = 0;
            foreach (var g in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (asset != null && export(asset))
                    ok++;
            }
            return ok;
        }

        #endregion

        #region ScriptableObject 新建 / 覆盖

        public static TAsset CreateOrReplace<TAsset>(
            string assetPath, Action<TAsset> onExisting, Action<TAsset> onCreate)
            where TAsset : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
            if (existing != null)
            {
                onExisting?.Invoke(existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<TAsset>();
            onCreate?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        #endregion

        #region 双键 / 扁平 JSON 读入

        /// <summary>读目录下匹配文件。双键 JSON 拆逻辑+表现；否则当扁平逻辑表。</summary>
        public static void LoadJsonDir<TFile, TLogic>(
            string absDir,
            string pattern,
            Func<TFile, TLogic> logicOf,
            Action<TFile, TLogic> applyPresentation,
            Func<TLogic, int> idOf,
            Dictionary<int, TLogic> into,
            string logTag)
            where TFile : class
            where TLogic : class
        {
            if (string.IsNullOrEmpty(absDir) || !Directory.Exists(absDir) || into == null)
                return;

            foreach (var path in Directory.GetFiles(absDir, pattern))
            {
                try
                {
                    string text = File.ReadAllText(path);
                    TLogic logic = null;
                    if (LogicPresentationSplit.IsDualKeyJson(text))
                    {
                        var file = JsonUtility.FromJson<TFile>(text);
                        logic = file != null ? logicOf(file) : null;
                        if (logic != null)
                            applyPresentation?.Invoke(file, logic);
                    }
                    else
                    {
                        logic = JsonUtility.FromJson<TLogic>(text);
                    }

                    if (logic == null) continue;
                    int id = idOf(logic);
                    if (id == 0) continue;
                    into[id] = logic;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[{logTag}] 解析失败 {path}: {e.Message}");
                }
            }
        }

        #endregion

        #region Timeline 开窗 / 落轨 / 同步运行时副本

        public static void OpenTimeline(TimelineAsset timeline)
        {
            if (timeline == null) return;
            Selection.activeObject = timeline;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            EditorGUIUtility.PingObject(timeline);
            AssetDatabase.OpenAsset(timeline);
        }

        /// <summary>同轨不能重叠：优先用 preferredName，没空位就再开一条同类型轨。</summary>
        public static SkillEventTrack FindAvailableTrack(
            TimelineAsset tl, int trackId, float time, float duration, string preferredName = null)
        {
            if (tl == null) return null;
            double start = time;
            double end = time + Math.Max(duration, 0.001f);

            if (!string.IsNullOrEmpty(preferredName))
            {
                var named = FindTrackByName(tl, preferredName);
                if (named != null && named.TrackId == trackId && !Overlaps(named, start, end))
                    return named;
            }

            foreach (var t in tl.GetOutputTracks())
            {
                if (t is SkillEventTrack st && st.TrackId == trackId && !Overlaps(st, start, end))
                    return st;
            }

            int n = 1;
            foreach (var t in tl.GetOutputTracks())
            {
                if (t is SkillEventTrack st && st.TrackId == trackId)
                    n++;
            }

            string baseName = !string.IsNullOrEmpty(preferredName)
                ? preferredName
                : DefaultTrackName(trackId);
            string newName = n <= 1 ? baseName : $"{baseName} {n}";
            return CreateTrack(tl, trackId, newName);
        }

        public static SkillEventTrack FindTrackByName(TimelineAsset tl, string name)
        {
            if (tl == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in tl.GetOutputTracks())
            {
                if (t is SkillEventTrack st && st.name == name)
                    return st;
            }
            return null;
        }

        public static string DefaultTrackName(int trackId)
        {
            switch (trackId)
            {
                case SkillTrackId.Anim: return "动画";
                case SkillTrackId.Audio: return "音频";
                case SkillTrackId.Vfx: return "特效";
                case SkillTrackId.Camera: return "镜头";
                case SkillTrackId.HitStop: return "卡肉";
                case SkillTrackId.Phase: return "逻辑";
                case SkillTrackId.Motion: return "位移";
                case SkillTrackId.WindowCancel: return "可取消";
                case SkillTrackId.WindowAccept: return "可接招";
                default: return $"轨{trackId}";
            }
        }

        public static SkillEventTrack CreateTrack(TimelineAsset tl, int trackId, string name)
        {
            if (tl == null) return null;
            TrackAsset created = trackId switch
            {
                SkillTrackId.Anim => tl.CreateTrack<SkillAnimTrack>(null, name),
                SkillTrackId.Audio => tl.CreateTrack<SkillAudioTrack>(null, name),
                SkillTrackId.Vfx => tl.CreateTrack<SkillVfxTrack>(null, name),
                SkillTrackId.Camera => tl.CreateTrack<SkillCameraTrack>(null, name),
                SkillTrackId.HitStop => tl.CreateTrack<SkillHitStopTrack>(null, name),
                SkillTrackId.Phase => tl.CreateTrack<SkillPhaseTrack>(null, name),
                SkillTrackId.Motion => tl.CreateTrack<SkillMotionTrack>(null, name),
                SkillTrackId.WindowCancel => tl.CreateTrack<SkillCancelWindowTrack>(null, name),
                SkillTrackId.WindowAccept => tl.CreateTrack<SkillAcceptWindowTrack>(null, name),
                _ => null
            };
            return created as SkillEventTrack;
        }

        static bool Overlaps(TrackAsset track, double start, double end)
        {
            if (track == null) return false;
            foreach (var c in track.GetClips())
            {
                if (start + 0.0001 < c.end && end - 0.0001 > c.start)
                    return true;
            }
            return false;
        }

        /// <summary>把 Config/ 分发到 Client 与 Server 运行时目录。</summary>
        public static bool SyncRuntimeCopies(out string error)
        {
            error = null;
            string ps1 = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "Tools", "SyncConfig.ps1"));
            if (!File.Exists(ps1))
            {
                error = "找不到 Tools/SyncConfig.ps1";
                return false;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                error = "无法启动 powershell";
                return false;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                return false;
            }

            AssetDatabase.Refresh();
            return true;
        }

        #endregion
    }
}
