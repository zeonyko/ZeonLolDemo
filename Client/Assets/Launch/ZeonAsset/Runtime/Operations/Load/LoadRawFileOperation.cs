using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步加载原生字节（TextAsset 或磁盘文件）。配置表 / DLL.bytes 等均可。
    /// </summary>
    public class LoadRawFileOperation : PooledOperation<LoadRawFileOperation, RawFileHandle>
    {
        private enum EStep
        {
            Resolve,
            DownloadIfNeeded,
            LoadTextAsset,
            ReadDisk,
            Done,
        }

        private string _location;
        private EStep _step;
        private LoadAssetOperation _textOp;
        private ZeonAssetDownloaderOperation _dlOp;
        private UnityWebRequest _uwr;
        private string _diskPath;
        private string _address;
        private PackageBundle _bundle;

        public string Location => _location;

        protected override string DiagnosticStep => _step.ToString();

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "loc", _location);
            ZeonAssetLog.AppendKv(sb, "address", _address);
            ZeonAssetLog.AppendKv(sb, "disk", _diskPath);
            ZeonAssetLog.AppendKv(sb, "bundle", _bundle != null ? _bundle.BundleName : null);
        }

        public static LoadRawFileOperation Create(string location)
        {
            var op = Rent();
            op._location = location;
            op._step = EStep.Resolve;
            return op;
        }

        protected override void InternalOnStart()
        {
            if (string.IsNullOrEmpty(_location))
            {
                Fail("Location is null or empty.");
                return;
            }

            var manifest = AssetManager.ActiveManifest;
            if (manifest != null &&
                (manifest.TryGetAssetByAddress(_location, out var asset) ||
                 manifest.TryGetAsset(_location, out asset)))
            {
                _address = string.IsNullOrEmpty(asset.Address) ? asset.AssetPath : asset.Address;
                _bundle = manifest.GetBundleById(asset.BundleID);

                // 优先：若本地已有 Bundle 文件且 Asset 标记为 Raw，直接读盘
                if (asset.IsRawFile && _bundle != null)
                {
                    TryBeginDiskRead(_bundle.FileName);
                    return;
                }

                // 常规：按 TextAsset 从 AB 加载（.dll.bytes / 配置）
                _step = EStep.LoadTextAsset;
                _textOp = AssetManager.LoadAssetAsync<TextAsset>(_location);
                return;
            }

            // Editor / 无清单：当路径当本地文件
            if (_location.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                Path.IsPathRooted(_location))
            {
                _diskPath = _location;
                _address = _location;
                BeginUwrRead(_diskPath);
                return;
            }

            Fail($"RawFile not found: {_location}");
        }

        private void TryBeginDiskRead(string fileName)
        {
            var services = AssetManager.PlayModeServices as BundlePlayModeServices;
            var sandbox = services != null
                ? services.GetBundleRoot()
                : DiskCacheManager.GetSandboxRoot();
            var streaming = services != null
                ? services.GetStreamingRoot()
                : DiskCacheManager.GetStreamingRoot();

            foreach (var path in DiskCacheManager.EnumerateBundleCandidates(sandbox, streaming, fileName))
            {
                if (ZeonAssetPathHelper.CanProbeLocalFile(path) && File.Exists(path))
                {
                    _diskPath = path;
                    BeginUwrRead(path);
                    return;
                }
            }

            if (BundleMemoryCache.TryGet(fileName, out var cached))
            {
                Result = RawFileHandle.Create(_address ?? _location, _location, cached);
                Progress = 1f;
                Succeed();
                return;
            }

            foreach (var path in DiskCacheManager.EnumerateBundleCandidates(sandbox, streaming, fileName))
            {
                // WebGL / Android jar：Streaming 路径无法 File.Exists，仍用 UWR
                if (ZeonAssetPathHelper.IsStreamingAssetsLike(path))
                {
                    _diskPath = path;
                    BeginUwrRead(path);
                    return;
                }
            }

            // 本地没有：先按 Tag/单 Bundle 下载
            if (_bundle != null && CdnUrlUtility.ResolveRemoteRoots(AssetManager.Config).Count > 0)
            {
                _step = EStep.DownloadIfNeeded;
                _dlOp = ZeonAssetDownloaderOperation.CreateByBundles(
                    AssetManager.Config,
                    new List<PackageBundle> { _bundle });
                OperationSystem.Start(_dlOp);
                return;
            }

            // 回退 TextAsset
            _step = EStep.LoadTextAsset;
            _textOp = AssetManager.LoadAssetAsync<TextAsset>(_location);
        }

        private void BeginUwrRead(string path)
        {
            _step = EStep.ReadDisk;
            var url = ZeonAssetPathHelper.ToRequestUrl(path);
            _uwr = UnityWebRequest.Get(url);
            _uwr.timeout = Mathf.Max(1, (int)(AssetManager.Config?.TimeoutSeconds ?? 60f));
            _uwr.SendWebRequest();
        }

        protected override void InternalOnUpdate()
        {
            if (_step == EStep.DownloadIfNeeded)
            {
                if (_dlOp == null)
                    return;
                Progress = _dlOp.Progress * 0.5f;
                if (!_dlOp.IsDone)
                    return;
                if (!_dlOp.IsSucceed)
                {
                    FailFrom(_dlOp);
                    return;
                }

                _dlOp = null;
                TryBeginDiskRead(_bundle.FileName);
                return;
            }

            if (_step == EStep.LoadTextAsset)
            {
                if (_textOp == null)
                    return;
                Progress = _textOp.Progress;
                if (!_textOp.IsDone)
                    return;
                if (!_textOp.IsSucceed || _textOp.Result == null)
                {
                    FailFrom(_textOp);
                    _textOp = null;
                    return;
                }

                var text = _textOp.Result.GetAsset<TextAsset>();
                if (text == null)
                {
                    Fail("Asset is not TextAsset. Pack RawFile as TextAsset (.bytes) or set IsRawFile.");
                    _textOp.Result.Release();
                    _textOp = null;
                    return;
                }

                var bytes = text.bytes;
                var handle = RawFileHandle.Create(_address ?? _location, null, bytes);
                _textOp.Result.Release();
                _textOp = null;
                Result = handle;
                Succeed();
                return;
            }

            if (_step == EStep.ReadDisk && _uwr != null)
            {
                Progress = 0.5f + _uwr.downloadProgress * 0.5f;
                if (!_uwr.isDone)
                    return;

    #if UNITY_2020_2_OR_NEWER
                bool hasError = _uwr.result != UnityWebRequest.Result.Success;
    #else
                bool hasError = _uwr.isNetworkError || _uwr.isHttpError;
    #endif
                if (hasError)
                {
                    Fail($"Read RawFile failed: {_diskPath}, {_uwr.error}");
                    DisposeUwr();
                    return;
                }

                Result = RawFileHandle.Create(_address ?? _location, _diskPath, _uwr.downloadHandler.data);
                DisposeUwr();
                Succeed();
            }
        }

        protected override void OnCancelRequested()
        {
            _textOp?.Cancel();
            _dlOp?.Cancel();
            if (_uwr != null)
            {
                _uwr.Abort();
                DisposeUwr();
            }
        }

        private void DisposeUwr()
        {
            if (_uwr == null)
                return;
            _uwr.Dispose();
            _uwr = null;
        }

        internal override void OnRecycle()
        {
            DisposeUwr();
            _location = null;
            _textOp = null;
            _dlOp = null;
            _diskPath = null;
            _address = null;
            _bundle = null;
            _step = EStep.Resolve;
            base.OnRecycle();
        }
    }
}
