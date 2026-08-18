using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    public class BuildParameters
    {
        public AssetCollectorSettings Settings;
        public BuildTarget BuildTarget;
        public BuildAssetBundleOptions BuildOptions = BuildAssetBundleOptions.ChunkBasedCompression;
        public string OutputFolder;
        public bool ClearOutputFolder = true;
        /// <summary>将本次 Manifest/Bundle 追加到模拟 CDN 目录（不清理历史）。</summary>
        public bool PublishToCdn = true;

        /// <summary>优先使用 Scriptable Build Pipeline（CompatibilityBuildPipeline）。</summary>
        public bool UseSBP = true;

        /// <summary>
        /// 增量构建：SBP UseCache=true，不强制 ForceRebuild。
        /// 与 ClearOutputFolder 互斥（清目录会删掉上一次产物，增量无意义）。
        /// </summary>
        public bool IncrementalBuild = false;

        /// <summary>构建后加密模式（写入 PackageBundle，运行时 IDecrypter / Offset 加载）。</summary>
        public EBundleEncryptMode EncryptMode = EBundleEncryptMode.None;

        /// <summary>Offset 加密插入头字节数。</summary>
        public int EncryptOffset = 32;

        /// <summary>XOR 加密密钥。</summary>
        public byte EncryptXorKey = 0x5A;
    }

    /// <summary>
    /// 构建期 Bundle 映射单元。
    /// </summary>
    public class BuildBundleInfo
    {
        public string BundleName;
        public List<string> AssetPaths = new List<string>();
        public List<string> Tags = new List<string>();
        public HashSet<string> DependBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool IsAutoExtracted;
        public EBundleEncryptMode EncryptMode = EBundleEncryptMode.None;
        public int LoadOffset;
    }

    public class BuildContext
    {
        public BuildParameters Parameters;
        public List<CollectAssetInfo> CollectedAssets = new List<CollectAssetInfo>();
        public Dictionary<string, BuildBundleInfo> BundleMap = new Dictionary<string, BuildBundleInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>隐式依赖图：AssetPath -> 引用它的 Bundle 集合。</summary>
        public Dictionary<string, HashSet<string>> ImplicitDependencyGraph =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>所有已明确归属 Bundle 的资源路径。</summary>
        public HashSet<string> OwnedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PackageManifest Manifest;
        public string OutputPath;
        public AssetBundleManifest UnityManifest;
        public List<string> Logs = new List<string>();

        public void Log(string message)
        {
            Logs.Add(message);
            UnityEngine.Debug.Log($"[ZeonAsset] {message}");
        }

        public void LogWarning(string message)
        {
            Logs.Add("[Warning] " + message);
            UnityEngine.Debug.LogWarning($"[ZeonAsset] {message}");
        }

        public BuildBundleInfo GetOrCreateBundle(string bundleName)
        {
            if (!BundleMap.TryGetValue(bundleName, out var info))
            {
                info = new BuildBundleInfo { BundleName = bundleName };
                BundleMap[bundleName] = info;
            }

            return info;
        }
    }

    public class BuildException : Exception
    {
        public BuildException(string message) : base(message)
        {
        }
    }
}
