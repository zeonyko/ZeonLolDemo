using System;
using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>多 CDN 根地址解析与故障切换。</summary>
    public static class CdnUrlUtility
    {
        public static List<string> ResolveRemoteRoots(ZeonAssetConfig config)
        {
            var roots = new List<string>();
            if (config == null)
                return roots;

            if (config.RemoteUrls != null)
            {
                for (int i = 0; i < config.RemoteUrls.Length; i++)
                {
                    var r = NormalizeRoot(config.RemoteUrls[i]);
                    if (!string.IsNullOrEmpty(r) && !ContainsIgnoreCase(roots, r))
                        roots.Add(r);
                }
            }

            var primary = NormalizeRoot(config.RemoteUrl);
            if (!string.IsNullOrEmpty(primary) && !ContainsIgnoreCase(roots, primary))
                roots.Insert(0, primary);

            return roots;
        }

        public static string BuildBundleUrl(string remoteRoot, string fileName)
        {
            remoteRoot = NormalizeRoot(remoteRoot);
            fileName = (fileName ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(remoteRoot))
                return fileName;

            return ZeonAssetPathHelper.CombineUrl(
                remoteRoot, DiskCacheManager.BundlesFolder + "/" + fileName);
        }

        public static string[] BuildBundleUrlCandidates(ZeonAssetConfig config, string fileName)
        {
            var roots = ResolveRemoteRoots(config);
            var urls = new List<string>(roots.Count);
            for (int i = 0; i < roots.Count; i++)
                urls.Add(BuildBundleUrl(roots[i], fileName));
            return urls.ToArray();
        }

        public static string PickNextUrl(string[] urls, string current)
        {
            if (urls == null || urls.Length == 0)
                return current;
            if (string.IsNullOrEmpty(current))
                return urls[0];

            for (int i = 0; i < urls.Length; i++)
            {
                if (!string.Equals(urls[i], current, StringComparison.OrdinalIgnoreCase))
                    continue;
                return urls[(i + 1) % urls.Length];
            }

            return urls[0];
        }

        private static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return null;
            return root.Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
