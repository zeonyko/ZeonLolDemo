using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 资源路径与 Manifest 文件命名（单包扁平布局）。
    /// 沙盒/首包不嵌套子目录；CDN/Build 仅按平台分目录。
    /// </summary>
    public static class ZeonAssetPathLayout
    {
        /// <summary>模块在工程内的根路径（挂在 Launch 下，仍是独立程序集）。</summary>
        public const string ModuleRoot = "Assets/Launch/ZeonAsset";

        public const string RootFolderName = "AssetBundles";

        /// <summary>Manifest JSON 内的逻辑包 ID（非路径分段）。</summary>
        public const string DefaultPackageId = "manifest";

        public const string ManifestActiveFileName = "manifest_active.bytes";
        public const string ManifestBuiltinFileName = "manifest_builtin.bytes";
        public const string ManifestCdnPrefix = "manifest";
        public const string TempManifestFileName = "target.bytes";

        public static string GetSandboxRoot()
        {
            if (!string.IsNullOrEmpty(AssetManager.Config?.BundleRoot))
                return AssetManager.Config.BundleRoot.Replace('\\', '/');

            return Path.Combine(Application.persistentDataPath, RootFolderName).Replace('\\', '/');
        }

        public static string GetStreamingRoot()
        {
            return Path.Combine(Application.streamingAssetsPath, RootFolderName).Replace('\\', '/');
        }

        public static string GetActiveManifestPath(string sandboxRoot = null)
        {
            sandboxRoot = string.IsNullOrEmpty(sandboxRoot) ? GetSandboxRoot() : sandboxRoot.Replace('\\', '/');
            return Path.Combine(sandboxRoot, ManifestActiveFileName).Replace('\\', '/');
        }

        public static string GetBuiltinManifestPath()
        {
            return Path.Combine(GetStreamingRoot(), ManifestBuiltinFileName).Replace('\\', '/');
        }

        /// <summary>工程根（Assets 的上一级）。比 GetCurrentDirectory 稳，批处理 cwd 经常不在 Client。</summary>
        public static string GetEditorProjectRoot()
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var parent = Directory.GetParent(dataPath);
                if (parent != null)
                    return parent.FullName;
            }

            var cwd = Directory.GetCurrentDirectory();
            return string.IsNullOrEmpty(cwd) ? string.Empty : cwd;
        }

        public static string GetEditorCdnRoot(string cdnFolder, string platform)
        {
            cdnFolder = string.IsNullOrEmpty(cdnFolder) ? "CDN" : cdnFolder;
            platform = string.IsNullOrEmpty(platform) ? "Unknown" : platform;
            return Path.Combine(GetEditorProjectRoot(), cdnFolder, platform).Replace('\\', '/');
        }

        public static string GetEditorBuildOutputRoot(string outputFolder, string platform)
        {
            outputFolder = string.IsNullOrEmpty(outputFolder) ? "Bundles" : outputFolder;
            platform = string.IsNullOrEmpty(platform) ? "Unknown" : platform;
            return Path.Combine(GetEditorProjectRoot(), outputFolder, platform).Replace('\\', '/');
        }
    }
}
