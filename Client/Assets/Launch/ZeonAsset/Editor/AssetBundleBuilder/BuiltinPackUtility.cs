using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 独立于 Build：按 BuiltinDeliveryProfile 从全量产物打 StreamingAssets 首包。
    /// </summary>
    public static class BuiltinPackUtility
    {
        public sealed class PackReport
        {
            public bool Success;
            public string Message;
            public string StreamingRoot;
            public string SourceManifestPath;
            public string SourceBundlesDir;
            public BuiltinManifestBuilder.BuildResult Subset;
        }

        public static PackReport Pack(
            AssetCollectorSettings settings,
            BuiltinDeliveryProfile profile,
            BuildTarget buildTarget)
        {
            var report = new PackReport();
            try
            {
                if (settings == null)
                    throw new InvalidOperationException("AssetCollectorSettings 为空。");
                if (profile == null)
                    throw new InvalidOperationException("BuiltinDeliveryProfile 为空。");

                if (!TryResolveManifestSource(
                        settings, profile, buildTarget,
                        out var manifestPath, out var bundlesDir, out var resolveNote))
                {
                    throw new InvalidOperationException(
                        "找不到全量 Manifest。请先 Build（并建议 Publish CDN）。\n" + resolveNote);
                }

                report.SourceManifestPath = manifestPath;
                report.SourceBundlesDir = bundlesDir;

                var bytes = File.ReadAllBytes(manifestPath);
                var full = PackageManifest.FromJsonBytes(bytes);
                var subsetResult = BuiltinManifestBuilder.BuildSubset(full, profile);
                report.Subset = subsetResult;

                if (subsetResult.SelectedBundles.Count == 0)
                {
                    throw new InvalidOperationException(
                        "首包未选中任何 Bundle。请检查 Group.Tags 与 BuiltinDeliveryProfile。\n" +
                        "例如给登录资源 Group 打 Tag「Builtin」，并把「Builtin」写入 Profile.BuiltinTags。");
                }

                // 校验源 Bundle 文件齐全
                for (int i = 0; i < subsetResult.SelectedBundles.Count; i++)
                {
                    var b = subsetResult.SelectedBundles[i];
                    var src = Path.Combine(bundlesDir, b.FileName);
                    if (!File.Exists(src))
                        throw new FileNotFoundException($"首包所需 Bundle 缺失: {src}");
                }

                var streamingRoot = DiskCacheManager.GetStreamingRoot();
                report.StreamingRoot = streamingRoot;

                if (Directory.Exists(streamingRoot))
                    Directory.Delete(streamingRoot, true);
                Directory.CreateDirectory(streamingRoot);
                var dstBundles = Path.Combine(streamingRoot, DiskCacheManager.BundlesFolder);
                Directory.CreateDirectory(dstBundles);

                var utf8 = new UTF8Encoding(false);
                var subsetJson = subsetResult.SubsetManifest.ToJson(true);
                var subsetBytes = utf8.GetBytes(subsetJson);
                var builtinPath = Path.Combine(streamingRoot, ZeonAssetPathLayout.ManifestBuiltinFileName);
                File.WriteAllBytes(builtinPath, subsetBytes);
                File.WriteAllText(
                    Path.Combine(streamingRoot, Path.ChangeExtension(ZeonAssetPathLayout.ManifestBuiltinFileName, ".json")),
                    subsetJson,
                    utf8);

                for (int i = 0; i < subsetResult.SelectedBundles.Count; i++)
                {
                    var b = subsetResult.SelectedBundles[i];
                    File.Copy(
                        Path.Combine(bundlesDir, b.FileName),
                        Path.Combine(dstBundles, b.FileName),
                        true);
                }

                AssetDatabase.Refresh();

                report.Success = true;
                report.Message =
                    $"首包已写入 StreamingAssets\n" +
                    $"{streamingRoot}\n\n" +
                    $"源 Manifest: {manifestPath}\n" +
                    $"源 Bundles: {bundlesDir}\n\n" +
                    BuiltinManifestBuilder.FormatPreview(subsetResult);
                Debug.Log("[ZeonAsset] " + report.Message.Replace("\n", " | "));
                return report;
            }
            catch (Exception e)
            {
                report.Success = false;
                report.Message = "Pack Builtin 失败: " + e.Message;
                Debug.LogError("[ZeonAsset] " + report.Message);
                return report;
            }
        }

        public static bool TryPreview(
            AssetCollectorSettings settings,
            BuiltinDeliveryProfile profile,
            BuildTarget buildTarget,
            out BuiltinManifestBuilder.BuildResult result,
            out string error)
        {
            result = null;
            error = null;
            try
            {
                if (!TryResolveManifestSource(
                        settings, profile, buildTarget,
                        out var manifestPath, out _, out var note))
                {
                    error = "找不到全量 Manifest。请先 Build。\n" + note;
                    return false;
                }

                var full = PackageManifest.FromJsonBytes(File.ReadAllBytes(manifestPath));
                result = BuiltinManifestBuilder.BuildSubset(full, profile);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// 解析全量 Manifest 与 Bundles 目录：优先 CDN（PreferCdn），否则 Build 输出。
        /// </summary>
        public static bool TryResolveManifestSource(
            AssetCollectorSettings settings,
            BuiltinDeliveryProfile profile,
            BuildTarget buildTarget,
            out string manifestPath,
            out string bundlesDir,
            out string note)
        {
            manifestPath = null;
            bundlesDir = null;
            var platform = buildTarget.ToString();
            var sb = new StringBuilder();

            if (profile == null || profile.PreferCdnAsSource)
            {
                var cdnRoot = TaskPublishCdn.GetCdnRoot(settings.CdnFolder, platform);
                var cdnManifests = Path.Combine(cdnRoot, DiskCacheManager.ManifestsFolder);
                var cdnBundles = Path.Combine(cdnRoot, DiskCacheManager.BundlesFolder);
                sb.AppendLine("CDN: " + cdnRoot);
                if (TryPickLatestBytesManifest(cdnManifests, out manifestPath))
                {
                    bundlesDir = cdnBundles;
                    note = sb.ToString();
                    return Directory.Exists(bundlesDir);
                }
            }

            var outputRoot = ZeonAssetPathLayout.GetEditorBuildOutputRoot(settings.OutputFolder, platform);
            var outManifests = Path.Combine(outputRoot, DiskCacheManager.ManifestsFolder);
            var outBundles = Path.Combine(outputRoot, DiskCacheManager.BundlesFolder);
            sb.AppendLine("Output: " + outputRoot);

            if (TryPickLatestBytesManifest(outManifests, out manifestPath))
            {
                bundlesDir = outBundles;
                note = sb.ToString();
                return Directory.Exists(bundlesDir);
            }

            note = sb.ToString();
            return false;
        }

        public static bool TryPickLatestBytesManifest(string manifestsDir, out string path)
        {
            path = null;
            if (!Directory.Exists(manifestsDir))
                return false;

            var files = Directory.GetFiles(manifestsDir, "*.bytes")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
            if (files.Length == 0)
                return false;

            path = files[0];
            return true;
        }

        /// <summary>收集 Settings 里出现过的全部 Tag（供编辑器勾选）。</summary>
        public static void CollectKnownTags(AssetCollectorSettings settings, System.Collections.Generic.HashSet<string> into)
        {
            if (settings?.Groups == null || into == null)
                return;
            for (int g = 0; g < settings.Groups.Count; g++)
            {
                var tags = settings.Groups[g]?.Tags;
                if (tags == null)
                    continue;
                for (int t = 0; t < tags.Count; t++)
                {
                    if (!string.IsNullOrWhiteSpace(tags[t]))
                        into.Add(tags[t].Trim());
                }
            }

            into.Add("Shaders");
            into.Add("AutoExtracted");
            into.Add("HotUpdate");
            into.Add("AOT");
            into.Add("Builtin");
            into.Add("Login");

            if (settings.TagRules != null)
            {
                for (int i = 0; i < settings.TagRules.Count; i++)
                {
                    var tags = settings.TagRules[i]?.Tags;
                    if (tags == null)
                        continue;
                    for (int t = 0; t < tags.Count; t++)
                    {
                        if (!string.IsNullOrWhiteSpace(tags[t]))
                            into.Add(tags[t].Trim());
                    }
                }
            }
        }
    }
}
