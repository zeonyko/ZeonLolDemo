using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 收集结果中的单个待打包资源。
    /// </summary>
    public class CollectAssetInfo
    {
        public string AssetPath;
        public string Address;
        public string BundleName;
        public List<string> Tags = new List<string>();
        public string GroupName;
        public string CollectorName;
    }

    /// <summary>
    /// 按 Settings 规则扫描工程资源，生成收集清单。
    /// </summary>
    public static class AssetBundleCollector
    {
        private static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".dll", ".meta", ".cginc", ".hlsl", ".shadergraph",
            ".asmdef", ".asmref", ".rsp", ".md", ".txt", ".xml",
            ".preset", ".unitypackage"
        };

        public static List<CollectAssetInfo> Collect(AssetCollectorSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var result = new List<CollectAssetInfo>();
            var usedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in settings.Groups)
            {
                if (group == null || !group.Active)
                    continue;

                foreach (var collector in group.Collectors)
                {
                    if (collector == null || !collector.Active)
                        continue;

                    CollectFromEntry(settings, group, collector, result, usedAssetPaths);
                }
            }

            if (!settings.CollectAtlasSourceSprites)
                StripAtlasPackedSprites(result);

            return result;
        }

        private static void CollectFromEntry(
            AssetCollectorSettings settings,
            AssetCollectorGroup group,
            AssetCollectorEntry collector,
            List<CollectAssetInfo> result,
            HashSet<string> usedAssetPaths)
        {
            if (string.IsNullOrEmpty(collector.CollectPath) || !AssetDatabase.IsValidFolder(collector.CollectPath))
            {
                Debug.LogWarning($"[ZeonAsset] CollectPath 无效，已跳过: {collector.CollectPath} ({group.GroupName}/{collector.CollectorName})");
                return;
            }

            var guids = AssetDatabase.FindAssets(string.Empty, new[] { collector.CollectPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                    continue;

                if (!PassFilter(assetPath, collector))
                    continue;

                if (!usedAssetPaths.Add(assetPath))
                {
                    Debug.LogWarning($"[ZeonAsset] 资源被多个 Collector 重复收集，已忽略后续规则: {assetPath}");
                    continue;
                }

                var info = new CollectAssetInfo
                {
                    AssetPath = assetPath,
                    Address = collector.Addressable ? assetPath : string.Empty,
                    BundleName = ResolveBundleName(settings, group, collector, assetPath),
                    GroupName = group.GroupName,
                    CollectorName = collector.CollectorName,
                };

                if (group.Tags != null && group.Tags.Count > 0)
                    info.Tags.AddRange(group.Tags);

                result.Add(info);
            }
        }

        /// <summary>
        /// 已收集的 SpriteAtlas 会把源散图合进去。散图再当独立资源就会打两份。
        /// Prefab 仍引用原来的 Sprite；打包时 Unity 把采样指到图集。
        /// </summary>
        static void StripAtlasPackedSprites(List<CollectAssetInfo> result)
        {
            var packed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < result.Count; i++)
            {
                string path = result[i].AssetPath;
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
                    continue;
                CollectPackedSpritePaths(path, packed);
            }

            if (packed.Count == 0) return;

            int removed = result.RemoveAll(a => a != null && packed.Contains(a.AssetPath));
            if (removed > 0)
                Debug.Log($"[ZeonAsset] 已剔除 {removed} 张图集源散图，只保留 SpriteAtlas 进包");
        }

        static void CollectPackedSpritePaths(string atlasPath, HashSet<string> packed)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null) return;

            var packables = atlas.GetPackables();
            if (packables == null) return;

            for (int i = 0; i < packables.Length; i++)
            {
                var obj = packables[i];
                if (obj == null) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                    for (int g = 0; g < guids.Length; g++)
                    {
                        string texPath = AssetDatabase.GUIDToAssetPath(guids[g]);
                        if (!string.IsNullOrEmpty(texPath))
                            packed.Add(texPath);
                    }
                }
                else
                {
                    packed.Add(path);
                }
            }
        }

        public static bool PassFilter(string assetPath, AssetCollectorEntry collector)
        {
            var ext = Path.GetExtension(assetPath);
            if (IgnoredExtensions.Contains(ext))
                return false;

            // 默认跳过 Editor / Scripts：代码目录不进 Bundle
            var normalized = assetPath.Replace('\\', '/');
            if (normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (normalized.IndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            switch (collector.Filter)
            {
                case ECollectorFilter.CollectAll:
                    return true;
                case ECollectorFilter.CollectPrefab:
                    return ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase);
                case ECollectorFilter.CollectScene:
                    return ext.Equals(".unity", StringComparison.OrdinalIgnoreCase);
                case ECollectorFilter.CollectTexture:
                    return AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(Texture2D)
                           || AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(Texture);
                case ECollectorFilter.CollectMaterial:
                    return ext.Equals(".mat", StringComparison.OrdinalIgnoreCase);
                case ECollectorFilter.CollectAudio:
                    return AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(AudioClip);
                case ECollectorFilter.CollectByExtension:
                    return MatchCustomExtensions(ext, collector.CustomExtensions);
                default:
                    return false;
            }
        }

        private static bool MatchCustomExtensions(string ext, string customExtensions)
        {
            if (string.IsNullOrWhiteSpace(customExtensions))
                return false;

            var parts = customExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var normalized = part.StartsWith(".") ? part : "." + part;
                if (ext.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string ResolveBundleName(
            AssetCollectorSettings settings,
            AssetCollectorGroup group,
            AssetCollectorEntry collector,
            string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            string raw;

            switch (collector.PackRule)
            {
                case EPackRule.PackTogether:
                    raw = $"{settings.PackageName}_{group.GroupName}_{collector.CollectorName}";
                    break;
                case EPackRule.PackSeparately:
                    raw = assetPath;
                    break;
                case EPackRule.PackByFolder:
                    raw = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? collector.CollectPath;
                    break;
                case EPackRule.PackByTopFolder:
                    raw = ResolveTopFolderBundleName(collector.CollectPath, assetPath);
                    break;
                default:
                    raw = assetPath;
                    break;
            }

            return NormalizeBundleName(raw);
        }

        private static string ResolveTopFolderBundleName(string collectPath, string assetPath)
        {
            collectPath = collectPath.Replace('\\', '/').TrimEnd('/');
            assetPath = assetPath.Replace('\\', '/');

            if (!assetPath.StartsWith(collectPath, StringComparison.OrdinalIgnoreCase))
                return collectPath;

            var relative = assetPath.Substring(collectPath.Length).TrimStart('/');
            var slash = relative.IndexOf('/');
            if (slash < 0)
                return collectPath;

            return collectPath + "/" + relative.Substring(0, slash);
        }

        /// <summary>
        /// Bundle 名规范化：小写、非法字符替换，避免跨平台问题。
        /// 注意：不允许保留资源扩展名里的 '.'（如 .mat/.png），否则 SBP 可能把后缀当文件类型处理。
        /// </summary>
        public static string NormalizeBundleName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return "unnamed";

            var chars = rawName.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool valid = (c >= 'a' && c <= 'z')
                             || (c >= '0' && c <= '9')
                             || c == '_' || c == '-';
                if (!valid)
                    chars[i] = '_';
            }

            var name = new string(chars).Trim('_');
            while (name.Contains("__"))
                name = name.Replace("__", "_");

            return string.IsNullOrEmpty(name) ? "unnamed" : name;
        }

        public static Dictionary<string, List<CollectAssetInfo>> GroupByBundle(List<CollectAssetInfo> assets)
        {
            return assets
                .GroupBy(a => a.BundleName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
