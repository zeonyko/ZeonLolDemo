using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    public enum ECollectorFilter
    {
        /// <summary>收集目录下所有资源（含子目录）。</summary>
        CollectAll = 0,

        /// <summary>仅收集 Prefab。</summary>
        CollectPrefab = 1,

        /// <summary>仅收集场景。</summary>
        CollectScene = 2,

        /// <summary>仅收集贴图。</summary>
        CollectTexture = 3,

        /// <summary>仅收集材质。</summary>
        CollectMaterial = 4,

        /// <summary>仅收集音频。</summary>
        CollectAudio = 5,

        /// <summary>自定义扩展名过滤，逗号分隔，例如 .prefab,.fbx</summary>
        CollectByExtension = 6,
    }

    public enum EPackRule
    {
        /// <summary>整个 Collector 打成一个 Bundle。</summary>
        PackTogether = 0,

        /// <summary>每个资源独立成包。</summary>
        PackSeparately = 1,

        /// <summary>按所在文件夹打包。</summary>
        PackByFolder = 2,

        /// <summary>按顶级子文件夹打包。</summary>
        PackByTopFolder = 3,
    }

    [Serializable]
    public class AssetCollectorGroup
    {
        public string GroupName = "DefaultGroup";
        public bool Active = true;
        public List<string> Tags = new List<string>();
        public List<AssetCollectorEntry> Collectors = new List<AssetCollectorEntry>();
    }

    [Serializable]
    public class AssetCollectorEntry
    {
        public string CollectorName = "Collector";
        public bool Active = true;

        /// <summary>收集根目录（Assets/...）。</summary>
        public string CollectPath = "Assets";

        public ECollectorFilter Filter = ECollectorFilter.CollectAll;
        public string CustomExtensions = ".prefab,.png,.jpg,.mat,.fbx,.asset";
        public EPackRule PackRule = EPackRule.PackByFolder;

        /// <summary>是否参与寻址（写入 Manifest Address）。</summary>
        public bool Addressable = true;
    }

    /// <summary>
    /// 资源收集规则 ScriptableObject 配置。
    /// </summary>
    [CreateAssetMenu(fileName = "AssetCollectorSettings", menuName = "Resource/Asset Collector Settings")]
    public class AssetCollectorSettings : ScriptableObject
    {
        public string PackageName = ZeonAssetPathLayout.DefaultPackageId;

        /// <summary>共享依赖被多少个 Bundle 引用时触发 Auto-Promote。</summary>
        [Min(2)]
        public int AutoPromoteShareThreshold = 2;

        /// <summary>构建输出目录（相对工程根目录，可每次清空）。</summary>
        public string OutputFolder = "Bundles";

        /// <summary>模拟 CDN 目录（相对工程根，只增不删）：{CdnFolder}/{Platform}</summary>
        public string CdnFolder = "CDN";

        /// <summary>
        /// 为 true 时，已被收集的 SpriteAtlas 覆盖的散图也当独立资源进包。
        /// 默认 false：只收集图集，Prefab 继续引用散图，由 Unity 在打包时合进图集。
        /// </summary>
        public bool CollectAtlasSourceSprites;

        public List<AssetCollectorGroup> Groups = new List<AssetCollectorGroup>();

        /// <summary>
        /// 路径前缀 → Tag（与 Group 解耦）。
        /// Group 负责「怎么收集/怎么分包」；TagRules 负责「按目录打等级/章节/首包标签」。
        /// </summary>
        public List<AssetPathTagRule> TagRules = new List<AssetPathTagRule>();

        public static AssetCollectorSettings CreateDefault()
        {
            var settings = CreateInstance<AssetCollectorSettings>();
            settings.Groups.Add(new AssetCollectorGroup
            {
                GroupName = "Default",
                // Group.Tags 仅作整组默认；细粒度请用 TagRules
                Tags = { },
                Collectors =
                {
                    new AssetCollectorEntry
                    {
                        CollectorName = "Prefabs",
                        CollectPath = "Assets",
                        Filter = ECollectorFilter.CollectPrefab,
                        PackRule = EPackRule.PackByFolder,
                    }
                }
            });
            return settings;
        }
    }
}
