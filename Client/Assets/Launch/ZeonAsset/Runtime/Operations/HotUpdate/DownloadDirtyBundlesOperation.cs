using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 批量下载缺失/损坏 Bundle（写入 Cache/tmp，校验后原子移入 Bundles/）。
    /// </summary>
    public class DownloadDirtyBundlesOperation : PooledOperation<DownloadDirtyBundlesOperation>
    {
        private ZeonAssetConfig _config;
        private string _packageName;
        private string _sandboxRoot;
        private string _remoteRoot;
        private List<PackageBundle> _bundles;
        private PackageManifest _remoteManifest;
        private VersionCheckData _targetVersion;
        private string _tempManifestPath;
        private readonly List<DownloadRequest> _requests = new List<DownloadRequest>();
        private int _finished;
        private int _failed;
        private bool _started;

        public int TotalCount => _bundles?.Count ?? 0;
        public int FinishedCount => _finished;
        public int FailedCount => _failed;
        public long TotalBytes { get; private set; }
        public long CurrentBytes { get; private set; }

        public static DownloadDirtyBundlesOperation Create(
            ZeonAssetConfig config,
            string packageName,
            string sandboxRoot,
            PackageManifest remoteManifest,
            List<PackageBundle> dirtyBundles,
            VersionCheckData targetVersion = null,
            string tempManifestPath = null)
        {
            var op = Rent();
            op._config = config ?? ZeonAssetConfig.CreateDefault();
            op._packageName = packageName;
            op._sandboxRoot = sandboxRoot;
            op._remoteRoot = (config?.RemoteUrl ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            op._remoteManifest = remoteManifest;
            op._targetVersion = targetVersion;
            op._tempManifestPath = tempManifestPath;
            op._bundles = dirtyBundles ?? new List<PackageBundle>();
            op._finished = 0;
            op._failed = 0;
            op._started = false;
            op._requests.Clear();
            op.TotalBytes = 0;
            op.CurrentBytes = 0;
            for (int i = 0; i < op._bundles.Count; i++)
                op.TotalBytes += Math.Max(0, op._bundles[i].FileSize);
            return op;
        }

        protected override void InternalOnStart()
        {
            FileDownloader.SetMaxConcurrent(_config.MaxConcurrentDownloads);
            DiskCacheManager.EnsureSandboxDirectories(_sandboxRoot);

            if (_bundles.Count == 0)
            {
                CommitManifest();
                Succeed();
                return;
            }

            for (int i = 0; i < _bundles.Count; i++)
            {
                var b = _bundles[i];
                var savePath = DiskCacheManager.GetBundlePath(_sandboxRoot, b.FileName);
                if (ZeonAssetPathHelper.CanProbeLocalFile(savePath) &&
                    File.Exists(savePath) &&
                    !DiskCacheManager.ValidateFile(savePath, b.CRC, b.FileSize))
                    DiskCacheManager.DeleteCorrupted(savePath, _sandboxRoot);

                var urls = CdnUrlUtility.BuildBundleUrlCandidates(_config, b.FileName);
                var req = DownloadRequest.CreateWithFailover(
                    urls, savePath, b.CRC, b.FileSize, _config.DownloadRetryCount);
                var tempKey = !string.IsNullOrEmpty(b.Hash) ? b.Hash : Path.GetFileNameWithoutExtension(b.FileName);
                req.TempPathOverride = DiskCacheManager.GetTempPath(_sandboxRoot, tempKey);

                req.OnCompleted = OnOneCompleted;
                _requests.Add(req);
                FileDownloader.Enqueue(req);
            }

            _started = true;
        }

        protected override void InternalOnUpdate()
        {
            if (!_started)
                return;

            long current = 0;
            for (int i = 0; i < _requests.Count; i++)
            {
                var r = _requests[i];
                if (r.IsSucceed)
                    current += r.ExpectedSize > 0 ? r.ExpectedSize : r.DownloadedBytes;
                else
                    current += Math.Max(0, r.DownloadedBytes);
            }

            CurrentBytes = current;
            if (TotalBytes > 0)
                Progress = Mathf.Clamp01((float)CurrentBytes / TotalBytes);
            else if (TotalCount > 0)
                Progress = (float)_finished / TotalCount;

            LoadingProgress.SetDownload(CurrentBytes, TotalBytes, _finished, TotalCount);
            LoadingProgress.SetProgress(
                ELoadingPhase.Downloading, Progress, "正在下载资源");

            if (_finished < TotalCount)
                return;

            if (_failed > 0)
            {
                Fail($"Download finished with {_failed} failures.");
                return;
            }

            CommitManifest();
            Succeed();
        }

        private void OnOneCompleted(DownloadRequest req)
        {
            _finished++;
            if (!req.IsSucceed)
            {
                _failed++;
                ZeonAssetLog.Error($"Bundle download failed: {req.Url}, {req.Error}");
            }
        }

        protected override void OnCancelRequested()
        {
            for (int i = 0; i < _requests.Count; i++)
                FileDownloader.Cancel(_requests[i]);
        }

        private void CommitManifest()
        {
            // 原子提交：Cache/tmp/target.bytes → manifest_active.bytes（不可持久化平台跳过）
            var tmp = _tempManifestPath;
            if (string.IsNullOrEmpty(tmp))
                tmp = DiskCacheManager.GetTempManifestPath(_sandboxRoot);

            try
            {
                if (ZeonAssetPathHelper.CanUsePersistentFileCache() &&
                    !string.IsNullOrEmpty(tmp) &&
                    File.Exists(tmp))
                    DiskCacheManager.CommitActiveManifest(_sandboxRoot, tmp);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"CommitActiveManifest soft-fail: {e.Message}");
            }

            AssetManager.SetActiveVersionCheck(_targetVersion);

            if (_config != null && _config.EnableSandboxGcAfterUpdate && _remoteManifest != null)
            {
                PackageManifest builtin = AssetManager.BuiltinManifest;
                if (builtin == null)
                    DiskCacheManager.TryReadManifestFile(
                        DiskCacheManager.GetBuiltinManifestPath(), out builtin);
                DiskCacheManager.GarbageCollectSandboxBundles(_remoteManifest, builtin);
            }

            DiskCacheManager.ClearTempDir(_sandboxRoot);
        }

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "cdn", _remoteRoot);
            ZeonAssetLog.AppendKv(sb, "failed", _failed + "/" + TotalCount);
            int listed = 0;
            for (int i = 0; i < _requests.Count && listed < 5; i++)
            {
                var r = _requests[i];
                if (r == null || r.IsSucceed)
                    continue;
                ZeonAssetLog.AppendKv(sb, "fail[" + listed + "]", r.Url + " | " + r.Error);
                listed++;
            }
        }

        internal override void OnRecycle()
        {
            _config = null;
            _packageName = null;
            _sandboxRoot = null;
            _remoteRoot = null;
            _bundles = null;
            _remoteManifest = null;
            _targetVersion = null;
            _tempManifestPath = null;
            _requests.Clear();
            _finished = 0;
            _failed = 0;
            _started = false;
            TotalBytes = 0;
            CurrentBytes = 0;
            base.OnRecycle();
        }
    }
}
