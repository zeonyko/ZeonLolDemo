using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 生成带 Header 的不可变 Manifest，并按内容寻址重命名 Bundle。
    /// 输出布局：
    ///   {Output}/version_check.json
    ///   {Output}/Manifests/manifest_{yyyyMMdd_HHmm}_{hash}.bytes
    ///   {Output}/Bundles/{contentHash}.bundle
    /// 首包 StreamingAssets 请用 BuiltinPackUtility / 菜单 Pack StreamingAssets。
    /// </summary>
    public class TaskCreateManifest : IBuildTask
    {
        public string Name => "TaskCreateManifest";

        public void Run(BuildContext context)
        {
            var settings = context.Parameters.Settings;
            var platform = context.Parameters.BuildTarget.ToString();
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var buildTimeUtc = DateTime.UtcNow;
            var buildTimeLocal = buildTimeUtc.ToLocalTime();
            var buildUnix = (long)(buildTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var appVersion = string.IsNullOrEmpty(PlayerSettings.bundleVersion)
                ? Application.version
                : PlayerSettings.bundleVersion;

            var bundlesDir = Path.Combine(context.OutputPath, DiskCacheManager.BundlesFolder);
            var manifestsDir = Path.Combine(context.OutputPath, DiskCacheManager.ManifestsFolder);
            Directory.CreateDirectory(bundlesDir);
            Directory.CreateDirectory(manifestsDir);

            var manifest = new PackageManifest
            {
                PackageName = string.IsNullOrEmpty(settings.PackageName)
                    ? ZeonAssetPathLayout.DefaultPackageId
                    : settings.PackageName,
                ManifestVersion = 1,
                AppVersion = appVersion,
                BuildTime = buildUnix,
            };

            var orderedBundles = context.BundleMap.Values
                .OrderBy(b => b.IsAutoExtracted ? 0 : 1)
                .ThenBy(b => b.BundleName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bundleIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < orderedBundles.Count; i++)
            {
                var info = orderedBundles[i];
                var buildFileName = info.BundleName + ".bundle";
                var buildFullPath = Path.Combine(context.OutputPath, buildFileName);

                var packageBundle = new PackageBundle
                {
                    BundleName = info.BundleName,
                    FileName = buildFileName,
                    Tags = info.Tags != null ? new List<string>(info.Tags) : new List<string>(),
                    DependBundles = info.DependBundles.OrderBy(d => d).ToList(),
                    EncryptMode = info.EncryptMode,
                    LoadOffset = info.LoadOffset,
                };

                if (File.Exists(buildFullPath))
                {
                    packageBundle.FileSize = new FileInfo(buildFullPath).Length;
                    packageBundle.CRC = Crc32Utility.ComputeFile(buildFullPath);
                    packageBundle.Hash = HashUtility.ComputeFileMd5(buildFullPath);

                    var addressedName = packageBundle.Hash + ".bundle";
                    var addressedPath = Path.Combine(bundlesDir, addressedName);
                    if (File.Exists(addressedPath))
                        File.Delete(addressedPath);
                    File.Move(buildFullPath, addressedPath);
                    packageBundle.FileName = addressedName;

                    var sideManifest = buildFullPath + ".manifest";
                    if (File.Exists(sideManifest))
                        File.Delete(sideManifest);
                }
                else
                {
                    throw new BuildException($"Bundle 文件不存在，构建中止: {buildFullPath}");
                }

                if (packageBundle.FileSize <= 0 || string.IsNullOrEmpty(packageBundle.Hash))
                    throw new BuildException($"Bundle 无效（Size/Hash 为空）: {packageBundle.FileName}");

                if (info.IsAutoExtracted && !packageBundle.Tags.Contains("AutoExtracted"))
                    packageBundle.Tags.Add("AutoExtracted");

                bundleIdMap[info.BundleName] = i;
                manifest.Bundles.Add(packageBundle);
            }

            var writtenAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in context.CollectedAssets)
            {
                if (!writtenAssets.Add(asset.AssetPath))
                    continue;

                if (!bundleIdMap.TryGetValue(asset.BundleName, out var bundleId))
                {
                    context.LogWarning($"资源找不到所属 Bundle，已跳过: {asset.AssetPath}");
                    continue;
                }

                manifest.Assets.Add(new PackageAsset
                {
                    Address = string.IsNullOrEmpty(asset.Address) ? asset.AssetPath : asset.Address,
                    AssetPath = asset.AssetPath,
                    BundleID = bundleId,
                });
            }

            // Header.ManifestHash：基于 Body 草稿（先空 Hash）计算，再回填
            manifest.ManifestHash = null;
            var draftForHash = manifest.ToJson(false);
            var fullHash = HashUtility.ComputeTextMd5(draftForHash);
            var shortHash = fullHash.Length > 8 ? fullHash.Substring(0, 8) : fullHash;
            manifest.ManifestHash = shortHash;

            var manifestFileName =
                ManifestBuildInfoUtility.BuildCdnManifestFileName(buildTimeLocal, shortHash)
                    .ToLowerInvariant();
            manifest.ManifestFileName = manifestFileName;
            manifest.BuildLookupCache();
            context.Manifest = manifest;

            var finalJson = manifest.ToJson(true);
            var finalBytes = utf8NoBom.GetBytes(finalJson);
            var cdnManifestPath = Path.Combine(manifestsDir, manifestFileName);
            File.WriteAllBytes(cdnManifestPath, finalBytes);
            // 调试可读副本
            File.WriteAllText(
                Path.ChangeExtension(cdnManifestPath, ".json"),
                finalJson,
                utf8NoBom);

            // CDN 模拟 VersionCheck API：version_check.json（精简字段）
            var versionCheckJson = VersionCheckResponse.ToCdnMockJson(
                hasUpdate: true,
                manifestName: manifestFileName,
                manifestHash: shortHash,
                cdnHost: string.Empty,
                prettyPrint: true,
                gameServerHost: string.Empty,
                gameServerPort: 0);
            File.WriteAllText(
                Path.Combine(context.OutputPath, DiskCacheManager.VersionCheckFileName),
                versionCheckJson,
                utf8NoBom);

            context.Log(
                $"Manifest Header: AppVersion={manifest.AppVersion}, ManifestHash={manifest.ManifestHash}, " +
                $"BuildTime={ManifestBuildInfoUtility.FormatBuildTime(manifest.BuildTime)}");
            context.Log($"CDN Manifest → Manifests/{manifestFileName}");
            context.Log($"统计: Bundles={manifest.Bundles.Count}, Assets={manifest.Assets.Count}");
            context.Log($"VersionCheck mock → {DiskCacheManager.VersionCheckFileName} (has_update=true, manifest_name={manifestFileName})");
            context.Log("StreamingAssets 请单独执行 Resource/Pack StreamingAssets (Builtin)");
        }
    }
}
