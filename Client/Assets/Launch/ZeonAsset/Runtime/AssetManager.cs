using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 资源系统门面入口。
    /// </summary>
    public static class AssetManager
    {
        public static PackageManifest ActiveManifest { get; private set; }
        /// <summary>首包 Manifest（StreamingAssets）；WebGL 等无法 File 探测时用于信任首包 Bundle。</summary>
        public static PackageManifest BuiltinManifest { get; private set; }
        /// <summary>当前生效的 VersionCheck data（远端调度返回）。</summary>
        public static VersionCheckData ActiveVersionCheck { get; private set; }
        public static ZeonAssetConfig Config { get; private set; } = ZeonAssetConfig.CreateDefault();
        public static IPlayModeServices PlayModeServices { get; private set; }
        public static bool IsInitialized => PlayModeServices != null && PlayModeServices.IsInitialized;

        /// <summary>RawFile / Tag 下载门面（代码热更与业务分包共用）。</summary>
        public static readonly IRawFileProvider RawFiles = new RawFileProviderFacade();

        public static void SetManifest(PackageManifest manifest)
        {
            ActiveManifest = manifest;
            ActiveManifest?.BuildLookupCache();
        }

        public static void SetBuiltinManifest(PackageManifest manifest)
        {
            BuiltinManifest = manifest;
            BuiltinManifest?.BuildLookupCache();
        }

        public static void SetActiveVersionCheck(VersionCheckData data)
        {
            ActiveVersionCheck = data;
        }

        public static void SetConfig(ZeonAssetConfig config)
        {
            Config = config ?? ZeonAssetConfig.CreateDefault();
        }

        /// <summary>
        /// 初始化资源系统（全异步）。
        /// Host 模式若 AutoUpdateOnInit=true，会跑 HostBoot（VersionCheck 决定 CDN）；
        /// 若注入了 CodeHotUpdateHook 再跑代码加载，然后 Shader 预热。
        /// 钩子为空时跳过代码加载。
        /// </summary>
        public static AsyncOperationBase InitializeAsync(ZeonAssetConfig config = null)
        {
            Config = config ?? Config ?? ZeonAssetConfig.CreateDefault();
            PlayModeServices = CreateServices(Config.PlayMode);
            OperationSystem.EnsureDriver();
            FileDownloader.SetMaxConcurrent(Config.MaxConcurrentDownloads);

            ZeonAssetLog.Info(ZeonAssetBootReport.CaptureStart(Config));

            var init = PlayModeServices.InitializeAsync(Config, Config.DefaultPackageName);

            bool needHostBoot = Config.PlayMode == EPlayMode.HostPlay
                                && Config.AutoUpdateOnInit;

            if (!needHostBoot)
            {
                WatchUpdateUi(init, isBoot: true);
                return init;
            }

            var boot = HostBootOperation.Create(init, Config);
            OperationSystem.Start(boot);
            WatchUpdateUi(boot, isBoot: true);
            return boot;
        }

        public static AsyncOperationBase InitializeAsync(EPlayMode playMode, string packageName = null, string bundleRoot = null)
        {
            var config = Config ?? ZeonAssetConfig.CreateDefault();
            config.PlayMode = playMode;
            if (!string.IsNullOrEmpty(packageName))
                config.DefaultPackageName = packageName;
            if (!string.IsNullOrEmpty(bundleRoot))
                config.BundleRoot = bundleRoot;
            return InitializeAsync(config);
        }

        /// <summary>热更：拉远端清单、比对；Full 策略下完脏 Bundle，OnDemand 只提交清单。</summary>
        public static HotUpdateOperation HotUpdateAsync(ZeonAssetConfig config = null)
        {
            EnsureServices();
            var op = HotUpdateOperation.Create(config ?? Config);
            OperationSystem.Start(op);
            WatchUpdateUi(op, isBoot: false);
            return op;
        }

        /// <summary>Shader 预热（加载 shaders.bundle + ShaderVariantCollection.WarmUp）。</summary>
        public static ShaderWarmupOperation WarmupShadersAsync(string shaderBundleName = null)
        {
            EnsureServices();
            var op = ShaderWarmupOperation.Create(shaderBundleName);
            OperationSystem.Start(op);
            return op;
        }

        public static LoadAssetOperation LoadAssetAsync<T>(string location) where T : UnityEngine.Object
        {
            return LoadAssetAsync(location, typeof(T));
        }

        public static LoadAssetOperation LoadAssetAsync(string location, Type type = null)
        {
            EnsureReady();
            return PlayModeServices.LoadAssetAsync(location, type ?? typeof(UnityEngine.Object));
        }

        public static LoadSubAssetsOperation LoadSubAssetsAsync<T>(string location) where T : UnityEngine.Object
        {
            return LoadSubAssetsAsync(location, typeof(T));
        }

        public static LoadSubAssetsOperation LoadSubAssetsAsync(string location, Type type = null)
        {
            EnsureReady();
            return PlayModeServices.LoadSubAssetsAsync(location, type ?? typeof(UnityEngine.Object));
        }

        public static LoadRawFileOperation LoadRawFileAsync(string location)
        {
            EnsureReady();
            var op = LoadRawFileOperation.Create(location);
            OperationSystem.Start(op);
            return op;
        }

        /// <summary>按 Tag 下载缺失 Bundle（不提交 ActiveManifest）。</summary>
        public static ZeonAssetDownloaderOperation CreateDownloaderByTags(params string[] tags)
        {
            EnsureReady();
            var op = ZeonAssetDownloaderOperation.CreateByTags(Config, tags);
            OperationSystem.Start(op);
            return op;
        }

        public static LoadSceneOperation LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single)
        {
            EnsureReady();
            return PlayModeServices.LoadSceneAsync(location, mode);
        }

        public static void Destroy()
        {
            FileDownloader.Clear();
            InflightAssetLoadRegistry.Clear();
            BundleMemoryCache.Clear();
            AssetLoaderManager.Clear();
            UnloadDelayQueue.ForceUnloadAll();
            BundleLoaderManager.ClearCache();
            OperationSystem.Clear();
            PlayModeServices = null;
            ActiveManifest = null;
            BuiltinManifest = null;
            ActiveVersionCheck = null;
            LoadingProgress.Reset();
        }

        public static void ForceUnloadUnusedBundles()
        {
            UnloadDelayQueue.ForceUnloadAll();
        }

        /// <summary>Dump 仍持有引用的 Bundle / Asset（诊断泄漏）。</summary>
        public static string DumpRefs(bool includeZeroRef = false)
        {
            return ZeonAssetLeakAnalyzer.Dump(includeZeroRef);
        }

        /// <summary>查询资源清单归属、引用数、Bundle 驻留状态。</summary>
        public static ZeonAssetDebugInfo QueryDebug(string location)
        {
            return ZeonAssetDebugQuery.Query(location);
        }

        private static void WatchUpdateUi(AsyncOperationBase op, bool isBoot)
        {
            if (op == null)
                return;

            LoadingProgress.Begin();
            op.Completed += watched =>
            {
                if (watched == null)
                    return;
                if (watched.IsSucceed)
                {
                    if (isBoot)
                        ZeonAssetLog.Info(ZeonAssetBootReport.CaptureEnd(watched));
                    LoadingProgress.Succeed();
                }
                else
                {
                    if (isBoot)
                        ZeonAssetLog.Error(ZeonAssetBootReport.CaptureEnd(watched));
                    LoadingProgress.FailIfIdle(watched.Error);
                }
            };
        }

        private static void EnsureServices()
        {
            if (PlayModeServices == null)
                throw new InvalidOperationException("AssetManager not initialized. Call InitializeAsync first.");
            OperationSystem.EnsureDriver();
        }

        private static void EnsureReady()
        {
            EnsureServices();
            if (ActiveManifest == null && Config.PlayMode != EPlayMode.EditorSimulate)
                throw new InvalidOperationException("ActiveManifest is null. Call HotUpdateAsync for Host mode first.");
        }

        private static IPlayModeServices CreateServices(EPlayMode playMode)
        {
            switch (playMode)
            {
                case EPlayMode.EditorSimulate:
                    return new EditorSimulateServices();
                case EPlayMode.HostPlay:
                    return new BundlePlayModeServices();
                default:
                    throw new ArgumentOutOfRangeException(nameof(playMode), playMode, null);
            }
        }

        private sealed class RawFileProviderFacade : IRawFileProvider
        {
            public LoadRawFileOperation LoadRawFileAsync(string location) => AssetManager.LoadRawFileAsync(location);

            public ZeonAssetDownloaderOperation CreateDownloaderByTags(params string[] tags) =>
                AssetManager.CreateDownloaderByTags(tags);
        }
    }
}
