using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Game.ZeonAsset;
using Shared;

namespace Client.Battle
{
    /// <summary>
    /// 真机 / HostPlay：配置在 AB 里（TextAsset）。
    /// <see cref="GameConfig.LoadAllAsync"/> 会按 Manifest 装齐 Configs/*.json 再同步解析。
    /// </summary>
    public sealed class ZeonAssetConfigSource : IConfigSource
    {
        const string ConfigAssetRoot = "Assets/Game/Configs";

        public bool Exists(string relativePath)
        {
            return TryResolveLocation(relativePath, out _);
        }

        public bool TryReadText(string relativePath, out string text)
        {
            text = null;
            if (!TryResolveLocation(relativePath, out var location))
                return false;

            var asset = LoadTextAsset(location, relativePath);
            if (asset == null || string.IsNullOrEmpty(asset.text))
                return false;

            text = asset.text;
            return true;
        }

        public IReadOnlyList<string> ListFiles(string relativeDir, string searchPattern)
        {
            var dir = ConfigPath.Normalize(relativeDir);
            var prefix = string.IsNullOrEmpty(dir)
                ? ConfigAssetRoot + "/"
                : ConfigAssetRoot + "/" + dir + "/";

            var manifest = AssetManager.ActiveManifest;
            if (manifest?.Assets == null || manifest.Assets.Count == 0)
                return Array.Empty<string>();

            var list = new List<string>();
            for (int i = 0; i < manifest.Assets.Count; i++)
            {
                var path = manifest.Assets[i]?.AssetPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rest = path.Substring(prefix.Length);
                if (rest.IndexOf('/') >= 0)
                    continue;

                var fileName = Path.GetFileName(path);
                if (!MatchPattern(fileName, searchPattern))
                    continue;

                var rel = path.Substring(ConfigAssetRoot.Length).TrimStart('/');
                list.Add(ConfigPath.Normalize(rel));
            }

            return list;
        }

        public override string ToString() => "ZeonAsset:" + ConfigAssetRoot;

        static bool TryResolveLocation(string relativePath, out string location)
        {
            location = null;
            var rel = ConfigPath.Normalize(relativePath);
            if (string.IsNullOrEmpty(rel))
                return false;

            var withJson = rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? rel
                : rel + ".json";

            var candidates = new[]
            {
                ConfigAssetRoot + "/" + rel,
                ConfigAssetRoot + "/" + withJson,
            };

            var manifest = AssetManager.ActiveManifest;
            for (int i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrEmpty(c))
                    continue;
                if (manifest != null &&
                    (manifest.TryGetAssetByAddress(c, out _) || manifest.TryGetAsset(c, out _)))
                {
                    location = c;
                    return true;
                }

                if (manifest == null)
                {
                    location = c;
                    return true;
                }
            }

            return false;
        }

        static TextAsset LoadTextAsset(string location, string relativePath)
        {
            // GameConfig.LoadAllAsync 已按 Manifest 装入 Configs/*.json
            var shortKey = location.StartsWith("Assets/Game/", StringComparison.OrdinalIgnoreCase)
                ? location.Substring("Assets/Game/".Length)
                : relativePath;

            if (BattleAssets.TryGetCached<TextAsset>(shortKey, out var cached))
                return cached;
            if (BattleAssets.TryGetCached<TextAsset>(location, out cached))
                return cached;

            Debug.LogError(
                "[ZeonAssetConfigSource] 配置未加载: " + location +
                "（须先 GameConfig.LoadAllAsync 装入 Configs）");
            return null;
        }

        static bool MatchPattern(string fileName, string searchPattern)
        {
            if (string.IsNullOrWhiteSpace(searchPattern) || searchPattern == "*" || searchPattern == "*.*")
                return true;
            if (string.IsNullOrEmpty(fileName))
                return false;

            return MatchGlob(fileName, searchPattern.Trim(), 0, 0);
        }

        static bool MatchGlob(string text, string pattern, int ti, int pi)
        {
            while (pi < pattern.Length)
            {
                char pc = pattern[pi];
                if (pc == '*')
                {
                    pi++;
                    if (pi >= pattern.Length)
                        return true;
                    while (ti <= text.Length)
                    {
                        if (MatchGlob(text, pattern, ti, pi))
                            return true;
                        if (ti >= text.Length)
                            break;
                        ti++;
                    }

                    return false;
                }

                if (ti >= text.Length)
                    return false;
                if (pc != '?' && !CharsEqualIgnoreCase(pc, text[ti]))
                    return false;
                ti++;
                pi++;
            }

            return ti >= text.Length;
        }

        static bool CharsEqualIgnoreCase(char a, char b)
        {
            if (a == b)
                return true;
            return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
        }
    }
}
