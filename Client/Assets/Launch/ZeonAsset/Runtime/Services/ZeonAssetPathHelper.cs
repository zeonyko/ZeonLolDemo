using System;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    public static class ZeonAssetPathHelper
    {
        /// <summary>
        /// 是否可把 Bundle 当普通本地文件缓存（LoadFromFile / FileStream 断点）。
        /// WebGL 等平台为 false：应走 URL / 内存缓存，不依赖 #if 宏。
        /// </summary>
        public static bool CanUsePersistentFileCache()
        {
            return Application.platform != RuntimePlatform.WebGLPlayer;
        }

        public static string GetDefaultBundleRoot(EPlayMode playMode)
        {
            // 仅 Host：沙盒为逻辑根，加载时仍回退 StreamingAssets 首包
            if (playMode == EPlayMode.HostPlay)
                return DiskCacheManager.GetSandboxRoot();
            return string.Empty;
        }

        /// <summary>沙盒 manifest_active → 首包 manifest_builtin。</summary>
        public static string GetManifestPath(string bundleRoot = null)
        {
            if (!string.IsNullOrEmpty(bundleRoot))
            {
                var active = ZeonAssetPathLayout.GetActiveManifestPath(bundleRoot);
                if (CanProbeLocalFile(active) && File.Exists(active))
                    return active;
            }

            return ZeonAssetPathLayout.GetBuiltinManifestPath();
        }

        public static string ResolveBundleLoadPath(string primaryRoot, string fallbackRoot, string fileName)
        {
            string streamingFallback = null;
            foreach (var candidate in DiskCacheManager.EnumerateBundleCandidates(
                         primaryRoot, fallbackRoot, fileName))
            {
                if (FileExistsCompat(candidate))
                    return candidate;
                if (streamingFallback == null && IsStreamingAssetsLike(candidate))
                    streamingFallback = candidate;
            }

            // Android/WebGL：jar 内首包无法 File.Exists，仍返回路径供 UWR 加载
            if (!string.IsNullOrEmpty(streamingFallback))
                return streamingFallback;

            return DiskCacheManager.GetBundlePath(primaryRoot, fileName);
        }

        public static bool CanProbeLocalFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // WebGL：持久化路径不可靠，一律视为不可探测（走 URL / BundleMemoryCache）
            if (!CanUsePersistentFileCache())
                return false;

            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("://") || normalized.Contains("jar:"))
                return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            var streaming = Application.streamingAssetsPath.Replace('\\', '/');
            if (!string.IsNullOrEmpty(streaming) &&
                normalized.StartsWith(streaming, StringComparison.OrdinalIgnoreCase))
                return false;
#endif
            return true;
        }

        /// <summary>是否为浏览器无法使用的 file:// CDN。</summary>
        public static bool IsFileUrl(string urlOrPath)
        {
            if (string.IsNullOrEmpty(urlOrPath))
                return false;
            return urlOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        public static bool FileExistsCompat(string path)
        {
            if (!CanProbeLocalFile(path))
                return false;
            return File.Exists(path);
        }

        public static bool IsStreamingAssetsLike(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("jar:"))
                return true;
            var streaming = Application.streamingAssetsPath.Replace('\\', '/');
            return !string.IsNullOrEmpty(streaming) &&
                   normalized.StartsWith(streaming, StringComparison.OrdinalIgnoreCase);
        }

        public static string CombineUrl(string root, string relativePath)
        {
            root = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            relativePath = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (root.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return root + "/" + relativePath;
            return root + "/" + relativePath;
        }

        /// <summary>本地路径 → UnityWebRequest 可用 URL（含 Android jar:）。</summary>
        public static string ToRequestUrl(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = path.Replace('\\', '/');
            if (path.Contains("://") || path.Contains("jar:"))
                return path;

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return path;

            try
            {
                return new Uri(path).AbsoluteUri;
            }
            catch
            {
                return "file:///" + path.TrimStart('/');
            }
        }
    }
}
