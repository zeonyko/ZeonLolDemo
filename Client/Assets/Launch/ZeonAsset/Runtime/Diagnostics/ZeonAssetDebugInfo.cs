using System.Text;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 单个资源的运行时诊断快照：清单归属 + 引用计数 + Bundle 驻留状态。
    /// </summary>
    public sealed class ZeonAssetDebugInfo
    {
        public string Location;
        public bool FoundInManifest;
        public string Address;
        public string AssetPath;
        public bool IsRawFile;

        public string BundleName;
        public string BundleFileName;
        public string BundleHash;
        public long BundleFileSize;
        public string[] Tags;
        public string[] DependBundles;

        public bool AssetLoaded;
        public int AssetRefCount;
        public string AssetTypeName;

        public bool BundleCached;
        public int BundleRefCount;
        public string BundleState;
        public bool BundleDelayUnload;
        public string BundleFullPath;

        public string Note;

        public string Format()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("Location: " + (Location ?? "-"));
            if (!string.IsNullOrEmpty(Note))
                sb.AppendLine("Note: " + Note);

            if (FoundInManifest)
            {
                sb.AppendLine($"Address: {Address}");
                sb.AppendLine($"AssetPath: {AssetPath}");
                sb.AppendLine($"RawFile: {IsRawFile}");
                sb.AppendLine($"Bundle: {BundleName}");
                sb.AppendLine($"File: {BundleFileName}  hash={BundleHash}  size={FormatBytes(BundleFileSize)}");
                if (Tags != null && Tags.Length > 0)
                    sb.AppendLine("Tags: " + string.Join(", ", Tags));
                if (DependBundles != null && DependBundles.Length > 0)
                    sb.AppendLine("Deps: " + string.Join(", ", DependBundles));
            }
            else
            {
                sb.AppendLine("Manifest: 未收录（EditorSimulate 直读或路径不对）");
            }

            sb.AppendLine();
            sb.Append("Asset: ");
            if (AssetLoaded)
                sb.Append($"loaded  ref={AssetRefCount}  type={AssetTypeName ?? "-"}");
            else
                sb.Append("not loaded");
            sb.AppendLine();

            sb.Append("Bundle: ");
            if (BundleCached)
            {
                sb.Append($"cached  ref={BundleRefCount}  state={BundleState}");
                if (BundleDelayUnload)
                    sb.Append("  delayUnload");
                if (!string.IsNullOrEmpty(BundleFullPath))
                    sb.AppendLine().Append("Path: ").Append(BundleFullPath);
            }
            else
            {
                sb.Append("not in memory");
            }

            return sb.ToString();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
                return "0";
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024f).ToString("0.##") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.##") + " MB";
        }
    }

    /// <summary>按路径/地址查询资源诊断信息。</summary>
    public static class ZeonAssetDebugQuery
    {
        public static ZeonAssetDebugInfo Query(string location)
        {
            var info = new ZeonAssetDebugInfo { Location = location };
            if (string.IsNullOrWhiteSpace(location))
            {
                info.Note = "路径为空";
                return info;
            }

            location = location.Trim();
            info.Location = location;

            var manifest = AssetManager.ActiveManifest;
            PackageAsset asset = null;
            PackageBundle bundle = null;
            if (manifest != null)
            {
                if (!manifest.TryGetAsset(location, out asset))
                    manifest.TryGetAssetByAddress(location, out asset);

                if (asset != null)
                {
                    info.FoundInManifest = true;
                    info.Address = asset.Address;
                    info.AssetPath = asset.AssetPath;
                    info.IsRawFile = asset.IsRawFile;
                    bundle = manifest.GetBundleById(asset.BundleID);
                    if (bundle != null)
                        FillBundleMeta(info, bundle);
                }
            }

            if (AssetManager.Config != null && AssetManager.Config.PlayMode == EPlayMode.EditorSimulate && bundle == null)
                info.Note = "EditorSimulate 不走 Bundle，引用来自 AssetLoader";

            FillRuntimeAsset(info, location, info.Address, info.AssetPath);

            if (!string.IsNullOrEmpty(info.BundleName) &&
                BundleLoaderManager.TryGet(info.BundleName, out var loader) &&
                loader != null)
            {
                FillRuntimeBundle(info, loader);
            }

            return info;
        }

        private static void FillBundleMeta(ZeonAssetDebugInfo info, PackageBundle bundle)
        {
            info.BundleName = bundle.BundleName;
            info.BundleFileName = bundle.FileName;
            info.BundleHash = bundle.Hash;
            info.BundleFileSize = bundle.FileSize;
            info.Tags = bundle.Tags != null ? bundle.Tags.ToArray() : null;
            info.DependBundles = bundle.DependBundles != null ? bundle.DependBundles.ToArray() : null;
        }

        private static void FillRuntimeAsset(
            ZeonAssetDebugInfo info,
            string location,
            string address,
            string assetPath)
        {
            if (TryGetAssetLoader(location, out var loader) ||
                TryGetAssetLoader(address, out loader) ||
                TryGetAssetLoader(assetPath, out loader))
            {
                info.AssetLoaded = true;
                info.AssetRefCount = loader.RefCount;
                info.AssetTypeName = loader.AssetObject != null ? loader.AssetObject.GetType().Name : null;
                if (string.IsNullOrEmpty(info.AssetPath))
                    info.AssetPath = loader.AssetPath;
                if (string.IsNullOrEmpty(info.Address))
                    info.Address = loader.Address;
                if (loader.OwnerBundle != null)
                    FillRuntimeBundle(info, loader.OwnerBundle);
            }
        }

        private static bool TryGetAssetLoader(string key, out AssetLoader loader)
        {
            loader = null;
            if (string.IsNullOrEmpty(key))
                return false;
            return AssetLoaderManager.TryGet(PathId.Get(key), out loader);
        }

        private static void FillRuntimeBundle(ZeonAssetDebugInfo info, BundleLoader loader)
        {
            info.BundleCached = true;
            info.BundleRefCount = loader.RefCount;
            info.BundleState = loader.State.ToString();
            info.BundleDelayUnload = loader.IsInDelayUnload;
            info.BundleFullPath = loader.FullPath;
            if (string.IsNullOrEmpty(info.BundleName))
                info.BundleName = loader.BundleName;
        }
    }
}
