using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 按 Tag / Bundle 列表下载（不提交 ActiveManifest）。带字节进度。
    /// </summary>
    public class ZeonAssetDownloaderOperation : PooledOperation<ZeonAssetDownloaderOperation>
    {
        private ZeonAssetConfig _config;
        private string _sandboxRoot;
        private string _remoteRoot;
        private List<PackageBundle> _bundles;
        private readonly List<DownloadRequest> _requests = new List<DownloadRequest>();
        private int _finished;
        private int _failed;
        private bool _started;
        private long _totalBytes;
        private long _currentBytes;

        public int TotalCount => _bundles?.Count ?? 0;
        public int FinishedCount => _finished;
        public int FailedCount => _failed;
        public long TotalBytes => _totalBytes;
        public long CurrentBytes => _currentBytes;
        public string[] Tags { get; private set; }

        public static ZeonAssetDownloaderOperation CreateByTags(ZeonAssetConfig config, params string[] tags)
        {
            var manifest = AssetManager.ActiveManifest;
            var filtered = FilterBundlesByTags(manifest, tags);
            var op = CreateByBundles(config, filtered);
            op.Tags = tags;
            return op;
        }

        public static ZeonAssetDownloaderOperation CreateByBundles(
            ZeonAssetConfig config,
            List<PackageBundle> bundles)
        {
            var op = Rent();
            op._config = config ?? AssetManager.Config ?? ZeonAssetConfig.CreateDefault();
            op._sandboxRoot = DiskCacheManager.GetSandboxRoot();
            op._remoteRoot = (op._config.RemoteUrl ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            op._bundles = bundles ?? new List<PackageBundle>();
            op._finished = 0;
            op._failed = 0;
            op._started = false;
            op._totalBytes = 0;
            op._currentBytes = 0;
            op._requests.Clear();
            op.Tags = null;

            for (int i = 0; i < op._bundles.Count; i++)
                op._totalBytes += Math.Max(0, op._bundles[i].FileSize);

            return op;
        }

        public static List<PackageBundle> FilterBundlesByTags(PackageManifest manifest, string[] tags)
        {
            var result = new List<PackageBundle>();
            if (manifest?.Bundles == null || tags == null || tags.Length == 0)
                return result;

            var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            var sandbox = DiskCacheManager.GetSandboxRoot();
            var streaming = DiskCacheManager.GetStreamingRoot();

            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                var b = manifest.Bundles[i];
                if (b?.Tags == null || b.Tags.Count == 0)
                    continue;

                bool match = false;
                for (int t = 0; t < b.Tags.Count; t++)
                {
                    if (tagSet.Contains(b.Tags[t]))
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                    continue;
                if (DiskCacheManager.IsBundleAvailableLocally(
                        b, sandbox, streaming, AssetManager.BuiltinManifest))
                    continue;
                result.Add(b);
            }

            return result;
        }

        protected override void InternalOnStart()
        {
            FileDownloader.SetMaxConcurrent(_config.MaxConcurrentDownloads);
            DiskCacheManager.EnsureSandboxDirectories(_sandboxRoot);

            if (_bundles.Count == 0)
            {
                Progress = 1f;
                Succeed();
                return;
            }

            if (CdnUrlUtility.ResolveRemoteRoots(_config).Count == 0)
            {
                Fail("RemoteUrl/cdn_host 为空，无法按 Tag 下载。");
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

            RefreshByteProgress();

            if (_finished < TotalCount)
                return;

            if (_failed > 0)
            {
                Fail($"Tag download finished with {_failed} failures.");
                return;
            }

            Progress = 1f;
            Succeed();
        }

        private void RefreshByteProgress()
        {
            long current = 0;
            for (int i = 0; i < _requests.Count; i++)
            {
                var r = _requests[i];
                if (r.IsSucceed)
                    current += r.ExpectedSize > 0 ? r.ExpectedSize : r.DownloadedBytes;
                else
                    current += Math.Max(0, r.DownloadedBytes);
            }

            _currentBytes = current;
            if (_totalBytes > 0)
                Progress = Mathf.Clamp01((float)_currentBytes / _totalBytes);
            else if (TotalCount > 0)
                Progress = (float)_finished / TotalCount;
        }

        private void OnOneCompleted(DownloadRequest req)
        {
            _finished++;
            if (!req.IsSucceed)
            {
                _failed++;
                ZeonAssetLog.Error($"Tag download failed: {req.Url}, {req.Error}");
            }
        }

        protected override void OnCancelRequested()
        {
            for (int i = 0; i < _requests.Count; i++)
                FileDownloader.Cancel(_requests[i]);
        }

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            if (Tags != null && Tags.Length > 0)
                ZeonAssetLog.AppendKv(sb, "tags", string.Join(",", Tags));
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
            _sandboxRoot = null;
            _remoteRoot = null;
            _bundles = null;
            _requests.Clear();
            _finished = 0;
            _failed = 0;
            _started = false;
            _totalBytes = 0;
            _currentBytes = 0;
            Tags = null;
            base.OnRecycle();
        }
    }
}
