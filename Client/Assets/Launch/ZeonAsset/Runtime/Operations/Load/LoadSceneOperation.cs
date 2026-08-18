using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步加载场景。
    /// </summary>
    public class LoadSceneOperation : PooledOperation<LoadSceneOperation, SceneHandle>
    {
        private string _location;
        private UnityEngine.SceneManagement.LoadSceneMode _sceneMode;
        private Func<string, UnityEngine.SceneManagement.LoadSceneMode, LoadSceneOperation, bool> _providerStart;
        private Func<LoadSceneOperation, bool> _providerUpdate;
        private AsyncOperation _unitySceneOp;
        private List<BundleLoader> _waitingLoaders;
        private string _scenePath;
        private string _address;
        private string _ownerBundleName;
        private BundleLoader _loadingHoldOwner;
        private ZeonAssetDownloaderOperation _pendingDownload;
        private bool _startedLoadersAfterDownload;

        public string Location => _location;
        public UnityEngine.SceneManagement.LoadSceneMode SceneMode => _sceneMode;

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "loc", _location);
            ZeonAssetLog.AppendKv(sb, "address", _address);
            ZeonAssetLog.AppendKv(sb, "scene", _scenePath);
            ZeonAssetLog.AppendKv(sb, "bundle", _ownerBundleName);
        }

        internal ZeonAssetDownloaderOperation PendingDownload
        {
            get => _pendingDownload;
            set
            {
                _pendingDownload = value;
                _startedLoadersAfterDownload = false;
            }
        }

        internal AsyncOperation UnitySceneOp
        {
            get => _unitySceneOp;
            set => _unitySceneOp = value;
        }

        internal List<BundleLoader> WaitingLoaders
        {
            get => _waitingLoaders;
            set => _waitingLoaders = value;
        }

        internal string ScenePath
        {
            get => _scenePath;
            set => _scenePath = value;
        }

        internal string Address
        {
            get => _address;
            set => _address = value;
        }

        internal string OwnerBundleName
        {
            get => _ownerBundleName;
            set => _ownerBundleName = value;
        }

        internal void HoldLoadingBundles(BundleLoader owner)
        {
            if (owner == null || _loadingHoldOwner != null)
                return;
            _loadingHoldOwner = owner;
            owner.Retain();
        }

        internal void ReleaseLoadingHold()
        {
            if (_loadingHoldOwner == null)
                return;
            _loadingHoldOwner.Release();
            _loadingHoldOwner = null;
        }

        public static LoadSceneOperation Create(
            string location,
            UnityEngine.SceneManagement.LoadSceneMode sceneMode,
            Func<string, UnityEngine.SceneManagement.LoadSceneMode, LoadSceneOperation, bool> onStart,
            Func<LoadSceneOperation, bool> onUpdate)
        {
            var op = Rent();
            op._location = location;
            op._sceneMode = sceneMode;
            op._providerStart = onStart;
            op._providerUpdate = onUpdate;
            return op;
        }

        protected override void InternalOnStart()
        {
            if (string.IsNullOrEmpty(_location))
            {
                Fail("Scene location is null or empty.");
                return;
            }

            if (_providerStart == null || !_providerStart(_location, _sceneMode, this))
            {
                if (!IsDone)
                    Fail($"Scene provider start failed: {_location}");
            }
        }

        protected override void InternalOnUpdate()
        {
            if (_providerUpdate != null)
            {
                if (_providerUpdate(this))
                    return;
            }

            if (_pendingDownload != null)
            {
                Progress = _pendingDownload.Progress * 0.3f;
                if (!_pendingDownload.IsDone)
                    return;
                if (!_pendingDownload.IsSucceed)
                {
                    FailFrom(_pendingDownload);
                    return;
                }

                if (!_startedLoadersAfterDownload)
                {
                    _startedLoadersAfterDownload = true;
                    var services = AssetManager.PlayModeServices as BundlePlayModeServices;
                    BundlePlayModeServices.RefreshLoaderPathsAfterDownload(
                        _waitingLoaders,
                        services?.GetBundleRoot() ?? DiskCacheManager.GetSandboxRoot(),
                        services?.GetStreamingRoot() ?? DiskCacheManager.GetStreamingRoot());
                    if (_waitingLoaders != null)
                    {
                        for (int i = 0; i < _waitingLoaders.Count; i++)
                            _waitingLoaders[i].StartLoad();
                    }
                }

                _pendingDownload = null;
            }

            if (_waitingLoaders != null && _waitingLoaders.Count > 0)
            {
                for (int i = 0; i < _waitingLoaders.Count; i++)
                {
                    var loader = _waitingLoaders[i];
                    if (loader.State == BundleLoader.ELoadState.Failed)
                    {
                        Fail(loader.Error);
                        return;
                    }

                    if (loader.State != BundleLoader.ELoadState.Loaded)
                        return;
                }

                _waitingLoaders = null;
                // Bundle 场景名通常是场景文件名（不含路径）
                var sceneName = Path.GetFileNameWithoutExtension(_scenePath);
                _unitySceneOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, _sceneMode);
                if (_unitySceneOp == null)
                {
                    Fail($"SceneManager.LoadSceneAsync failed: {sceneName}");
                    return;
                }
            }

            if (_unitySceneOp != null)
            {
                Progress = _unitySceneOp.progress;
                if (!_unitySceneOp.isDone)
                    return;

                BundleLoader ownerLoader = null;
                if (!string.IsNullOrEmpty(_ownerBundleName))
                    BundleLoaderManager.TryGet(_ownerBundleName, out ownerLoader);

                Result = SceneHandle.Create(_address ?? _location, _scenePath, _unitySceneOp, ownerLoader);
                Succeed();
            }
        }

        protected override void OnFinished()
        {
            ReleaseLoadingHold();
        }

        internal void CompleteWithScene(string address, string scenePath, AsyncOperation unityOp)
        {
            if (unityOp == null)
            {
                Fail($"Scene load failed: {scenePath}");
                return;
            }

            // 仍需等待 unityOp 完成
            _unitySceneOp = unityOp;
            _address = address;
            _scenePath = scenePath;
        }

        internal void CompleteWithError(string error) => Fail(error);

        internal override void OnRecycle()
        {
            ReleaseLoadingHold();
            _location = null;
            _providerStart = null;
            _providerUpdate = null;
            _unitySceneOp = null;
            _waitingLoaders = null;
            _scenePath = null;
            _address = null;
            _ownerBundleName = null;
            _pendingDownload = null;
            _startedLoadersAfterDownload = false;
            base.OnRecycle();
        }
    }
}
