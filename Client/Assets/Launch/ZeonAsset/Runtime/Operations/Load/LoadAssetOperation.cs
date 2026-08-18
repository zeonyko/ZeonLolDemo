using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步加载资源。
    /// </summary>
    public class LoadAssetOperation : PooledOperation<LoadAssetOperation, AssetHandle>
    {
        private string _location;
        private Type _assetType;
        private Func<string, Type, bool, LoadAssetOperation, bool> _providerStart;
        private Func<LoadAssetOperation, bool> _providerUpdate;
        private AssetBundleRequest _assetRequest;
        private AssetBundle _ownerBundle;
        private string _ownerBundleName;
        private string _assetPath;
        private string _address;
        private List<BundleLoader> _waitingLoaders;
        private BundleLoader _loadingHoldOwner;
        private long _inflightKey;
        private bool _isFollower;
        private LoadAssetOperation _primary;
        private ZeonAssetDownloaderOperation _pendingDownload;
        private bool _startedLoadersAfterDownload;

        public string Location => _location;
        public Type AssetType => _assetType;
        public long InflightKey => _inflightKey;

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "loc", _location);
            ZeonAssetLog.AppendKv(sb, "address", _address);
            ZeonAssetLog.AppendKv(sb, "asset", _assetPath);
            ZeonAssetLog.AppendKv(sb, "bundle", _ownerBundleName);
            ZeonAssetLog.AppendKv(sb, "type", _assetType != null ? _assetType.Name : null);
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

        internal AssetBundleRequest AssetRequest
        {
            get => _assetRequest;
            set => _assetRequest = value;
        }

        internal AssetBundle OwnerBundle
        {
            get => _ownerBundle;
            set => _ownerBundle = value;
        }

        internal string OwnerBundleName
        {
            get => _ownerBundleName;
            set => _ownerBundleName = value;
        }

        internal string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        internal string Address
        {
            get => _address;
            set => _address = value;
        }

        internal List<BundleLoader> WaitingLoaders
        {
            get => _waitingLoaders;
            set => _waitingLoaders = value;
        }

        /// <summary>加载期间持有 Owner Bundle；完成或失败后释放。</summary>
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

        public static LoadAssetOperation Create(
            string location,
            Type assetType,
            Func<string, Type, bool, LoadAssetOperation, bool> onStart,
            Func<LoadAssetOperation, bool> onUpdate)
        {
            var op = Rent();
            op._location = location;
            op._assetType = assetType ?? typeof(UnityEngine.Object);
            op._providerStart = onStart;
            op._providerUpdate = onUpdate;
            op._inflightKey = 0;
            op._isFollower = false;
            op._primary = null;
            return op;
        }

        public static LoadAssetOperation CreateFollower(string location, Type assetType, LoadAssetOperation primary)
        {
            var op = Rent();
            op._location = location;
            op._assetType = assetType ?? typeof(UnityEngine.Object);
            op._providerStart = null;
            op._providerUpdate = null;
            op._isFollower = true;
            op._primary = primary;
            op._inflightKey = 0;
            return op;
        }

        public static LoadAssetOperation CreateFromCache(string location, Type assetType, AssetLoader cached)
        {
            var op = Rent();
            op._location = location;
            op._assetType = assetType ?? typeof(UnityEngine.Object);
            op._providerStart = null;
            op._providerUpdate = null;
            op._isFollower = false;
            op._primary = null;
            op._address = cached.Address;
            op._assetPath = cached.AssetPath;
            // 在 Start 时完成
            op._providerStart = (_, __, ___, self) =>
            {
                self.Result = AssetHandle.Create(cached.Address, cached.AssetPath, cached.AssetObject, cached.OwnerBundle);
                self.Succeed();
                return true;
            };
            return op;
        }

        internal void SetInflightKey(long key) => _inflightKey = key;

        internal void CompleteAsFollower(LoadAssetOperation primary)
        {
            if (IsDone)
                return;

            if (primary != null && primary.IsSucceed && primary.Result != null && primary.Result.AssetObject != null)
            {
                Result = AssetHandle.Create(
                    primary.Result.Address,
                    primary.Result.AssetPath,
                    primary.Result.AssetObject,
                    null);
                _primary = null;
                Succeed();
                return;
            }

            FailSilent(primary?.Error ?? "Primary load failed.");
            _primary = null;
        }

        protected override void InternalOnStart()
        {
            if (_isFollower)
            {
                // 跟随者由 Inflight 注册表在主任务结束时 CompleteAsFollower
                if (_primary != null && _primary.IsDone)
                    CompleteAsFollower(_primary);
                return;
            }

            if (string.IsNullOrEmpty(_location))
            {
                Fail("Location is null or empty.");
                return;
            }

            if (_providerStart == null || !_providerStart(_location, _assetType, true, this))
            {
                if (!IsDone)
                    Fail($"Provider start failed: {_location}");
            }
        }

        protected override void InternalOnUpdate()
        {
            if (_isFollower)
            {
                if (_primary != null && _primary.IsDone && !IsDone)
                    CompleteAsFollower(_primary);
                else if (_primary != null)
                    Progress = _primary.Progress;
                return;
            }

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
                float sum = 0f;
                for (int i = 0; i < _waitingLoaders.Count; i++)
                {
                    var loader = _waitingLoaders[i];
                    if (loader.State == BundleLoader.ELoadState.Failed)
                    {
                        Fail(loader.Error);
                        return;
                    }

                    sum += loader.Progress;
                    if (loader.State != BundleLoader.ELoadState.Loaded)
                        return;
                }

                Progress = 0.7f * (sum / _waitingLoaders.Count);

                if (_ownerBundle == null)
                {
                    if (!string.IsNullOrEmpty(_ownerBundleName)
                        && BundleLoaderManager.TryGet(_ownerBundleName, out var ownerLoader)
                        && ownerLoader.Bundle != null)
                    {
                        _ownerBundle = ownerLoader.Bundle;
                    }
                }

                _waitingLoaders = null;

                if (_ownerBundle == null)
                {
                    Fail("Owner bundle is null after dependency load.");
                    return;
                }

                _assetRequest = _assetType == typeof(UnityEngine.Object)
                    ? _ownerBundle.LoadAssetAsync(_assetPath)
                    : _ownerBundle.LoadAssetAsync(_assetPath, _assetType);
            }

            if (_assetRequest != null)
            {
                Progress = 0.7f + 0.3f * _assetRequest.progress;
                if (!_assetRequest.isDone)
                    return;

                var asset = _assetRequest.asset;
                _assetRequest = null;
                if (asset == null)
                {
                    Fail($"Asset not found in bundle: {_assetPath}");
                    return;
                }

                BundleLoader ownerLoader = null;
                if (!string.IsNullOrEmpty(_ownerBundleName))
                    BundleLoaderManager.TryGet(_ownerBundleName, out ownerLoader);

                Result = AssetHandle.Create(_address ?? _location, _assetPath, asset, ownerLoader);
                Succeed();
            }
        }

        protected override void OnFinished()
        {
            // 先通知跟随者（此时 Result 仍可用），再释放加载期引用
            if (!_isFollower && _inflightKey != 0)
            {
                var key = _inflightKey;
                _inflightKey = 0;
                InflightAssetLoadRegistry.NotifyPrimaryFinished(key, this);
            }

            ReleaseLoadingHold();
        }

        protected override void OnCancelRequested()
        {
            _assetRequest = null;
            _waitingLoaders = null;
            if (_pendingDownload != null && !_pendingDownload.IsDone)
                _pendingDownload.Cancel();
            _pendingDownload = null;
            ReleaseLoadingHold();
            if (!_isFollower && _inflightKey != 0)
            {
                var key = _inflightKey;
                _inflightKey = 0;
                InflightAssetLoadRegistry.NotifyPrimaryFinished(key, this);
            }
        }

        /// <summary>EditorSimulate：同步取到资产后直接完成（仅编辑器模式）。</summary>
        internal void CompleteWithAsset(string address, string assetPath, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                Fail($"AssetDatabase load failed: {assetPath}");
                return;
            }

            Result = AssetHandle.Create(address, assetPath, asset, null);
            Succeed();
        }

        internal void CompleteWithError(string error) => Fail(error);

        internal override void OnRecycle()
        {
            ReleaseLoadingHold();
            _location = null;
            _assetType = null;
            _providerStart = null;
            _providerUpdate = null;
            _assetRequest = null;
            _ownerBundle = null;
            _ownerBundleName = null;
            _assetPath = null;
            _address = null;
            _waitingLoaders = null;
            _pendingDownload = null;
            _startedLoadersAfterDownload = false;
            _inflightKey = 0;
            _isFollower = false;
            _primary = null;
            base.OnRecycle();
        }
    }
}
