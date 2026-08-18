using System.IO;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 将本次构建的不可变 Manifest/Bundle 追加上传到模拟 CDN（只增不删）。
    /// 构建工作区 Bundles/ 仍可每次清空；CDN/ 保留历史清单与旧 Hash 包，便于回退与多版本并存。
    /// </summary>
    public class TaskPublishCdn : IBuildTask
    {
        public string Name => "TaskPublishCdn";

        public static string GetCdnRoot(string cdnFolder, string platform)
        {
            return ZeonAssetPathLayout.GetEditorCdnRoot(cdnFolder, platform);
        }

        public void Run(BuildContext context)
        {
            if (!context.Parameters.PublishToCdn)
            {
                context.Log("跳过模拟 CDN 发布（PublishToCdn=false）");
                return;
            }

            var settings = context.Parameters.Settings;
            var cdnRoot = GetCdnRoot(
                settings.CdnFolder,
                context.Parameters.BuildTarget.ToString());

            var cdnManifests = Path.Combine(cdnRoot, DiskCacheManager.ManifestsFolder);
            var cdnBundles = Path.Combine(cdnRoot, DiskCacheManager.BundlesFolder);
            Directory.CreateDirectory(cdnManifests);
            Directory.CreateDirectory(cdnBundles);

            int manifestCount = CopyNewFiles(
                Path.Combine(context.OutputPath, DiskCacheManager.ManifestsFolder),
                cdnManifests,
                ".bytes", ".json");
            int bundleCount = CopyNewFiles(
                Path.Combine(context.OutputPath, DiskCacheManager.BundlesFolder),
                cdnBundles,
                ".bundle");

            var versionCheckSrc = Path.Combine(context.OutputPath, DiskCacheManager.VersionCheckFileName);
            if (File.Exists(versionCheckSrc))
                File.Copy(versionCheckSrc, Path.Combine(cdnRoot, DiskCacheManager.VersionCheckFileName), true);

            context.Log(
                $"模拟 CDN 已追加: +{manifestCount} Manifest, +{bundleCount} Bundle → {cdnRoot}\n" +
                $"可读 {DiskCacheManager.VersionCheckFileName}；cdn_host 空则自动用本目录 file://。");
        }

        private static int CopyNewFiles(string srcDir, string dstDir, params string[] extensions)
        {
            if (!Directory.Exists(srcDir))
                return 0;

            int copied = 0;
            foreach (var file in Directory.GetFiles(srcDir))
            {
                var ext = Path.GetExtension(file);
                bool match = false;
                for (int i = 0; i < extensions.Length; i++)
                {
                    if (ext.Equals(extensions[i], System.StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                    continue;

                var dest = Path.Combine(dstDir, Path.GetFileName(file));
                if (File.Exists(dest))
                {
                    var srcInfo = new FileInfo(file);
                    var dstInfo = new FileInfo(dest);
                    if (srcInfo.Length == dstInfo.Length)
                        continue;
                }

                File.Copy(file, dest, true);
                copied++;
            }

            return copied;
        }
    }
}
