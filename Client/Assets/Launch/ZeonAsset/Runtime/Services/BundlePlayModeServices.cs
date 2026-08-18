using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 真机运行模式：沙盒 + StreamingAssets 回退；有 CDN 则热更 / 边玩边下。
    /// </summary>
    public class BundlePlayModeServices : IPlayModeServices
    {
        public string ModeName => "HostPlay";
        public PackageManifest Manifest { get; protected set; }
        public bool IsInitialized { get; protected set; }

        protected ZeonAssetConfig Config;
        protected string PackageName;
        protected string BundleRoot;
        protected string StreamingRoot;

        public virtual InitializationOperation InitializeAsync(ZeonAssetConfig config, string packageName)
        {
            Config = config ?? ZeonAssetConfig.CreateDefault();
            PackageName = string.IsNullOrEmpty(packageName) ? Config.DefaultPackageName : packageName;
            StreamingRoot = DiskCacheManager.GetStreamingRoot();
            BundleRoot = string.IsNullOrEmpty(Config.BundleRoot)
                ? ZeonAssetPathHelper.GetDefaultBundleRoot(EPlayMode.HostPlay)
                : Config.BundleRoot.Replace('\\', '/');

            if (!ZeonAssetPathHelper.IsStreamingAssetsLike(BundleRoot))
                DiskCacheManager.EnsureSandboxDirectories(BundleRoot);

            var appVersion = Config.ResolveAppVersion();
            if (DiskCacheManager.TryLoadActiveManifest(
                    appVersion, out var activeManifest, out var sourcePath, out _))
            {
                Manifest = activeManifest;
                IsInitialized = true;
                AssetManager.SetManifest(activeManifest);
                if (!string.IsNullOrEmpty(sourcePath) &&
                    (ZeonAssetPathHelper.IsStreamingAssetsLike(sourcePath) ||
                     string.Equals(
                         sourcePath,
                         DiskCacheManager.GetBuiltinManifestPath(),
                         StringComparison.OrdinalIgnoreCase)))
                    AssetManager.SetBuiltinManifest(activeManifest);
                AssetManager.SetActiveVersionCheck(new VersionCheckData
                {
                    has_update = false,
                    status = "normal",
                    manifest_hash = activeManifest.ManifestHash,
                    manifest_name = activeManifest.ManifestFileName,
                });
                ZeonAssetLog.Info(
                    $"Init ActiveManifest hash={activeManifest.ManifestHash}, " +
                    $"AppVersion={activeManifest.AppVersion}, source={sourcePath}");

                var done = InitializationOperation.Create(this, Config, PackageName, null, _ => { });
                OperationSystem.Start(done);
                return done;
            }

            // 沙盒没有：异步读首包（Android jar / WebGL 走 UWR）
            var manifestPath = ZeonAssetPathHelper.GetManifestPath(BundleRoot);
            bool optional = Config.AutoUpdateOnInit &&
                            CdnUrlUtility.ResolveRemoteRoots(Config).Count > 0;
            var op = InitializationOperation.Create(this, Config, PackageName, manifestPath, manifest =>
            {
                Manifest = manifest;
                IsInitialized = manifest != null || optional;
                if (manifest != null)
                {
                    AssetManager.SetManifest(manifest);
                    if (ZeonAssetPathHelper.IsStreamingAssetsLike(manifestPath) ||
                        string.Equals(
                            manifestPath,
                            DiskCacheManager.GetBuiltinManifestPath(),
                            StringComparison.OrdinalIgnoreCase))
                        AssetManager.SetBuiltinManifest(manifest);
                }
            }, manifestOptional: optional);
            OperationSystem.Start(op);
            return op;
        }

        public void ApplyHotUpdate(PackageManifest manifest, string sandboxRoot)
        {
            InflightAssetLoadRegistry.Clear();
            AssetLoaderManager.Clear();
            BundleLoaderManager.ClearCache();

            Manifest = manifest;
            BundleRoot = sandboxRoot?.Replace('\\', '/') ?? BundleRoot;
            IsInitialized = manifest != null;
            if (manifest != null)
                AssetManager.SetManifest(manifest);
        }

        public string GetBundleRoot() => BundleRoot;
        public string GetStreamingRoot() => StreamingRoot;

        public LoadAssetOperation LoadAssetAsync(string location, Type assetType)
        {
            assetType = assetType ?? typeof(UnityEngine.Object);
            long key = InflightAssetLoadRegistry.MakeKey(location, assetType);

            // 缓存命中：直接返回新 Handle
            if (TryGetCachedLoader(location, out var cached))
            {
                var instant = LoadAssetOperation.CreateFromCache(location, assetType, cached);
                OperationSystem.Start(instant);
                return instant;
            }

            // 同一资源正在加载时复用这次请求。
            if (InflightAssetLoadRegistry.TryGetPrimary(key, out var primary))
            {
                var follower = LoadAssetOperation.CreateFollower(location, assetType, primary);
                InflightAssetLoadRegistry.AttachFollower(key, follower);
                OperationSystem.Start(follower);
                return follower;
            }

            var op = LoadAssetOperation.Create(location, assetType, OnAssetStart, null);
            op.SetInflightKey(key);
            InflightAssetLoadRegistry.RegisterPrimary(key, op);
            OperationSystem.Start(op);
            return op;
        }

        public LoadSceneOperation LoadSceneAsync(string location, LoadSceneMode sceneMode)
        {
            var op = LoadSceneOperation.Create(location, sceneMode, OnSceneStart, null);
            OperationSystem.Start(op);
            return op;
        }

        public LoadSubAssetsOperation LoadSubAssetsAsync(string location, Type assetType)
        {
            assetType = assetType ?? typeof(UnityEngine.Object);
            var op = LoadSubAssetsOperation.Create(location, assetType, OnSubAssetsStart);
            OperationSystem.Start(op);
            return op;
        }

        private bool TryGetCachedLoader(string location, out AssetLoader loader)
        {
            loader = null;
            if (!IsInitialized || Manifest == null)
                return false;
            if (!TryResolveAsset(location, out var packageAsset, out _))
                return false;

            long pathId = PathId.Get(string.IsNullOrEmpty(packageAsset.Address)
                ? packageAsset.AssetPath
                : packageAsset.Address);
            return AssetLoaderManager.TryGet(pathId, out loader);
        }

        private bool OnAssetStart(string location, Type assetType, bool _, LoadAssetOperation op)
        {
            if (!IsInitialized || Manifest == null)
            {
                op.CompleteWithError($"{ModeName} not initialized or manifest missing.");
                return true;
            }

            if (!TryResolveAsset(location, out var packageAsset, out var packageBundle))
            {
                op.CompleteWithError($"Asset not found in manifest: {location}");
                return true;
            }

            var loaders = CollectBundleLoaders(packageBundle);
            if (!BundleLoaderManager.TryGet(packageBundle.BundleName, out var owner))
            {
                op.CompleteWithError($"Owner BundleLoader missing: {packageBundle.BundleName}");
                return true;
            }

            // 加载期间先持有 Bundle，避免被延迟卸载队列提前卸掉。
            op.HoldLoadingBundles(owner);
            op.Address = packageAsset.Address;
            op.AssetPath = packageAsset.AssetPath;
            op.OwnerBundleName = packageBundle.BundleName;
            op.WaitingLoaders = loaders;

            if (TryBeginPlayWhileDownload(packageBundle, loaders, out var downloader))
            {
                op.PendingDownload = downloader;
                return true;
            }

            for (int i = 0; i < loaders.Count; i++)
                loaders[i].StartLoad();
            return true;
        }

        private bool OnSubAssetsStart(string location, Type assetType, LoadSubAssetsOperation op)
        {
            if (!IsInitialized || Manifest == null)
            {
                op.CompleteWithError($"{ModeName} not initialized or manifest missing.");
                return true;
            }

            if (!TryResolveAsset(location, out var packageAsset, out var packageBundle))
            {
                op.CompleteWithError($"Asset not found in manifest: {location}");
                return true;
            }

            var loaders = CollectBundleLoaders(packageBundle);
            if (!BundleLoaderManager.TryGet(packageBundle.BundleName, out var owner))
            {
                op.CompleteWithError($"Owner BundleLoader missing: {packageBundle.BundleName}");
                return true;
            }

            op.HoldLoadingBundles(owner);
            op.Address = packageAsset.Address;
            op.AssetPath = packageAsset.AssetPath;
            op.OwnerBundleName = packageBundle.BundleName;
            op.WaitingLoaders = loaders;

            if (TryBeginPlayWhileDownload(packageBundle, loaders, out var downloader))
            {
                op.PendingDownload = downloader;
                return true;
            }

            for (int i = 0; i < loaders.Count; i++)
                loaders[i].StartLoad();
            return true;
        }

        private bool OnSceneStart(string location, LoadSceneMode sceneMode, LoadSceneOperation op)
        {
            if (!IsInitialized || Manifest == null)
            {
                op.CompleteWithError($"{ModeName} not initialized or manifest missing.");
                return true;
            }

            if (!TryResolveAsset(location, out var packageAsset, out var packageBundle))
            {
                op.CompleteWithError($"Scene not found in manifest: {location}");
                return true;
            }

            var loaders = CollectBundleLoaders(packageBundle);
            if (!BundleLoaderManager.TryGet(packageBundle.BundleName, out var owner))
            {
                op.CompleteWithError($"Owner BundleLoader missing: {packageBundle.BundleName}");
                return true;
            }

            op.HoldLoadingBundles(owner);
            op.Address = packageAsset.Address;
            op.ScenePath = packageAsset.AssetPath;
            op.OwnerBundleName = packageBundle.BundleName;
            op.WaitingLoaders = loaders;

            if (TryBeginPlayWhileDownload(packageBundle, loaders, out var downloader))
            {
                op.PendingDownload = downloader;
                return true;
            }

            for (int i = 0; i < loaders.Count; i++)
                loaders[i].StartLoad();
            return true;
        }

        private bool TryBeginPlayWhileDownload(
            PackageBundle rootBundle,
            List<BundleLoader> loaders,
            out ZeonAssetDownloaderOperation downloader)
        {
            downloader = null;
            if (Config == null || !Config.ShouldPlayWhileDownload())
                return false;

            var missing = CollectMissingBundles(rootBundle);
            if (missing.Count == 0)
                return false;
            if (CdnUrlUtility.ResolveRemoteRoots(Config).Count == 0)
            {
                ZeonAssetLog.Warn("PlayWhileDownload: RemoteUrl(s) 为空，跳过自动下载。");
                return false;
            }

            downloader = ZeonAssetDownloaderOperation.CreateByBundles(Config, missing);
            OperationSystem.Start(downloader);
            ZeonAssetLog.Info($"PlayWhileDownload: downloading {missing.Count} missing bundles.");
            return true;
        }

        private List<PackageBundle> CollectMissingBundles(PackageBundle root)
        {
            var missing = new List<PackageBundle>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectMissingRecursive(root, missing, visited);
            return missing;
        }

        private void CollectMissingRecursive(
            PackageBundle bundle,
            List<PackageBundle> missing,
            HashSet<string> visited)
        {
            if (bundle == null || !visited.Add(bundle.BundleName))
                return;

            if (bundle.DependBundles != null)
            {
                for (int i = 0; i < bundle.DependBundles.Count; i++)
                {
                    if (Manifest.TryGetBundle(bundle.DependBundles[i], out var dep))
                        CollectMissingRecursive(dep, missing, visited);
                }
            }

            if (!DiskCacheManager.IsBundleAvailableLocally(
                    bundle, BundleRoot, StreamingRoot, AssetManager.BuiltinManifest))
                missing.Add(bundle);
        }

        private bool TryResolveAsset(string location, out PackageAsset asset, out PackageBundle bundle)
        {
            asset = null;
            bundle = null;
            if (Manifest.TryGetAssetByAddress(location, out asset) || Manifest.TryGetAsset(location, out asset))
            {
                bundle = Manifest.GetBundleById(asset.BundleID);
                return bundle != null;
            }

            return false;
        }

        protected List<BundleLoader> CollectBundleLoaders(PackageBundle rootBundle)
        {
            var result = new List<BundleLoader>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectRecursive(rootBundle, result, visited);
            return result;
        }

        private void CollectRecursive(PackageBundle bundle, List<BundleLoader> list, HashSet<string> visited)
        {
            if (bundle == null || !visited.Add(bundle.BundleName))
                return;

            var directDeps = new List<BundleLoader>();
            if (bundle.DependBundles != null)
            {
                for (int i = 0; i < bundle.DependBundles.Count; i++)
                {
                    if (!Manifest.TryGetBundle(bundle.DependBundles[i], out var depInfo))
                        continue;

                    CollectRecursive(depInfo, list, visited);

                    if (BundleLoaderManager.TryGet(depInfo.BundleName, out var depLoader))
                        directDeps.Add(depLoader);
                }
            }

            var fullPath = ZeonAssetPathHelper.ResolveBundleLoadPath(
                BundleRoot, StreamingRoot, bundle.FileName);
            var loader = BundleLoaderManager.GetOrCreate(
                bundle.BundleName, bundle.FileName, fullPath, bundle.EncryptMode, bundle.LoadOffset);
            loader.SetDirectDependencies(directDeps);
            list.Add(loader);
        }

        internal static void RefreshLoaderPathsAfterDownload(
            List<BundleLoader> loaders,
            string sandboxRoot,
            string streamingRoot)
        {
            if (loaders == null)
                return;
            for (int i = 0; i < loaders.Count; i++)
            {
                var loader = loaders[i];
                if (loader == null)
                    continue;
                var path = ZeonAssetPathHelper.ResolveBundleLoadPath(
                    sandboxRoot, streamingRoot, loader.FileName);
                loader.RefreshPath(path);
            }
        }
    }
}
