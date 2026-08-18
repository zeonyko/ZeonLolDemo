using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 沙盒：manifest_active.bytes + Bundles/ + Cache/tmp/
    /// 首包：StreamingAssets/AssetBundles/manifest_builtin.bytes + Bundles/
    /// </summary>
    public static class DiskCacheManager
    {
        public const string ManifestsFolder = "Manifests";
        public const string BundlesFolder = "Bundles";
        public const string CacheFolder = "Cache";
        public const string TmpFolder = "tmp";

        /// <summary>构建产物里的静态 VersionCheck 模拟文件。Launch 不会自动猜测此路径。</summary>
        public const string VersionCheckFileName = "version_check.json";

        public static string GetSandboxRoot() => ZeonAssetPathLayout.GetSandboxRoot();

        public static string GetStreamingRoot() => ZeonAssetPathLayout.GetStreamingRoot();

        public static string GetActiveManifestPath(string sandboxRoot = null) =>
            ZeonAssetPathLayout.GetActiveManifestPath(sandboxRoot);

        public static string GetBuiltinManifestPath() => ZeonAssetPathLayout.GetBuiltinManifestPath();

        public static string GetTempManifestPath(string sandboxRoot) =>
            Path.Combine(GetTempDir(sandboxRoot), ZeonAssetPathLayout.TempManifestFileName).Replace('\\', '/');

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public static void EnsureSandboxDirectories(string sandboxRoot)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return;
            EnsureDirectory(sandboxRoot);
            EnsureDirectory(Path.Combine(sandboxRoot, BundlesFolder));
            EnsureDirectory(Path.Combine(sandboxRoot, CacheFolder, TmpFolder));
        }

        public static string GetBundlesDir(string root) =>
            Path.Combine(root, BundlesFolder).Replace('\\', '/');

        public static string GetTempDir(string sandboxRoot) =>
            Path.Combine(sandboxRoot, CacheFolder, TmpFolder).Replace('\\', '/');

        public static string GetBundlePath(string root, string fileName) =>
            Path.Combine(root, BundlesFolder, fileName).Replace('\\', '/');

        public static string GetTempPath(string sandboxRoot, string fileNameOrHash)
        {
            var name = Path.GetFileNameWithoutExtension(fileNameOrHash);
            if (string.IsNullOrEmpty(name))
                name = "download";
            return Path.Combine(GetTempDir(sandboxRoot), name + ".tmp").Replace('\\', '/');
        }

        public static bool ValidateFile(string filePath, uint expectedCrc, long expectedSize)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            var info = new FileInfo(filePath);
            if (expectedSize > 0 && info.Length != expectedSize)
                return false;

            if (expectedCrc != 0)
            {
                var crc = Crc32Utility.ComputeFile(filePath);
                if (crc != expectedCrc)
                    return false;
            }

            return true;
        }

        public static void DeleteCorrupted(string filePath, string sandboxRoot = null)
        {
            TryDelete(filePath);
            TryDelete(filePath + ".tmp");
            if (!string.IsNullOrEmpty(sandboxRoot) && !string.IsNullOrEmpty(filePath))
                TryDelete(GetTempPath(sandboxRoot, Path.GetFileName(filePath)));
            ZeonAssetLog.Warn($"Deleted corrupted cache: {filePath}");
        }

        /// <summary>
        /// 读沙盒 manifest_active；AppVersion 不匹配则清沙盒；再降级首包 manifest_builtin。
        /// </summary>
        public static bool TryLoadActiveManifest(
            string appVersion,
            out PackageManifest manifest,
            out string sourcePath,
            out bool clearedSandbox)
        {
            manifest = null;
            sourcePath = null;
            clearedSandbox = false;
            appVersion = string.IsNullOrEmpty(appVersion) ? Application.version : appVersion;

            var sandbox = GetSandboxRoot();
            TryResumePendingCommit(sandbox);
            var activePath = GetActiveManifestPath(sandbox);

            if (ZeonAssetPathHelper.CanProbeLocalFile(activePath) && File.Exists(activePath))
            {
                if (!TryReadManifestFile(activePath, out var sandboxManifest))
                {
                    ZeonAssetLog.Warn("manifest_active 损坏，清理沙盒。");
                    ClearSandbox(sandbox);
                    clearedSandbox = true;
                }
                else if (!string.IsNullOrEmpty(sandboxManifest.AppVersion) &&
                         !string.Equals(sandboxManifest.AppVersion, appVersion, StringComparison.OrdinalIgnoreCase))
                {
                    ZeonAssetLog.Warn(
                        $"manifest_active.AppVersion={sandboxManifest.AppVersion} " +
                        $"!= App={appVersion}，清理沙盒热更（商店大包覆盖）。");
                    ClearSandbox(sandbox);
                    clearedSandbox = true;
                }
                else
                {
                    manifest = sandboxManifest;
                    sourcePath = activePath;
                    return true;
                }
            }

            var builtinPath = GetBuiltinManifestPath();
            if (TryReadManifestFile(builtinPath, out manifest))
            {
                sourcePath = builtinPath;
                ZeonAssetLog.Info($"使用首包 Manifest: {builtinPath}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 启动恢复：manifest_active 丢失但 tmp/target.bytes 仍在 → 补提交。
        /// </summary>
        public static bool TryResumePendingCommit(string sandboxRoot)
        {
            if (string.IsNullOrEmpty(sandboxRoot))
                return false;

            var activePath = GetActiveManifestPath(sandboxRoot);
            var tmp = GetTempManifestPath(sandboxRoot);
            if (!ZeonAssetPathHelper.CanProbeLocalFile(tmp) || !File.Exists(tmp))
                return false;
            if (ZeonAssetPathHelper.CanProbeLocalFile(activePath) && File.Exists(activePath))
                return false;

            try
            {
                CommitActiveManifest(sandboxRoot, tmp);
                ZeonAssetLog.Info("Resumed pending manifest_active commit from target.bytes");
                return true;
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"Resume commit failed: {e.Message}");
                return false;
            }
        }

        /// <summary>原子提交：Cache/tmp/target.bytes → manifest_active.bytes。不可持久化平台跳过。</summary>
        public static void CommitActiveManifest(string sandboxRoot, string tmpManifestPath)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
            {
                ZeonAssetLog.Info(
                    "Skip disk CommitActiveManifest（不可持久化平台，ActiveManifest 仅内存生效）");
                return;
            }

            if (string.IsNullOrEmpty(tmpManifestPath) || !File.Exists(tmpManifestPath))
                throw new InvalidOperationException("Temp manifest missing: " + tmpManifestPath);

            EnsureSandboxDirectories(sandboxRoot);
            var activePath = GetActiveManifestPath(sandboxRoot);
            var backupPath = activePath + ".bak";

            if (File.Exists(activePath))
            {
                try
                {
                    File.Replace(tmpManifestPath, activePath, backupPath);
                    TryDelete(backupPath);
                }
                catch (Exception e)
                {
                    ZeonAssetLog.Warn($"File.Replace failed ({e.Message}), fallback to Move.");
                    TryDelete(activePath);
                    File.Move(tmpManifestPath, activePath);
                    TryDelete(backupPath);
                }
            }
            else
            {
                File.Move(tmpManifestPath, activePath);
            }

            ZeonAssetLog.Info($"Atomic commit manifest_active: {activePath}");
        }

        public static void ClearSandbox(string sandboxRoot)
        {
            if (string.IsNullOrEmpty(sandboxRoot) || !Directory.Exists(sandboxRoot))
                return;

            try
            {
                TryDelete(GetActiveManifestPath(sandboxRoot));
                var bundles = GetBundlesDir(sandboxRoot);
                if (Directory.Exists(bundles))
                    Directory.Delete(bundles, true);
                var tmp = GetTempDir(sandboxRoot);
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, true);

                EnsureSandboxDirectories(sandboxRoot);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"ClearSandbox failed: {e.Message}");
            }
        }

        public static void ClearTempDir(string sandboxRoot)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return;
            var tmp = GetTempDir(sandboxRoot);
            try
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, true);
                EnsureDirectory(tmp);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"ClearTempDir failed: {e.Message}");
            }
        }

        /// <summary>热更后 GC：删除不被当前 ActiveManifest 与首包引用的孤儿 Bundle。</summary>
        public static void GarbageCollectSandboxBundles(PackageManifest active, PackageManifest builtin)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return;

            var sandbox = GetSandboxRoot();
            var bundlesDir = GetBundlesDir(sandbox);
            if (!Directory.Exists(bundlesDir))
                return;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectBundleFileNames(active, keep);
            CollectBundleFileNames(builtin, keep);

            foreach (var file in Directory.GetFiles(bundlesDir, "*.bundle"))
            {
                var name = Path.GetFileName(file);
                if (keep.Contains(name))
                    continue;
                TryDelete(file);
                ZeonAssetLog.Info($"Sandbox GC deleted: {name}");
            }
        }

        public static List<PackageBundle> CollectDirtyBundles(
            PackageManifest remote,
            string sandboxRoot,
            string streamingRoot = null,
            PackageManifest builtinManifest = null)
        {
            var dirty = new List<PackageBundle>();
            if (remote?.Bundles == null)
                return dirty;

            if (string.IsNullOrEmpty(streamingRoot))
                streamingRoot = GetStreamingRoot();

            for (int i = 0; i < remote.Bundles.Count; i++)
            {
                var bundle = remote.Bundles[i];
                if (IsBundleAvailableLocally(bundle, sandboxRoot, streamingRoot, builtinManifest))
                    continue;
                dirty.Add(bundle);
            }

            return dirty;
        }

        public static bool IsBundleAvailableLocally(
            PackageBundle bundle,
            string sandboxRoot,
            string streamingRoot,
            PackageManifest builtinManifest = null)
        {
            if (bundle == null)
                return false;

            if (BundleMemoryCache.Contains(bundle.FileName))
                return true;

            foreach (var path in EnumerateBundleCandidates(sandboxRoot, streamingRoot, bundle.FileName))
            {
                if (ZeonAssetPathHelper.CanProbeLocalFile(path))
                {
                    if (ValidateFile(path, bundle.CRC, bundle.FileSize))
                        return true;
                    continue;
                }

                // Streaming / WebGL：无法 CRC，信任 builtin Manifest 中的同名文件
                if (ZeonAssetPathHelper.IsStreamingAssetsLike(path) &&
                    BuiltinContainsFile(builtinManifest, bundle.FileName))
                    return true;
            }

            return false;
        }

        private static bool BuiltinContainsFile(PackageManifest builtin, string fileName)
        {
            if (builtin?.Bundles == null || string.IsNullOrEmpty(fileName))
                return false;
            for (int i = 0; i < builtin.Bundles.Count; i++)
            {
                if (string.Equals(builtin.Bundles[i]?.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>沙盒 Bundles/ → 首包 Bundles/（仅 CDN 扁平布局）。</summary>
        public static IEnumerable<string> EnumerateBundleCandidates(
            string primaryRoot,
            string fallbackRoot,
            string fileName)
        {
            if (!string.IsNullOrEmpty(primaryRoot))
                yield return Path.Combine(primaryRoot, BundlesFolder, fileName).Replace('\\', '/');

            if (!string.IsNullOrEmpty(fallbackRoot) &&
                !string.Equals(primaryRoot, fallbackRoot, StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(fallbackRoot, BundlesFolder, fileName).Replace('\\', '/');
        }

        private static void CollectBundleFileNames(PackageManifest manifest, HashSet<string> keep)
        {
            if (manifest?.Bundles == null)
                return;
            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                var f = manifest.Bundles[i]?.FileName;
                if (!string.IsNullOrEmpty(f))
                    keep.Add(f);
            }
        }

        public static bool TryReadManifestFile(string path, out PackageManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrEmpty(path))
                return false;
            if (!ZeonAssetPathHelper.CanProbeLocalFile(path) || !File.Exists(path))
                return false;

            try
            {
                var bytes = File.ReadAllBytes(path);
                manifest = PackageManifest.FromJsonBytes(bytes);
                return manifest != null;
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"Read manifest failed: {path}, {e.Message}");
                manifest = null;
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }
}
