using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 从全量 Manifest 按交付 Profile 生成首包子集 Manifest。
    /// </summary>
    public static class BuiltinManifestBuilder
    {
        public sealed class BuildResult
        {
            public PackageManifest SubsetManifest;
            public List<PackageBundle> SelectedBundles = new List<PackageBundle>();
            public long TotalBytes;
            public int SeedBundleCount;
            public int ClosureAddedCount;
        }

        public static BuildResult BuildSubset(PackageManifest full, BuiltinDeliveryProfile profile)
        {
            if (full == null)
                throw new ArgumentNullException(nameof(full));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            full.BuildLookupCache();
            var result = new BuildResult();

            var seedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < full.Bundles.Count; i++)
            {
                var b = full.Bundles[i];
                if (b == null || string.IsNullOrEmpty(b.BundleName))
                    continue;
                if (IsExcluded(b, profile))
                    continue;
                if (IsSeed(b, profile))
                    seedNames.Add(b.BundleName);
            }

            result.SeedBundleCount = seedNames.Count;

            var selectedNames = new HashSet<string>(seedNames, StringComparer.OrdinalIgnoreCase);
            if (profile.IncludeDependencyClosure)
            {
                var queue = new Queue<string>(seedNames);
                while (queue.Count > 0)
                {
                    var name = queue.Dequeue();
                    if (!full.TryGetBundle(name, out var bundle) || bundle?.DependBundles == null)
                        continue;

                    for (int d = 0; d < bundle.DependBundles.Count; d++)
                    {
                        var dep = bundle.DependBundles[d];
                        if (string.IsNullOrEmpty(dep) || !selectedNames.Add(dep))
                            continue;
                        if (full.TryGetBundle(dep, out var depBundle) && IsExcluded(depBundle, profile))
                        {
                            selectedNames.Remove(dep);
                            continue;
                        }

                        queue.Enqueue(dep);
                    }
                }
            }

            result.ClosureAddedCount = selectedNames.Count - result.SeedBundleCount;

            // 保持全量中的相对顺序，便于调试
            var oldIdToBundle = new Dictionary<int, PackageBundle>();
            for (int i = 0; i < full.Bundles.Count; i++)
            {
                var b = full.Bundles[i];
                if (b != null && selectedNames.Contains(b.BundleName))
                    oldIdToBundle[i] = b;
            }

            var ordered = full.Bundles
                .Where(b => b != null && selectedNames.Contains(b.BundleName))
                .ToList();

            var nameToNewId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var subset = new PackageManifest
            {
                PackageName = full.PackageName,
                ManifestVersion = full.ManifestVersion,
                AppVersion = full.AppVersion,
                BuildTime = full.BuildTime,
                ManifestFileName = null, // 首包 manifest_builtin，不走 CDN 文件名
            };

            for (int i = 0; i < ordered.Count; i++)
            {
                var src = ordered[i];
                var copy = new PackageBundle
                {
                    BundleName = src.BundleName,
                    FileName = src.FileName,
                    Hash = src.Hash,
                    CRC = src.CRC,
                    FileSize = src.FileSize,
                    Tags = src.Tags != null ? new List<string>(src.Tags) : new List<string>(),
                    DependBundles = src.DependBundles != null
                        ? new List<string>(src.DependBundles)
                        : new List<string>(),
                    EncryptMode = src.EncryptMode,
                    LoadOffset = src.LoadOffset,
                };
                nameToNewId[copy.BundleName] = i;
                subset.Bundles.Add(copy);
                result.SelectedBundles.Add(copy);
                result.TotalBytes += Math.Max(0, copy.FileSize);
            }

            // DependBundles 可能指向未选入首包的包：保留名字（运行时全量热更后会齐）；或裁剪
            // 子集阶段裁剪掉不在首包内的依赖边，避免只读首包时误以为依赖已齐
            for (int i = 0; i < subset.Bundles.Count; i++)
            {
                var deps = subset.Bundles[i].DependBundles;
                if (deps == null || deps.Count == 0)
                    continue;
                deps.RemoveAll(d => !nameToNewId.ContainsKey(d));
            }

            for (int i = 0; i < full.Assets.Count; i++)
            {
                var a = full.Assets[i];
                if (a == null)
                    continue;
                if (!oldIdToBundle.TryGetValue(a.BundleID, out var owner))
                    continue;
                if (!nameToNewId.TryGetValue(owner.BundleName, out var newId))
                    continue;

                subset.Assets.Add(new PackageAsset
                {
                    Address = a.Address,
                    AssetPath = a.AssetPath,
                    BundleID = newId,
                    IsRawFile = a.IsRawFile,
                });
            }

            // 子集自己的 ManifestHash（可与 CDN 全量不同 → Host 首次通常 has_update=true）
            subset.ManifestHash = null;
            var draft = subset.ToJson(false);
            var fullHash = HashUtility.ComputeTextMd5(draft);
            subset.ManifestHash = fullHash.Length > 8 ? fullHash.Substring(0, 8) : fullHash;
            subset.BuildLookupCache();

            result.SubsetManifest = subset;
            return result;
        }

        public static string FormatPreview(BuildResult result)
        {
            if (result?.SubsetManifest == null)
                return "无结果";

            var sb = new StringBuilder();
            sb.AppendLine(
                $"首包子集: Bundles={result.SubsetManifest.Bundles.Count} " +
                $"(seed={result.SeedBundleCount}, closure+={result.ClosureAddedCount}), " +
                $"Assets={result.SubsetManifest.Assets.Count}, " +
                $"Size≈{FormatBytes(result.TotalBytes)}, " +
                $"Hash={result.SubsetManifest.ManifestHash}");
            for (int i = 0; i < result.SelectedBundles.Count; i++)
            {
                var b = result.SelectedBundles[i];
                var tags = b.Tags != null && b.Tags.Count > 0 ? string.Join(",", b.Tags) : "-";
                sb.AppendLine($"  • {b.FileName}  {FormatBytes(b.FileSize)}  tags=[{tags}]");
            }

            return sb.ToString();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024f).ToString("0.0") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.00") + " MB";
        }

        private static bool IsExcluded(PackageBundle b, BuiltinDeliveryProfile profile)
        {
            if (b?.Tags == null || profile.ExcludeTags == null)
                return false;
            for (int i = 0; i < profile.ExcludeTags.Count; i++)
            {
                var t = profile.ExcludeTags[i];
                if (string.IsNullOrEmpty(t))
                    continue;
                if (ContainsTag(b.Tags, t))
                    return true;
            }

            return false;
        }

        private static bool IsSeed(PackageBundle b, BuiltinDeliveryProfile profile)
        {
            if (b == null)
                return false;

            if (HasAnyTag(b, profile.AlwaysIncludeTags))
                return true;

            if (b.Tags == null || b.Tags.Count == 0)
                return profile.IncludeUntaggedBundles;

            return HasAnyTag(b, profile.BuiltinTags);
        }

        private static bool HasAnyTag(PackageBundle b, List<string> tags)
        {
            if (b?.Tags == null || tags == null || tags.Count == 0)
                return false;
            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (string.IsNullOrEmpty(t))
                    continue;
                if (ContainsTag(b.Tags, t))
                    return true;
            }

            return false;
        }

        private static bool ContainsTag(List<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
