using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Game.ZeonAsset;
using Game.ZeonAsset.Editor;

namespace Launch.Editor
{
    /// <summary>
    /// 发布补丁 / 出安装包。菜单在编辑器里跑；Tools/ci 用 -executeMethod 批处理。
    /// 安装包运行时固定 HostPlay + OnDemand。
    /// </summary>
    public static class LaunchCi
    {
        [MenuItem("Launch/发布补丁/PC", false, 20)]
        public static void PublishPatchPcMenu()
        {
            RunMenu(BuildTarget.StandaloneWindows64, patch: true);
        }

        [MenuItem("Launch/发布补丁/Android", false, 21)]
        public static void PublishPatchAndroidMenu()
        {
            RunMenu(BuildTarget.Android, patch: true);
        }

        [MenuItem("Launch/出安装包/PC", false, 32)]
        public static void BuildPlayerPcMenu()
        {
            RunMenu(BuildTarget.StandaloneWindows64, patch: false);
        }

        [MenuItem("Launch/出安装包/Android", false, 33)]
        public static void BuildPlayerAndroidMenu()
        {
            RunMenu(BuildTarget.Android, patch: false);
        }

        private static void RunMenu(BuildTarget want, bool patch)
        {
            var title = patch ? "发布补丁" : "出安装包";
            var current = EditorUserBuildSettings.activeBuildTarget;
            if (current != want)
            {
                EditorUtility.DisplayDialog(
                    title,
                    "当前平台是 " + current + "。\n请先 File / Build Settings 切到 " + want + "。",
                    "OK");
                return;
            }

            var label = want == BuildTarget.Android ? "Android" : "PC";
            var what = patch
                ? "生成热更 DLL → 打 AB → 发布到 CDN"
                : "检查启动配置 → 生成热更 DLL → 打 AB → 发布 CDN → Pack 首包 → 出安装包";
            if (!EditorUtility.DisplayDialog(
                title,
                "平台: " + label + "\n" + what + "\n可能要几分钟，期间不要进 Play。",
                "开始",
                "取消"))
                return;

            try
            {
                if (patch)
                    PublishPatch();
                else
                    BuildPlayer();
                EditorUtility.DisplayDialog(title, "完成。看 Console 日志。", "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(title + "失败", e.Message, "OK");
            }
        }

        public static void PublishPatch()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            Log("PublishPatch begin  target=" + target);
            EnsureTargetSupported(target);
            Prepare(target);
            var cdn = BuildAndPublish(target);
            WriteOutput(cdn);
            Log("PublishPatch ok  target=" + target + "  cdn=" + cdn);
        }

        public static void BuildPlayer()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            Log("BuildPlayer begin  target=" + target);
            EnsureTargetSupported(target);
            EnsureBootConfig();
            Prepare(target);
            BuildAndPublish(target);
            PackBuiltin(target);
            var path = BuildPlayerPackage(target);
            WriteOutput(path);
            Log("BuildPlayer ok  target=" + target + "  out=" + path);
        }

        private static void EnsureBootConfig()
        {
            if (BootConfigLocalDevStamp.HasUsableBootConfig())
                return;
            throw new Exception(
                "没有启动配置。请先跑 Tools/init-初始化.bat，或菜单 Launch / 生成启动配置。");
        }

        private static void Prepare(BuildTarget target)
        {
            Log("生成热更 DLL …");
            if (!HybridClrPlayerPrepare.TryPrepare(target, out var message))
                throw new Exception("生成热更 DLL 失败:\n" + message);
            Log(message);
        }

        private static string BuildAndPublish(BuildTarget target)
        {
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            var cdnFolder = settings.CdnFolder;
            Log("Build AssetBundles …");
            var buildMsg = ZeonAssetEditorActions.ExecuteBuild(
                settings, target, clearOutput: true, useSbp: true);
            Log(buildMsg);
            if (buildMsg != null && buildMsg.StartsWith("构建失败", StringComparison.Ordinal))
                throw new Exception(buildMsg);

            // Build 内 Refresh 后旧 settings 可能失效，发布前重载。
            settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            if (settings != null && !string.IsNullOrEmpty(settings.CdnFolder))
                cdnFolder = settings.CdnFolder;

            Log("Publish CDN …");
            var pubMsg = ZeonAssetEditorActions.ExecutePublish(settings, target);
            Log(pubMsg);
            if (pubMsg != null && pubMsg.StartsWith("发布失败", StringComparison.Ordinal))
                throw new Exception(pubMsg);

            return TaskPublishCdn.GetCdnRoot(cdnFolder, target.ToString());
        }

        private static void PackBuiltin(BuildTarget target)
        {
            Log("Pack StreamingAssets …");
            var settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            var profile = ZeonAssetEditorActions.LoadOrCreateDeliveryProfile();
            var report = BuiltinPackUtility.Pack(settings, profile, target);
            if (!report.Success)
                throw new Exception(report.Message);
            Log(report.Message);
        }

        private static string BuildPlayerPackage(BuildTarget target)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new Exception("EditorBuildSettings 没有启用场景。");

            var repo = GetRepoRoot();
            var product = SanitizeFileName(
                string.IsNullOrEmpty(PlayerSettings.productName) ? "ZeonLolDemo" : PlayerSettings.productName);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var stamped = product + "_" + stamp;
            string location;
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                    location = Path.Combine(repo, "Dist", "PC", stamped, product + ".exe");
                    break;
                case BuildTarget.Android:
                    PlayerSettings.Android.targetArchitectures =
                        AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
                    EditorUserBuildSettings.buildAppBundle = false;
                    location = Path.Combine(repo, "Dist", "Android", stamped + ".apk");
                    break;
                default:
                    throw new Exception("CI 暂不支持出包平台: " + target);
            }

            var dir = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            Log("BuildPipeline.BuildPlayer → " + location);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location.Replace('\\', '/'),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = EditorUserBuildSettings.development
                    ? BuildOptions.Development
                    : BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("出包失败: " + report.summary.result);
            return location;
        }

        private static void EnsureTargetSupported(BuildTarget target)
        {
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.Android)
                throw new Exception("CI 目前只支持 Win64 / Android，当前是 " + target);
        }

        private static string GetRepoRoot()
        {
            var client = Directory.GetParent(Application.dataPath)?.FullName;
            var repo = Directory.GetParent(client ?? "")?.FullName;
            if (string.IsNullOrEmpty(repo))
                throw new Exception("找不到仓库根目录。");
            return repo;
        }

        private static void WriteOutput(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/');
            Log("OUTPUT=" + path);
            var file = Path.Combine(GetRepoRoot(), "Tools", "ci", "logs", "last-output.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
            File.WriteAllText(file, path + Environment.NewLine);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "ZeonLolDemo" : name.Trim();
        }

        private static void Log(string message)
        {
            Debug.Log("[LaunchCI] " + message);
        }
    }
}
