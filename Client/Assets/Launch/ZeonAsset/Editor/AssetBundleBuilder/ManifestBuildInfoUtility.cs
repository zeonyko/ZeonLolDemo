using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// Manifest 命名与构建信息展示（替代手填 PackageVersion）。
    /// 识别维度：ManifestFileName + BuildTime + ManifestHash。
    /// </summary>
    public static class ManifestBuildInfoUtility
    {
        public const string FileNameTimeFormat = "yyyyMMdd_HHmm";

        public static string FormatBuildStamp(DateTime localTime)
        {
            return localTime.ToString(FileNameTimeFormat);
        }

        public static string FormatBuildStampUtc(DateTime utcTime)
        {
            return utcTime.ToString(FileNameTimeFormat);
        }

        public static string BuildCdnManifestFileName(DateTime buildTimeLocal, string shortHash)
        {
            shortHash = (shortHash ?? string.Empty).Trim().ToLowerInvariant();
            var stamp = FormatBuildStamp(buildTimeLocal);
            return $"{ZeonAssetPathLayout.ManifestCdnPrefix}_{stamp}_{shortHash}.bytes";
        }

        public static string FormatBuildTime(long unixSeconds, bool localTime = true)
        {
            if (unixSeconds <= 0)
                return "-";
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return localTime
                ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
        }

        public static string FormatSummary(PackageManifest manifest)
        {
            if (manifest == null)
                return "(no manifest)";
            return
                $"file={manifest.ManifestFileName ?? "-"}\n" +
                $"hash={manifest.ManifestHash ?? "-"}\n" +
                $"build={FormatBuildTime(manifest.BuildTime)}\n" +
                $"app={manifest.AppVersion ?? "-"}  bundles={manifest.Bundles?.Count ?? 0}  assets={manifest.Assets?.Count ?? 0}";
        }

        public static bool TryLoadManifestFile(string path, out PackageManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            try
            {
                manifest = PackageManifest.FromJsonBytes(File.ReadAllBytes(path));
                return manifest != null;
            }
            catch
            {
                return false;
            }
        }

        public static List<(string path, PackageManifest manifest, DateTime writeTime)> ListManifests(
            string manifestsDir,
            int maxCount = 20)
        {
            var list = new List<(string, PackageManifest, DateTime)>();
            if (string.IsNullOrEmpty(manifestsDir) || !Directory.Exists(manifestsDir))
                return list;

            var files = Directory.GetFiles(manifestsDir, "*.bytes")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(Math.Max(1, maxCount));

            foreach (var file in files)
            {
                if (!TryLoadManifestFile(file, out var manifest))
                    continue;
                list.Add((file.Replace('\\', '/'), manifest, File.GetLastWriteTimeUtc(file)));
            }

            return list;
        }

        public static bool TryGetLatestManifest(string manifestsDir, out PackageManifest manifest, out string path)
        {
            manifest = null;
            path = null;
            var list = ListManifests(manifestsDir, 1);
            if (list.Count == 0)
                return false;
            path = list[0].path;
            manifest = list[0].manifest;
            return manifest != null;
        }
    }
}
