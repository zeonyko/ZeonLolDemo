using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 按资源路径前缀打 Tag（与 Group 解耦）。
    /// 例：Collect 用 Group「Login」扫 Assets/Login/，
    /// Tag 用规则 Assets/Login/ → Login + Builtin。
    /// </summary>
    [Serializable]
    public class AssetPathTagRule
    {
        public bool Active = true;

        /// <summary>Assets 下路径前缀，匹配 AssetPath.StartsWith（忽略大小写）。</summary>
        public string PathPrefix = "Assets/";

        /// <summary>备注（等级说明等，不进 Manifest）。</summary>
        public string Note = string.Empty;

        public List<string> Tags = new List<string>();
    }

    public static class AssetTagRuleUtility
    {
        /// <summary>把命中的路径规则 Tag 合并进资源（去重，保留 Group 已有 Tag）。</summary>
        public static void ApplyPathRules(AssetCollectorSettings settings, List<CollectAssetInfo> assets)
        {
            if (settings?.TagRules == null || assets == null || assets.Count == 0)
                return;

            var active = settings.TagRules
                .Where(r => r != null && r.Active && !string.IsNullOrWhiteSpace(r.PathPrefix))
                .Select(r =>
                {
                    var prefix = NormalizePrefix(r.PathPrefix);
                    return (prefix, r);
                })
                // 长前缀优先，便于「先粗后细」阅读；合并时仍全部命中都加
                .OrderByDescending(x => x.prefix.Length)
                .ToList();

            if (active.Count == 0)
                return;

            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null || string.IsNullOrEmpty(asset.AssetPath))
                    continue;

                var path = asset.AssetPath.Replace('\\', '/');
                for (int r = 0; r < active.Count; r++)
                {
                    var prefix = active[r].prefix;
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var tags = active[r].r.Tags;
                    if (tags == null)
                        continue;
                    for (int t = 0; t < tags.Count; t++)
                    {
                        var tag = tags[t]?.Trim();
                        if (string.IsNullOrEmpty(tag))
                            continue;
                        if (!asset.Tags.Exists(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
                            asset.Tags.Add(tag);
                    }
                }
            }
        }

        public static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return "Assets/";
            prefix = prefix.Replace('\\', '/').Trim();
            if (!prefix.EndsWith("/", StringComparison.Ordinal))
                prefix += "/";
            return prefix;
        }

        /// <summary>
        /// 扫描根目录下一层子文件夹，为每个子目录生成一条 Tag 规则（已存在前缀则跳过）。
        /// 适合 UI/功能1、UI/功能2 这种结构。
        /// </summary>
        public static int GenerateRulesFromTopFolders(
            AssetCollectorSettings settings,
            string rootFolder,
            string defaultTagPrefix = null)
        {
            if (settings == null || string.IsNullOrEmpty(rootFolder) || !AssetDatabase.IsValidFolder(rootFolder))
                return 0;

            if (settings.TagRules == null)
                settings.TagRules = new List<AssetPathTagRule>();

            rootFolder = rootFolder.Replace('\\', '/').TrimEnd('/');
            var existing = new HashSet<string>(
                settings.TagRules
                    .Where(r => r != null && !string.IsNullOrEmpty(r.PathPrefix))
                    .Select(r => NormalizePrefix(r.PathPrefix)),
                StringComparer.OrdinalIgnoreCase);

            int added = 0;
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { rootFolder });
            var topFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.StartsWith(rootFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = path.Substring(rootFolder.Length + 1);
                var slash = relative.IndexOf('/');
                var top = slash >= 0 ? relative.Substring(0, slash) : null;
                if (string.IsNullOrEmpty(top))
                {
                    // 文件直接在 root 下：可选一条 root 规则
                    continue;
                }

                topFolders.Add(rootFolder + "/" + top);
            }

            foreach (var folder in topFolders)
            {
                var prefix = NormalizePrefix(folder);
                if (!existing.Add(prefix))
                    continue;

                var folderName = Path.GetFileName(folder.TrimEnd('/'));
                var rule = new AssetPathTagRule
                {
                    Active = true,
                    PathPrefix = prefix,
                    Note = folderName,
                    Tags = new List<string>(),
                };

                // 可选：默认塞一个与文件夹同名的 Tag，方便立刻用；等级请用户改
                if (!string.IsNullOrEmpty(defaultTagPrefix))
                    rule.Tags.Add(defaultTagPrefix + folderName);
                else
                    rule.Tags.Add(folderName);

                settings.TagRules.Add(rule);
                added++;
            }

            // 根目录本身一条粗粒度规则（若无）
            var rootPrefix = NormalizePrefix(rootFolder);
            if (existing.Add(rootPrefix))
            {
                settings.TagRules.Insert(0, new AssetPathTagRule
                {
                    Active = true,
                    PathPrefix = rootPrefix,
                    Note = "整棵目录粗粒度 Tag（可关）",
                    Tags = new List<string> { Path.GetFileName(rootFolder) },
                });
                added++;
            }

            return added;
        }

        public static List<(string pathPrefix, List<string> tags, int assetCount)> PreviewCoverage(
            AssetCollectorSettings settings)
        {
            var list = new List<(string, List<string>, int)>();
            if (settings?.TagRules == null)
                return list;

            var assets = AssetBundleCollector.Collect(settings);
            ApplyPathRules(settings, assets);

            for (int i = 0; i < settings.TagRules.Count; i++)
            {
                var rule = settings.TagRules[i];
                if (rule == null || !rule.Active)
                    continue;
                var prefix = NormalizePrefix(rule.PathPrefix);
                int count = 0;
                for (int a = 0; a < assets.Count; a++)
                {
                    if (assets[a].AssetPath.Replace('\\', '/')
                        .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        count++;
                }

                list.Add((prefix, rule.Tags != null ? new List<string>(rule.Tags) : new List<string>(), count));
            }

            return list;
        }
    }
}
