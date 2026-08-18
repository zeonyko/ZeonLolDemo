using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Game.ZeonAsset.Editor;
using Launch;

#if HYBRIDCLR
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
#endif

namespace Launch.Editor
{
    /// <summary>
    /// HybridCLR：Generate / 拷到 GameDlls / 出包前检查。只认当前 Active Build Target。
    /// </summary>
    public static class HybridClrPlayerPrepare
    {
        [MenuItem("Launch/生成热更 DLL", false, 10)]
        public static void PrepareMenu()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var ok = TryPrepare(target, out var message);
            Debug.Log("[Launch] 生成热更 DLL:\n" + message);
            EditorUtility.DisplayDialog(
                "生成热更 DLL",
                ok
                    ? message + "\n\n已写入 Assets/GameDlls。发补丁或出包用 Launch / 发布补丁、出安装包。"
                    : message,
                "OK");
        }

        public static bool TryPrepare(BuildTarget target, out string message)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                message =
                    $"请先把 Build Target 切到 {target}（File / Build Settings），再生成热更 DLL。\n" +
                    "当前是 " + EditorUserBuildSettings.activeBuildTarget;
                return false;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Launch", "HybridCLR Generate / All …", 0.3f);
#if HYBRIDCLR
                var installer = new InstallerController();
                if (!installer.HasInstalledHybridCLR())
                {
                    message = "还没有安装 HybridCLR 本地 libil2cpp。请先菜单 HybridCLR / Installer 点安装。只需做一次。";
                    return false;
                }

                PrebuildCommand.GenerateAll();
#else
                message = "未引用 HybridCLR.Editor，无法 Generate。";
                return false;
#endif
                EditorUtility.DisplayProgressBar("Launch", "拷贝 DLL 到 Assets/GameDlls …", 0.8f);
                if (!TryCopyGeneratedDlls(target, out var copyLog))
                {
                    message = "Generate 完成，但拷贝 DLL 失败。\n\n" + copyLog;
                    return false;
                }

                if (!AllHotUpdateDllsExist(out var missing))
                {
                    message = "未找齐热更 DLL:\n" + missing + "\n\n" + copyLog;
                    return false;
                }

                message = "热更 DLL 已生成  target=" + target + "\n\n" + copyLog;
                return true;
            }
            catch (Exception e)
            {
                message = "生成热更 DLL 失败: " + e.Message;
                Debug.LogException(e);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static bool HotUpdateDllExists()
        {
            return AllHotUpdateDllsExist(out _);
        }

        /// <summary>拷贝当前（或指定）平台的热更/AOT DLL 到 Assets/GameDlls。</summary>
        public static bool TryCopyGeneratedDlls(BuildTarget buildTarget, out string summary)
        {
            EnsureCollectorGroups();
            AssetDatabase.SaveAssets();

            var target = buildTarget.ToString();
            var hotSrc = Path.Combine(GetHybridClrDataRoot(), "HotUpdateDlls", target);
            var aotSrc = Path.Combine(GetHybridClrDataRoot(), "AssembliesPostIl2CppStrip", target);
            var log = new StringBuilder();
            log.AppendLine("BuildTarget=" + target);

            if (!Directory.Exists(GetHybridClrDataRoot()))
            {
                summary =
                    "未找到 HybridCLRData。请先 HybridCLR / Installer 安装，再执行 Launch / 生成热更 DLL。";
                return false;
            }

            int copied = 0;
            copied += CopyFolderDlls(hotSrc, ToProjectPath(HybridClrDllPaths.HotUpdateDllFolder), true, log);
            copied += CopyFolderDlls(aotSrc, ToProjectPath(HybridClrDllPaths.AotDllFolder), false, log);

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();

            summary = copied > 0
                ? $"已拷贝 {copied} 个 .dll.bytes\n\n{log}"
                : $"没有拷到 DLL。\n\n{log}\n请先菜单 Launch / 生成热更 DLL。";
            return copied > 0;
        }

        private static bool AllHotUpdateDllsExist(out string missingReport)
        {
            var project = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            var folder = Path.Combine(
                project,
                HybridClrDllPaths.HotUpdateDllFolder.Replace('/', Path.DirectorySeparatorChar));
            var missing = new StringBuilder();
            var names = HybridClrDllPaths.HotUpdateAssemblyNames;
            for (int i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(folder, names[i] + ".dll.bytes");
                if (!File.Exists(path))
                    missing.AppendLine(HybridClrDllPaths.HotUpdateDllFolder + "/" + names[i] + ".dll.bytes");
            }

            missingReport = missing.ToString();
            return missing.Length == 0;
        }

        private static void EnsureDllFolders()
        {
            EnsureAssetFolder("Assets", "GameDlls");
            EnsureAssetFolder("Assets/GameDlls", "HotUpdate");
            EnsureAssetFolder("Assets/GameDlls", "AOT");
        }

        private static void EnsureCollectorGroups()
        {
            EnsureDllFolders();
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            UpsertGroup(settings, "HybridClrHotUpdateDlls", new AssetCollectorEntry
            {
                CollectorName = "HotUpdate Assemblies",
                CollectPath = HybridClrDllPaths.HotUpdateDllFolder,
                Filter = ECollectorFilter.CollectByExtension,
                CustomExtensions = ".bytes",
                PackRule = EPackRule.PackSeparately,
                Addressable = true,
            });
            UpsertGroup(settings, "HybridClrAotDlls", new AssetCollectorEntry
            {
                CollectorName = "AOT Metadata",
                CollectPath = HybridClrDllPaths.AotDllFolder,
                Filter = ECollectorFilter.CollectByExtension,
                CustomExtensions = ".bytes",
                PackRule = EPackRule.PackSeparately,
                Addressable = true,
            });
            UpsertTagRule(settings, HybridClrDllPaths.AotDllFolder + "/", "AOT 补充元数据 DLL", "AOT");
            UpsertTagRule(settings, HybridClrDllPaths.HotUpdateDllFolder + "/", "热更程序集 DLL", "HotUpdate");
            EditorUtility.SetDirty(settings);
        }

        private static void UpsertGroup(
            AssetCollectorSettings settings,
            string groupName,
            AssetCollectorEntry collector)
        {
            for (int i = 0; i < settings.Groups.Count; i++)
            {
                if (settings.Groups[i] != null &&
                    string.Equals(settings.Groups[i].GroupName, groupName, StringComparison.Ordinal))
                {
                    settings.Groups[i].Active = true;
                    settings.Groups[i].Collectors.Clear();
                    settings.Groups[i].Collectors.Add(collector);
                    return;
                }
            }

            settings.Groups.Add(new AssetCollectorGroup
            {
                GroupName = groupName,
                Active = true,
                Collectors = { collector },
            });
        }

        private static void UpsertTagRule(
            AssetCollectorSettings settings,
            string prefix,
            string note,
            params string[] tags)
        {
            if (settings.TagRules == null)
                settings.TagRules = new List<AssetPathTagRule>();

            var normalized = prefix.Replace('\\', '/');
            if (!normalized.EndsWith("/"))
                normalized += "/";

            for (int i = 0; i < settings.TagRules.Count; i++)
            {
                var rule = settings.TagRules[i];
                if (rule == null)
                    continue;
                var existing = (rule.PathPrefix ?? string.Empty).Replace('\\', '/');
                if (!existing.EndsWith("/"))
                    existing += "/";
                if (!string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                rule.Active = true;
                rule.Note = note;
                rule.Tags = new List<string>(tags);
                return;
            }

            settings.TagRules.Add(new AssetPathTagRule
            {
                Active = true,
                PathPrefix = normalized,
                Note = note,
                Tags = new List<string>(tags),
            });
        }

        private static int CopyFolderDlls(string srcDir, string destDir, bool allDlls, StringBuilder log)
        {
            if (!Directory.Exists(srcDir))
            {
                log.AppendLine("源目录不存在: " + srcDir);
                return 0;
            }

            Directory.CreateDirectory(destDir);
            int count = 0;
            var files = Directory.GetFiles(srcDir, "*.dll");
            for (int i = 0; i < files.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(files[i]);
                if (!allDlls && !IsDefaultAotAssembly(name))
                    continue;

                var dest = Path.Combine(destDir, name + ".dll.bytes");
                File.Copy(files[i], dest, overwrite: true);
                log.AppendLine($"{name}.dll  →  {ToAssetPath(dest)}");
                count++;
            }

            if (!allDlls)
            {
                var names = HybridClrDllPaths.DefaultAotMetadataAssemblies;
                for (int i = 0; i < names.Length; i++)
                {
                    var expected = Path.Combine(destDir, names[i] + ".dll.bytes");
                    if (!File.Exists(expected))
                        log.AppendLine("缺少 AOT 元数据: " + names[i] + ".dll");
                }
            }

            return count;
        }

        private static bool IsDefaultAotAssembly(string assemblyName)
        {
            var names = HybridClrDllPaths.DefaultAotMetadataAssemblies;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetHybridClrDataRoot()
        {
            var project = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(project, "HybridCLRData");
        }

        private static string ToProjectPath(string assetPath)
        {
            var project = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(project, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToAssetPath(string fullPath)
        {
            var project = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var n = fullPath.Replace('\\', '/');
            var root = project.Replace('\\', '/').TrimEnd('/');
            if (n.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return n.Substring(root.Length + 1);
            return n;
        }

        private static void EnsureAssetFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }
    }

    /// <summary>
    /// File/Build 若忘了 Prepare，直接打断。
    /// HybridCLR Generate/AOTDlls 的 scripts-only 临时包必须放行。
    /// </summary>
    public sealed class HybridClrPreprocessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 50;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorUserBuildSettings.buildScriptsOnly)
                return;

            if (HybridClrPlayerPrepare.HotUpdateDllExists())
                return;

            throw new BuildFailedException(
                "缺少热更 DLL（需 Login.HotUpdate + Game.HotUpdate 的 .dll.bytes）。\n" +
                "目录: " + HybridClrDllPaths.HotUpdateDllFolder + "\n" +
                "请先菜单 Launch / 生成热更 DLL。");
        }
    }
}
