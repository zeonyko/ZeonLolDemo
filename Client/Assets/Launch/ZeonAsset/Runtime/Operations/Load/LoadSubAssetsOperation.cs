using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步加载子资源（图集 Sprite 等）。
    /// </summary>
    public class LoadSubAssetsOperation : PooledOperation<LoadSubAssetsOperation, SubAssetHandle>
    {
        private string _location;
        private Type _assetType;
        private Func<string, Type, LoadSubAssetsOperation, bool> _providerStart;
        private List<BundleLoader> _waitingLoaders;
        private BundleLoader _loadingHoldOwner;
        private AssetBundleRequest _assetRequest;
        private AssetBundle _ownerBundle;
        private string _ownerBundleName;
        private string _assetPath;
        private string _address;
        private ZeonAssetDownloaderOperation _pendingDownload;
        private bool _startedLoadersAfterDownload;

        public string Location => _location;

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

        internal string Address
        {
            get => _address;
            set => _address = value;
        }

        internal string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        internal string OwnerBundleName
        {
            get => _ownerBundleName;
            set => _ownerBundleName = value;
        }

        internal List<BundleLoader> WaitingLoaders
        {
            get => _waitingLoaders;
            set => _waitingLoaders = value;
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

        public static LoadSubAssetsOperation Create(
            string location,
            Type assetType,
            Func<string, Type, LoadSubAssetsOperation, bool> onStart)
        {
            var op = Rent();
            op._location = location;
            op._assetType = assetType ?? typeof(UnityEngine.Object);
            op._providerStart = onStart;
            return op;
        }

        protected override void InternalOnStart()
        {
            if (string.IsNullOrEmpty(_location))
            {
                Fail("Location is null or empty.");
                return;
            }

            if (_providerStart == null || !_providerStart(_location, _assetType, this))
            {
                if (!IsDone)
                    Fail($"Provider start failed: {_location}");
            }
        }

        protected override void InternalOnUpdate()
        {
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

                if (_ownerBundle == null &&
                    !string.IsNullOrEmpty(_ownerBundleName) &&
                    BundleLoaderManager.TryGet(_ownerBundleName, out var ownerLoader))
                    _ownerBundle = ownerLoader.Bundle;

                _waitingLoaders = null;

                if (_ownerBundle == null)
                {
                    Fail("Owner bundle is null after dependency load.");
                    return;
                }

                _assetRequest = _assetType == typeof(UnityEngine.Object)
                    ? _ownerBundle.LoadAssetWithSubAssetsAsync(_assetPath)
                    : _ownerBundle.LoadAssetWithSubAssetsAsync(_assetPath, _assetType);
            }

            if (_assetRequest != null)
            {
                Progress = 0.7f + 0.3f * _assetRequest.progress;
                if (!_assetRequest.isDone)
                    return;

                var all = _assetRequest.allAssets;
                _assetRequest = null;
                if (all == null || all.Length == 0)
                {
                    Fail($"SubAssets not found in bundle: {_assetPath}");
                    return;
                }

                BundleLoader owner = null;
                if (!string.IsNullOrEmpty(_ownerBundleName))
                    BundleLoaderManager.TryGet(_ownerBundleName, out owner);

                var main = all[0];
                var mainHandle = AssetHandle.Create(_address ?? _location, _assetPath, main, owner);
                Result = SubAssetHandle.Create(mainHandle, all);
                Succeed();
            }
        }

        protected override void OnFinished()
        {
            ReleaseLoadingHold();
        }

        internal void CompleteWithSubAssets(string address, string assetPath, UnityEngine.Object[] assets)
        {
            if (assets == null || assets.Length == 0)
            {
                Fail($"SubAssets empty: {assetPath}");
                return;
            }

            var mainHandle = AssetHandle.Create(address, assetPath, assets[0], null);
            Result = SubAssetHandle.Create(mainHandle, assets);
            Succeed();
        }

        internal void CompleteWithError(string error) => Fail(error);

        internal override void OnRecycle()
        {
            ReleaseLoadingHold();
            _location = null;
            _assetType = null;
            _providerStart = null;
            _waitingLoaders = null;
            _assetRequest = null;
            _ownerBundle = null;
            _ownerBundleName = null;
            _assetPath = null;
            _address = null;
            _pendingDownload = null;
            _startedLoadersAfterDownload = false;
            base.OnRecycle();
        }
    }
}
