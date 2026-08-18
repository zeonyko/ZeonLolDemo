using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.ZeonAsset;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 生成构建体积 / 冗余报告 → {Output}/BuildReport.json。
    /// </summary>
    public class TaskBuildReport : IBuildTask
    {
        public const string ReportFileName = "BuildReport.json";
        private const int TopN = 20;

        public string Name => "TaskBuildReport";

        public void Run(BuildContext context)
        {
            var manifest = context.Manifest;
            if (manifest == null)
            {
                context.LogWarning("Manifest 为空，跳过 BuildReport。");
                return;
            }

            var report = new BuildVolumeReport
            {
                PackageName = manifest.PackageName,
                Platform = context.Parameters.BuildTarget.ToString(),
                ManifestHash = manifest.ManifestHash,
                ManifestFileName = manifest.ManifestFileName,
                BundleCount = manifest.Bundles?.Count ?? 0,
                AssetCount = manifest.Assets?.Count ?? 0,
            };

            long total = 0;
            var sizeEntries = new List<BundleSizeEntry>();
            if (manifest.Bundles != null)
            {
                for (int i = 0; i < manifest.Bundles.Count; i++)
                {
                    var b = manifest.Bundles[i];
                    total += Math.Max(0, b.FileSize);
                    int assetCount = 0;
                    bool auto = false;
                    if (context.BundleMap.TryGetValue(b.BundleName, out var info))
                    {
                        assetCount = info.AssetPaths?.Count ?? 0;
                        auto = info.IsAutoExtracted;
                    }

                    if (b.Tags != null && b.Tags.Contains("AutoExtracted"))
                        auto = true;

                    sizeEntries.Add(new BundleSizeEntry
                    {
                        BundleName = b.BundleName,
                        FileName = b.FileName,
                        FileSize = b.FileSize,
                        AssetCount = assetCount,
                        IsAutoExtracted = auto,
                    });
                }
            }

            report.TotalBundleBytes = total;
            report.AutoExtractedCount = sizeEntries.Count(e => e.IsAutoExtracted);
            report.TopBundlesBySize = sizeEntries
                .OrderByDescending(e => e.FileSize)
                .Take(TopN)
                .ToList();

            var shared = new List<SharedAssetEntry>();
            foreach (var pair in context.ImplicitDependencyGraph)
            {
                var refs = pair.Value;
                if (refs == null || refs.Count < 2)
                    continue;
                shared.Add(new SharedAssetEntry
                {
                    AssetPath = pair.Key,
                    ReferencedByBundleCount = refs.Count,
                    Bundles = refs.OrderBy(x => x).ToList(),
                });
            }

            report.TopSharedAssets = shared
                .OrderByDescending(s => s.ReferencedByBundleCount)
                .ThenBy(s => s.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Take(TopN)
                .ToList();

            var json = JsonUtility.ToJson(report, true);
            var path = Path.Combine(context.OutputPath, ReportFileName);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            context.Log(
                $"BuildReport → {ReportFileName} | Total={FormatBytes(total)}, " +
                $"Bundles={report.BundleCount}, AutoExtracted={report.AutoExtractedCount}, " +
                $"SharedAssets(top)={report.TopSharedAssets.Count}");
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024)
                return kb.ToString("0.0") + " KB";
            double mb = kb / 1024.0;
            return mb.ToString("0.00") + " MB";
        }
    }

    [Serializable]
    public class BuildVolumeReport
    {
        public string PackageName;
        public string Platform;
        public string ManifestHash;
        public string ManifestFileName;
        public long TotalBundleBytes;
        public int BundleCount;
        public int AssetCount;
        public int AutoExtractedCount;
        public List<BundleSizeEntry> TopBundlesBySize = new List<BundleSizeEntry>();
        public List<SharedAssetEntry> TopSharedAssets = new List<SharedAssetEntry>();
    }

    [Serializable]
    public class BundleSizeEntry
    {
        public string BundleName;
        public string FileName;
        public long FileSize;
        public int AssetCount;
        public bool IsAutoExtracted;
    }

    [Serializable]
    public class SharedAssetEntry
    {
        public string AssetPath;
        public int ReferencedByBundleCount;
        public List<string> Bundles = new List<string>();
    }
}
