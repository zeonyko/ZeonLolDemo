using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 资源包清单根数据。支持 JSON 序列化，供运行时初始化与热更比对使用。
    /// </summary>
    [Serializable]
    public class PackageManifest
    {
        public string PackageName = ZeonAssetPathLayout.DefaultPackageId;
        public int ManifestVersion = 1;

        /// <summary>原生 App 版本；沙盒 ActiveManifest 与 Application.version 不一致时清沙盒。</summary>
        public string AppVersion = "1.0.0";

        /// <summary>清单逻辑 Hash（Header）；VersionCheck 用它做 has_update 判定。</summary>
        public string ManifestHash;

        /// <summary>构建时间 Unix 秒。</summary>
        public long BuildTime;

        /// <summary>本地可读构建时间（GMT+本地时区）。</summary>
        public string FormatBuildTime(bool localTime = true)
        {
            if (BuildTime <= 0)
                return "-";
            var dt = DateTimeOffset.FromUnixTimeSeconds(BuildTime);
            return localTime
                ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
        }

        /// <summary>CDN 不可变文件名；沙盒 manifest_active；首包 manifest_builtin。</summary>
        public string ManifestFileName;

        public List<PackageAsset> Assets = new List<PackageAsset>();
        public List<PackageBundle> Bundles = new List<PackageBundle>();

        [NonSerialized]
        private Dictionary<string, PackageAsset> _assetMap;

        [NonSerialized]
        private Dictionary<string, PackageBundle> _bundleMap;

        [NonSerialized]
        private Dictionary<string, PackageAsset> _addressMap;

        public void BuildLookupCache()
        {
            _assetMap = new Dictionary<string, PackageAsset>(Assets.Count, StringComparer.OrdinalIgnoreCase);
            _addressMap = new Dictionary<string, PackageAsset>(Assets.Count, StringComparer.OrdinalIgnoreCase);
            _bundleMap = new Dictionary<string, PackageBundle>(Bundles.Count, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Assets.Count; i++)
            {
                var asset = Assets[i];
                if (!string.IsNullOrEmpty(asset.AssetPath))
                    _assetMap[asset.AssetPath] = asset;
                if (!string.IsNullOrEmpty(asset.Address))
                    _addressMap[asset.Address] = asset;
            }

            for (int i = 0; i < Bundles.Count; i++)
            {
                var bundle = Bundles[i];
                if (!string.IsNullOrEmpty(bundle.BundleName))
                    _bundleMap[bundle.BundleName] = bundle;
            }
        }

        public bool TryGetAsset(string assetPath, out PackageAsset asset)
        {
            EnsureCache();
            return _assetMap.TryGetValue(assetPath, out asset);
        }

        public bool TryGetAssetByAddress(string address, out PackageAsset asset)
        {
            EnsureCache();
            return _addressMap.TryGetValue(address, out asset);
        }

        public bool TryGetBundle(string bundleName, out PackageBundle bundle)
        {
            EnsureCache();
            return _bundleMap.TryGetValue(bundleName, out bundle);
        }

        public PackageBundle GetBundleById(int bundleId)
        {
            if (bundleId < 0 || bundleId >= Bundles.Count)
                return null;
            return Bundles[bundleId];
        }

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public byte[] ToJsonBytes(bool prettyPrint = true)
        {
            return Encoding.UTF8.GetBytes(ToJson(prettyPrint));
        }

        public static PackageManifest FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("Manifest json is null or empty.", nameof(json));

            // Unity JsonUtility 无法解析带 BOM 的 JSON（会报 Invalid value）
            json = StripBom(json).Trim();
            if (json.Length == 0 || json[0] != '{')
                throw new InvalidOperationException("Manifest json is not a valid object.");

            var manifest = JsonUtility.FromJson<PackageManifest>(json);
            if (manifest == null)
                throw new InvalidOperationException("Failed to deserialize PackageManifest.");

            if (manifest.Assets == null)
                manifest.Assets = new List<PackageAsset>();
            if (manifest.Bundles == null)
                manifest.Bundles = new List<PackageBundle>();

            // List<string> 在部分 JsonUtility 场景下可能为 null
            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                var b = manifest.Bundles[i];
                if (b.Tags == null)
                    b.Tags = new List<string>();
                if (b.DependBundles == null)
                    b.DependBundles = new List<string>();
            }

            manifest.BuildLookupCache();
            return manifest;
        }

        public static PackageManifest FromJsonBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Manifest bytes is null or empty.", nameof(bytes));

            // 跳过 UTF-8 BOM
            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                offset = 3;

            return FromJson(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset));
        }

        private static string StripBom(string text)
        {
            if (!string.IsNullOrEmpty(text) && text[0] == '\uFEFF')
                return text.Substring(1);
            return text;
        }

        private void EnsureCache()
        {
            if (_assetMap == null || _bundleMap == null || _addressMap == null)
                BuildLookupCache();
        }
    }
}
