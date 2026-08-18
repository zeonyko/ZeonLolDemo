using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 泄漏分析：Dump 仍持有引用的 Bundle / Asset。
    /// </summary>
    public static class ZeonAssetLeakAnalyzer
    {
        public static string Dump(bool includeZeroRef = false)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== Resource Leak Dump ===");
            sb.AppendLine($"BundleLoaders cached={BundleLoaderManager.CachedCount}");
            sb.AppendLine($"AssetLoaders cached={AssetLoaderManager.CachedCount}");
            sb.AppendLine();

            int liveBundles = 0;
            foreach (var loader in BundleLoaderManager.Enumerate())
            {
                if (loader == null)
                    continue;
                if (!includeZeroRef && loader.RefCount <= 0 && !loader.IsInDelayUnload)
                    continue;

                liveBundles++;
                sb.AppendLine(
                    $"[Bundle] ref={loader.RefCount} state={loader.State} " +
                    $"delay={loader.IsInDelayUnload} name={loader.BundleName} path={loader.FullPath}");
            }

            sb.AppendLine();
            int liveAssets = 0;
            foreach (var loader in AssetLoaderManager.Enumerate())
            {
                if (loader == null)
                    continue;
                if (!includeZeroRef && loader.RefCount <= 0)
                    continue;

                liveAssets++;
                sb.AppendLine(
                    $"[Asset] ref={loader.RefCount} address={loader.Address} " +
                    $"path={loader.AssetPath} owner={loader.OwnerBundle?.BundleName}");
            }

            sb.AppendLine();
            sb.AppendLine($"Live bundles(listed)={liveBundles}, live assets(listed)={liveAssets}");
            return sb.ToString();
        }

        public static void LogDump(bool includeZeroRef = false)
        {
            ZeonAssetLog.Info(Dump(includeZeroRef));
        }
    }
}
