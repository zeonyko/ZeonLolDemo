using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Shared;
using UnityEngine;
using Game.ZeonAsset;

namespace Client.Battle
{
    /// <summary>客户端战斗配置：真机先装齐 Configs/*.json，再解析。</summary>
    public static class GameConfig
    {
        public static void EnsureAssetLoader()
        {
            if (BattleAssets.Loader != null)
                return;
#if UNITY_EDITOR
            BattleAssets.Initialize(EditorAssetDatabaseBattleAssetLoader.Instance);
#else
            BattleAssets.Initialize(ZeonAssetBattleAssetLoader.Instance);
#endif
        }

        /// <summary>同步解析配置（Configs 须已在缓存，或编辑器 FileConfigSource）。</summary>
        public static void LoadAll()
        {
            EnsureAssetLoader();
            if (ConfigService.IsReady)
                return;

            ConfigService.Initialize(CreateConfigSource(), new UnityConfigJsonCodec());
            BattleCatalogLoader.LoadCore(loadPresentation: true);
            LoadClientOnly();
            Debug.Log(
                $"<color=cyan>[GameConfig] LoadAll OK WorldCollision blockers={WorldCollision.All.Count} uv={WorldCollision.UseUvMapBounds}</color>");
        }

        /// <summary>异步装入 Configs 下全部 json 后解析。编辑器有磁盘源时可直接 LoadAll。</summary>
        public static IEnumerator LoadAllAsync()
        {
            EnsureAssetLoader();
            if (ConfigService.IsReady)
                yield break;

#if UNITY_EDITOR
            if (Directory.Exists(BattlePaths.ConfigRoot))
            {
                LoadAll();
                yield break;
            }
#endif
            var keys = ListConfigJsonKeys();
            if (keys.Count == 0)
                Debug.LogWarning("[GameConfig] Manifest 中未找到 Configs/*.json，读表可能失败");
            else
                Debug.Log($"[GameConfig] 装入 Configs TextAsset × {keys.Count}");
            yield return BattleAssets.LoadAllAsync<TextAsset>(keys);
            LoadAll();
        }

        /// <summary>
        /// 从 ActiveManifest 收集 <c>Assets/Game/Configs/**/*.json</c> → BattleAssets key。
        /// 新增配置只要打进 AB，无需改代码。
        /// </summary>
        public static List<string> ListConfigJsonKeys()
        {
            var keys = new List<string>();
            var manifest = AssetManager.ActiveManifest;
            if (manifest?.Assets == null)
                return keys;

            const string root = "Assets/Game/Configs/";
            for (int i = 0; i < manifest.Assets.Count; i++)
            {
                var path = manifest.Assets[i]?.AssetPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path))
                    continue;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Assets/Game/Configs/foo.json → Configs/foo.json
                keys.Add(path.Substring("Assets/Game/".Length));
            }

            return keys;
        }

        static IConfigSource CreateConfigSource()
        {
#if UNITY_EDITOR
            if (Directory.Exists(BattlePaths.ConfigRoot))
                return new FileConfigSource(BattlePaths.ConfigRoot);
#endif
            return new ZeonAssetConfigSource();
        }

        static void LoadClientOnly()
        {
            UnitViewCatalog.LoadFromConfig();
            SkillTimelineLoader.ResetPlayback();
            SkillReactionConfigLoader.LoadAll();
            SkillHotkeyTable.Load();
            CombatTextCatalog.Reload();
            GroundSurfaceSampler.Reload();
            VfxLibrary.ReloadAliases();
        }
    }
}
