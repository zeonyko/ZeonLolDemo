using System.IO;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 构建 / 发布 / 首包的无 UI 入口（命令行批处理与各 Editor 窗口共用）。
    /// </summary>
    public static class ZeonAssetEditorActions
    {
        private const string DefaultProfilePath = ZeonAssetPathLayout.ModuleRoot + "/Settings/BuiltinDeliveryProfile.asset";

        #region Build

        /// <summary>批处理：-executeMethod Game.ZeonAsset.Editor.ZeonAssetEditorActions.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            var msg = ExecuteBuild(
                settings, EditorUserBuildSettings.activeBuildTarget, clearOutput: true, useSbp: true);
            if (msg != null && msg.StartsWith("构建失败"))
                throw new System.Exception(msg);
        }

        public static string ExecuteBuild(
            AssetCollectorSettings settings,
            BuildTarget buildTarget,
            bool clearOutput,
            bool useSbp,
            bool incrementalBuild = false,
            EBundleEncryptMode encryptMode = EBundleEncryptMode.None,
            int encryptOffset = 32,
            byte encryptXorKey = 0x5A)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Resource", "Building AssetBundles...", 0.5f);
                // 互斥：清目录则强制关闭增量
                if (clearOutput)
                    incrementalBuild = false;
                else if (incrementalBuild)
                    clearOutput = false;

                // Build 末尾会 AssetDatabase.Refresh，settings 引用可能变假 null；路径先拷出来。
                var outputFolder = settings != null ? settings.OutputFolder : "Bundles";
                var manifest = AssetBundleBuilder.Build(new BuildParameters
                {
                    Settings = settings,
                    ClearOutputFolder = clearOutput,
                    PublishToCdn = false,
                    UseSBP = useSbp,
                    IncrementalBuild = incrementalBuild,
                    EncryptMode = encryptMode,
                    EncryptOffset = encryptOffset,
                    EncryptXorKey = encryptXorKey,
                    BuildTarget = buildTarget,
                });

                var outPath = ZeonAssetPathLayout.GetEditorBuildOutputRoot(
                    outputFolder, buildTarget.ToString());
                return
                    $"构建成功（未发布 CDN）\n" +
                    $"Package={manifest.PackageName}\n" +
                    $"Manifest={manifest.ManifestFileName}\n" +
                    $"Hash={manifest.ManifestHash}\n" +
                    $"BuildTime={ManifestBuildInfoUtility.FormatBuildTime(manifest.BuildTime)}\n" +
                    $"Target={buildTarget}\n" +
                    $"Incremental={incrementalBuild} Encrypt={encryptMode}\n" +
                    $"Bundles={manifest.Bundles.Count} Assets={manifest.Assets.Count}\n" +
                    $"Output={outPath}\n" +
                    $"Report={Path.Combine(outPath, TaskBuildReport.ReportFileName)}";
            }
            catch (System.Exception e)
            {
                return "构建失败: " + e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        #endregion

        #region Publish

        /// <summary>批处理：-executeMethod Game.ZeonAsset.Editor.ZeonAssetEditorActions.PublishFromCommandLine</summary>
        public static void PublishFromCommandLine()
        {
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            var msg = ExecutePublish(settings, EditorUserBuildSettings.activeBuildTarget);
            if (msg != null && msg.StartsWith("发布失败"))
                throw new System.Exception(msg);
        }

        public static string ExecutePublish(AssetCollectorSettings settings, BuildTarget buildTarget)
        {
            try
            {
                // 可能刚 Build 完 Refresh 过，重新加载，避免假 null。
                if (settings == null)
                    settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();

                var outputFolder = settings.OutputFolder;
                var cdnFolder = settings.CdnFolder;
                var outputPath = ZeonAssetPathLayout.GetEditorBuildOutputRoot(
                    outputFolder, buildTarget.ToString());
                if (!Directory.Exists(outputPath))
                    return "发布失败: Build 输出不存在，请先 Build。\n" + outputPath;

                var bundlesDir = Path.Combine(outputPath, DiskCacheManager.BundlesFolder);
                var manifestsDir = Path.Combine(outputPath, DiskCacheManager.ManifestsFolder);
                if (!Directory.Exists(bundlesDir) && !Directory.Exists(manifestsDir))
                    return "发布失败: 输出目录缺少 Bundles/ 或 Manifests/。";

                var context = new BuildContext
                {
                    Parameters = new BuildParameters
                    {
                        Settings = settings,
                        BuildTarget = buildTarget,
                        PublishToCdn = true,
                        ClearOutputFolder = false,
                    },
                    OutputPath = outputPath,
                };

                new TaskPublishCdn().Run(context);
                var cdnRoot = TaskPublishCdn.GetCdnRoot(cdnFolder, buildTarget.ToString());
                return $"发布成功（只增不删）\nCDN={cdnRoot}";
            }
            catch (System.Exception e)
            {
                return "发布失败: " + e.Message;
            }
        }

        #endregion

        #region Pack Builtin

        /// <summary>批处理：-executeMethod Game.ZeonAsset.Editor.ZeonAssetEditorActions.PackFromCommandLine</summary>
        public static void PackFromCommandLine()
        {
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            var profile = LoadOrCreateDeliveryProfile();
            var report = BuiltinPackUtility.Pack(settings, profile, EditorUserBuildSettings.activeBuildTarget);
            if (!report.Success)
                throw new System.Exception(report.Message);
        }

        public static BuiltinDeliveryProfile LoadOrCreateDeliveryProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuiltinDeliveryProfile>(DefaultProfilePath);
            if (profile != null)
                return profile;

            var dir = Path.GetDirectoryName(DefaultProfilePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            profile = BuiltinDeliveryProfile.CreateDefault();
            AssetDatabase.CreateAsset(profile, DefaultProfilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ZeonAsset] 已创建默认 BuiltinDeliveryProfile: {DefaultProfilePath}");
            return profile;
        }

        #endregion

        #region Diagnostics

        [MenuItem("ZeonAsset/打开沙盒", priority = 199)]
        public static void OpenSandbox()
        {
            var dir = DiskCacheManager.GetSandboxRoot();
            if (string.IsNullOrEmpty(dir))
            {
                EditorUtility.DisplayDialog("ZeonAsset", "沙盒路径为空。", "OK");
                return;
            }

            DiskCacheManager.EnsureDirectory(dir);
            EditorUtility.RevealInFinder(dir);
            Debug.Log("[ZeonAsset] Sandbox: " + dir);
        }

        [MenuItem("ZeonAsset/打印引用计数", priority = 200)]
        public static void DumpRefCounts()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Resource", "请在 Play Mode 下 Dump 引用。", "OK");
                return;
            }

            var dump = AssetManager.DumpRefs(includeZeroRef: false);
            Debug.Log(dump);
            EditorUtility.DisplayDialog(
                "Resource Dump Refs",
                dump.Length > 1500 ? dump.Substring(0, 1500) + "..." : dump,
                "OK");
        }

        [MenuItem("ZeonAsset/复制启动报告", priority = 201)]
        public static void CopyLastBootReport()
        {
            var text = ZeonAssetBootReport.Last;
            if (string.IsNullOrEmpty(text))
            {
                EditorUtility.DisplayDialog("ZeonAsset", "还没有 Boot Report。先 Play 一次。", "OK");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log("[ZeonAsset] Last Boot Report copied.\n" + text);
        }

        [MenuItem("ZeonAsset/日志级别/错误", false, 210)]
        public static void SetLogError() => SetLogLevel(EZeonAssetLogLevel.Error);

        [MenuItem("ZeonAsset/日志级别/错误", true)]
        public static bool SetLogErrorValidate() => ValidateLogLevel(EZeonAssetLogLevel.Error);

        [MenuItem("ZeonAsset/日志级别/警告", false, 211)]
        public static void SetLogWarn() => SetLogLevel(EZeonAssetLogLevel.Warn);

        [MenuItem("ZeonAsset/日志级别/警告", true)]
        public static bool SetLogWarnValidate() => ValidateLogLevel(EZeonAssetLogLevel.Warn);

        [MenuItem("ZeonAsset/日志级别/信息", false, 212)]
        public static void SetLogInfo() => SetLogLevel(EZeonAssetLogLevel.Info);

        [MenuItem("ZeonAsset/日志级别/信息", true)]
        public static bool SetLogInfoValidate() => ValidateLogLevel(EZeonAssetLogLevel.Info);

        [MenuItem("ZeonAsset/日志级别/详细", false, 213)]
        public static void SetLogVerbose() => SetLogLevel(EZeonAssetLogLevel.Verbose);

        [MenuItem("ZeonAsset/日志级别/详细", true)]
        public static bool SetLogVerboseValidate() => ValidateLogLevel(EZeonAssetLogLevel.Verbose);

        private static void SetLogLevel(EZeonAssetLogLevel level)
        {
            ZeonAssetLog.SetLevel(level, persist: true);
            Debug.Log("[ZeonAsset] LogLevel=" + level);
        }

        private static bool ValidateLogLevel(EZeonAssetLogLevel level)
        {
            Menu.SetChecked(LogLevelMenuPath(level), ZeonAssetLog.Level == level);
            return true;
        }

        static string LogLevelMenuPath(EZeonAssetLogLevel level)
        {
            switch (level)
            {
                case EZeonAssetLogLevel.Error: return "ZeonAsset/日志级别/错误";
                case EZeonAssetLogLevel.Warn: return "ZeonAsset/日志级别/警告";
                case EZeonAssetLogLevel.Info: return "ZeonAsset/日志级别/信息";
                case EZeonAssetLogLevel.Verbose: return "ZeonAsset/日志级别/详细";
                default: return "ZeonAsset/日志级别/" + level;
            }
        }

        #endregion
    }
}
