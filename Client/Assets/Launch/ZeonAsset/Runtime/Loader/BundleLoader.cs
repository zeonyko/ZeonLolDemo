using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 单个 Bundle 的异步加载与依赖引用计数。
    /// 引用归零后延迟卸载；卸载时保留已实例化对象。
    /// </summary>
    public class BundleLoader : RefCountObject
    {
        public enum ELoadState
        {
            None,
            Loading,
            Loaded,
            Failed,
        }

        public string BundleName { get; private set; }
        public string FileName { get; private set; }
        public string FullPath { get; private set; }
        public EBundleEncryptMode EncryptMode { get; private set; }
        public int LoadOffset { get; private set; }
        public ELoadState State { get; private set; } = ELoadState.None;
        public string Error { get; private set; }
        public AssetBundle Bundle { get; private set; }
        public float Progress { get; private set; }
        public bool IsInDelayUnload => UnloadDelayQueue.Contains(this);

        private enum ELoadChannel
        {
            None,
            FileCreateRequest,
            WebAssetBundle,
            WebBytes,
            MemoryCreateRequest,
        }

        private readonly List<BundleLoader> _directDependencies = new List<BundleLoader>(4);
        private AssetBundleCreateRequest _fileRequest;
        private UnityWebRequest _webRequest;
        private ELoadChannel _channel;

        internal void Setup(
            string bundleName,
            string fileName,
            string fullPath,
            EBundleEncryptMode encryptMode = EBundleEncryptMode.None,
            int loadOffset = 0)
        {
            BundleName = bundleName;
            FileName = fileName;
            FullPath = fullPath;
            EncryptMode = encryptMode;
            LoadOffset = Math.Max(0, loadOffset);
            State = ELoadState.None;
            Error = null;
            Bundle = null;
            Progress = 0f;
            _fileRequest = null;
            _webRequest = null;
            _channel = ELoadChannel.None;
            _directDependencies.Clear();
            ResetRefCount();
        }

        internal void RefreshPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return;
            FullPath = fullPath;
        }

        /// <summary>设置直接依赖。已有引用时不允许修改依赖关系。</summary>
        internal bool SetDirectDependencies(List<BundleLoader> dependencies)
        {
            if (RefCount > 0)
            {
                // 已有引用时改依赖边会导致旧依赖泄漏 / 新依赖多次 Release
                if (!DependenciesEqual(dependencies))
                {
                    ZeonAssetLog.Warn(
                        $"Ignore SetDirectDependencies while RefCount={RefCount}: {BundleName}");
                }

                return false;
            }

            _directDependencies.Clear();
            if (dependencies == null)
                return true;

            for (int i = 0; i < dependencies.Count; i++)
            {
                var dep = dependencies[i];
                if (dep != null && !ReferenceEquals(dep, this) && !_directDependencies.Contains(dep))
                    _directDependencies.Add(dep);
            }

            return true;
        }

        private bool DependenciesEqual(List<BundleLoader> dependencies)
        {
            int count = dependencies?.Count ?? 0;
            int valid = 0;
            if (dependencies != null)
            {
                for (int i = 0; i < dependencies.Count; i++)
                {
                    var dep = dependencies[i];
                    if (dep != null && !ReferenceEquals(dep, this))
                        valid++;
                }
            }

            if (valid != _directDependencies.Count)
                return false;

            if (dependencies == null)
                return true;

            for (int i = 0; i < dependencies.Count; i++)
            {
                var dep = dependencies[i];
                if (dep == null || ReferenceEquals(dep, this))
                    continue;
                if (!_directDependencies.Contains(dep))
                    return false;
            }

            return true;
        }

        public override void Retain()
        {
            // 从延迟卸载队列唤醒
            if (RefCount == 0)
                UnloadDelayQueue.Cancel(this);

            base.Retain();

            // 级联：主包被引用时，直接依赖也 +1
            for (int i = 0; i < _directDependencies.Count; i++)
                _directDependencies[i].Retain();
        }

        public override void Release()
        {
            // 先释放依赖，再释放自己（与 Retain 对称）
            for (int i = 0; i < _directDependencies.Count; i++)
                _directDependencies[i].Release();

            base.Release();
        }

        protected override void OnRefZero()
        {
            // 延迟卸载，避免短时间内重复加载。
            UnloadDelayQueue.Enqueue(this);
        }

        /// <summary>卸载 Bundle，但保留已实例化对象。</summary>
        internal void ExecuteUnload()
        {
            if (Bundle != null)
            {
                // 禁止 Unload(true)，避免已实例化对象材质变粉/丢失
                Bundle.Unload(false);
                Bundle = null;
            }

            State = ELoadState.None;
            Progress = 0f;
            Error = null;
            _channel = ELoadChannel.None;
            _fileRequest = null;
            if (_webRequest != null)
            {
                _webRequest.Dispose();
                _webRequest = null;
            }
        }

        internal void StartLoad()
        {
            if (State == ELoadState.Loaded || State == ELoadState.Loading)
                return;

            UnloadDelayQueue.Cancel(this);
            State = ELoadState.Loading;
            Error = null;
            _channel = ELoadChannel.None;
            _fileRequest = null;
            _webRequest = null;

            if (string.IsNullOrEmpty(FullPath) && !BundleMemoryCache.Contains(FileName))
            {
                Fail("Bundle path is empty.");
                return;
            }

            try
            {
                // 1) 内存缓存（WebGL 热更下载结果）
                if (BundleMemoryCache.TryGet(FileName, out var cached))
                {
                    BeginMemoryLoad(DecryptBytes(cached));
                    return;
                }

                // 2) 可探测本地文件：LoadFromFile / 读盘解密
                if (ZeonAssetPathHelper.CanProbeLocalFile(FullPath) && File.Exists(FullPath))
                {
                    BeginLocalFileLoad();
                    return;
                }

                // 3) URL / StreamingAssets / jar：UWR
                var url = ZeonAssetPathHelper.ToRequestUrl(FullPath);
                if (ZeonAssetPathHelper.IsFileUrl(url) &&
                    !ZeonAssetPathHelper.CanUsePersistentFileCache())
                {
                    Fail(
                        "WebGL/不可持久化平台不支持 file:// Bundle URL，请使用 http(s) CDN 或 StreamingAssets。 path=" +
                        FullPath);
                    return;
                }

                bool needDecrypt =
                    EncryptMode == EBundleEncryptMode.Xor ||
                    (EncryptMode == EBundleEncryptMode.Offset && LoadOffset > 0);

                if (needDecrypt)
                {
                    _channel = ELoadChannel.WebBytes;
                    _webRequest = UnityWebRequest.Get(url);
                }
                else
                {
                    _channel = ELoadChannel.WebAssetBundle;
                    _webRequest = UnityWebRequestAssetBundle.GetAssetBundle(url);
                }

                _webRequest.timeout = Mathf.Max(1, (int)(AssetManager.Config?.TimeoutSeconds ?? 60f));
                _webRequest.SendWebRequest();
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        private void BeginLocalFileLoad()
        {
            if (EncryptMode == EBundleEncryptMode.Xor)
            {
                var raw = File.ReadAllBytes(FullPath);
                BeginMemoryLoad(DecryptBytes(raw));
                return;
            }

            if (EncryptMode == EBundleEncryptMode.Offset && LoadOffset > 0)
            {
                _channel = ELoadChannel.FileCreateRequest;
                _fileRequest = AssetBundle.LoadFromFileAsync(FullPath, 0, (ulong)LoadOffset);
                return;
            }

            _channel = ELoadChannel.FileCreateRequest;
            _fileRequest = AssetBundle.LoadFromFileAsync(FullPath);
        }

        private void BeginMemoryLoad(byte[] plain)
        {
            if (plain == null || plain.Length == 0)
            {
                Fail("Bundle bytes empty after decrypt.");
                return;
            }

            _channel = ELoadChannel.MemoryCreateRequest;
            _fileRequest = AssetBundle.LoadFromMemoryAsync(plain);
        }

        private byte[] DecryptBytes(byte[] raw)
        {
            var decrypter = AssetManager.Config?.ResolveDecrypter(
                new PackageBundle
                {
                    BundleName = BundleName,
                    EncryptMode = EncryptMode,
                    LoadOffset = LoadOffset,
                }) ?? NullDecrypter.Instance;
            return decrypter.Decrypt(raw, BundleName);
        }

        internal void Update()
        {
            if (State != ELoadState.Loading)
                return;

            switch (_channel)
            {
                case ELoadChannel.WebAssetBundle:
                    TickWebAssetBundle();
                    return;
                case ELoadChannel.WebBytes:
                    TickWebBytes();
                    return;
                case ELoadChannel.FileCreateRequest:
                case ELoadChannel.MemoryCreateRequest:
                    TickCreateRequest();
                    return;
                default:
                    Fail("Load channel is none.");
                    return;
            }
        }

        private void TickWebAssetBundle()
        {
            if (_webRequest == null)
            {
                Fail("WebRequest is null.");
                return;
            }

            Progress = _webRequest.downloadProgress;
            if (!_webRequest.isDone)
                return;

            if (HasUwrError(_webRequest))
            {
                Fail(_webRequest.error);
                return;
            }

            Bundle = DownloadHandlerAssetBundle.GetContent(_webRequest);
            DisposeWebRequest();
            if (Bundle == null)
            {
                DiskCacheManager.DeleteCorrupted(FullPath);
                Fail("DownloadHandlerAssetBundle returned null (corrupted deleted).");
                return;
            }

            Progress = 1f;
            State = ELoadState.Loaded;
        }

        private void TickWebBytes()
        {
            if (_webRequest == null)
            {
                Fail("WebRequest is null.");
                return;
            }

            Progress = _webRequest.downloadProgress * 0.85f;
            if (!_webRequest.isDone)
                return;

            if (HasUwrError(_webRequest))
            {
                Fail(_webRequest.error);
                return;
            }

            var raw = _webRequest.downloadHandler?.data;
            DisposeWebRequest();
            if (raw == null || raw.Length == 0)
            {
                Fail("Downloaded bundle bytes empty.");
                return;
            }

            BeginMemoryLoad(DecryptBytes(raw));
        }

        private void TickCreateRequest()
        {
            if (_fileRequest == null)
            {
                Fail("FileRequest is null.");
                return;
            }

            Progress = _channel == ELoadChannel.MemoryCreateRequest
                ? 0.85f + _fileRequest.progress * 0.15f
                : _fileRequest.progress;
            if (!_fileRequest.isDone)
                return;

            Bundle = _fileRequest.assetBundle;
            _fileRequest = null;
            if (Bundle == null)
            {
                DiskCacheManager.DeleteCorrupted(FullPath);
                BundleMemoryCache.Remove(FileName);
                Fail($"LoadFromMemory/File failed (corrupted deleted): {FullPath}");
                return;
            }

            Progress = 1f;
            State = ELoadState.Loaded;
        }

        private static bool HasUwrError(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result != UnityWebRequest.Result.Success;
#else
            return request.isNetworkError || request.isHttpError;
#endif
        }

        private void Fail(string error)
        {
            Error = error;
            State = ELoadState.Failed;
            Progress = 0f;
            DisposeWebRequest();
            _fileRequest = null;
            _channel = ELoadChannel.None;
            ZeonAssetLog.Error($"BundleLoader failed: {BundleName}, {error}");
        }

        private void DisposeWebRequest()
        {
            if (_webRequest == null)
                return;
            _webRequest.Dispose();
            _webRequest = null;
        }
    }

    /// <summary>
    /// BundleLoader 工厂与缓存，避免重复加载同名 Bundle。
    /// </summary>
    public static class BundleLoaderManager
    {
        private static readonly Dictionary<string, BundleLoader> Loaders =
            new Dictionary<string, BundleLoader>(StringComparer.OrdinalIgnoreCase);

        private static readonly ObjectPool<BundleLoader> Pool = new ObjectPool<BundleLoader>();

        public static BundleLoader GetOrCreate(
            string bundleName,
            string fileName,
            string fullPath,
            EBundleEncryptMode encryptMode = EBundleEncryptMode.None,
            int loadOffset = 0)
        {
            if (Loaders.TryGetValue(bundleName, out var existing))
            {
                // 已缓存：即使已 Unload(false)，也复用同一 Loader，避免 Duplicate Bundle Entry
                if (string.IsNullOrEmpty(existing.FullPath) && !string.IsNullOrEmpty(fullPath))
                    existing.Setup(bundleName, fileName, fullPath, encryptMode, loadOffset);
                else if (!string.IsNullOrEmpty(fullPath) &&
                         !string.Equals(existing.FullPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                         existing.State == BundleLoader.ELoadState.None)
                    existing.RefreshPath(fullPath);
                return existing;
            }

            var loader = Pool.Get();
            loader.Setup(bundleName, fileName, fullPath, encryptMode, loadOffset);
            Loaders[bundleName] = loader;
            return loader;
        }

        public static bool TryGet(string bundleName, out BundleLoader loader)
        {
            return Loaders.TryGetValue(bundleName, out loader);
        }

        public static IEnumerable<BundleLoader> Enumerate() => Loaders.Values;

        public static void Update()
        {
            UnityEngine.Profiling.Profiler.BeginSample("Resource.BundleLoaderManager");
            try
            {
                foreach (var pair in Loaders)
                    pair.Value.Update();

                UnloadDelayQueue.Update();
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        /// <summary>强制卸载全部（Destroy / 低内存）。统一 Unload(false)。</summary>
        public static void UnloadAll(bool unloadAllLoadedObjects = false)
        {
            // 阶段3约定：忽略 true，强制 false，防止粉色材质
            if (unloadAllLoadedObjects)
                ZeonAssetLog.Warn("UnloadAll(true) is blocked. Forcing Unload(false).");

            UnloadDelayQueue.Clear();
            foreach (var pair in Loaders)
                pair.Value.ExecuteUnload();
        }

        public static void ClearCache()
        {
            UnloadAll(false);
            foreach (var pair in Loaders)
                Pool.Release(pair.Value);
            Loaders.Clear();
        }

        public static int CachedCount => Loaders.Count;
    }
}
