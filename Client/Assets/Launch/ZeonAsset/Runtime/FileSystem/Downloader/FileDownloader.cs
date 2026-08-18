using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 文件下载器：可持久化平台 = FileStream 断点续传；否则 = 内存下载 + BundleMemoryCache。
    /// </summary>
    public static class FileDownloader
    {
        private static readonly List<DownloadRequest> Waiting = new List<DownloadRequest>(32);
        private static readonly List<ActiveDownload> Active = new List<ActiveDownload>(8);
        private static int _maxConcurrent = 4;

        private class ActiveDownload
        {
            public DownloadRequest Request;
            public UnityWebRequest WebRequest;
            public FileStream Stream;
            public long StartOffset;
            public bool MemoryMode;
        }

        public static int WaitingCount => Waiting.Count;
        public static int ActiveCount => Active.Count;

        public static void SetMaxConcurrent(int max)
        {
            _maxConcurrent = Mathf.Max(1, max);
        }

        public static void Enqueue(DownloadRequest request)
        {
            if (request == null)
                return;
            request.Status = EDownloadStatus.Waiting;
            Waiting.Add(request);
        }

        public static void EnqueueRange(IEnumerable<DownloadRequest> requests)
        {
            if (requests == null)
                return;
            foreach (var r in requests)
                Enqueue(r);
        }

        public static void Update()
        {
            UnityEngine.Profiling.Profiler.BeginSample("Resource.FileDownloader");
            try
            {
                PumpWaiting();
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    if (TickActive(Active[i]))
                    {
                        DisposeActive(Active[i]);
                        Active.RemoveAt(i);
                    }
                }
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        public static void Clear()
        {
            for (int i = 0; i < Active.Count; i++)
                DisposeActive(Active[i]);
            Active.Clear();
            Waiting.Clear();
        }

        public static void Cancel(DownloadRequest request)
        {
            if (request == null || request.IsDone)
                return;

            for (int i = Waiting.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(Waiting[i], request))
                    continue;
                Waiting.RemoveAt(i);
                request.Status = EDownloadStatus.Failed;
                request.Error = "Canceled";
                request.OnCompleted?.Invoke(request);
                return;
            }

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(Active[i].Request, request))
                    continue;
                try
                {
                    Active[i].WebRequest?.Abort();
                }
                catch
                {
                    // ignored
                }

                DisposeActive(Active[i]);
                Active.RemoveAt(i);
                request.Status = EDownloadStatus.Failed;
                request.Error = "Canceled";
                request.OnCompleted?.Invoke(request);
                return;
            }
        }

        private static void PumpWaiting()
        {
            while (Active.Count < _maxConcurrent && Waiting.Count > 0)
            {
                var req = Waiting[0];
                Waiting.RemoveAt(0);
                if (!TryStart(req))
                {
                    req.Status = EDownloadStatus.Failed;
                    req.OnCompleted?.Invoke(req);
                }
            }
        }

        private static bool TryStart(DownloadRequest req)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return TryStartMemory(req);
            return TryStartDisk(req);
        }

        private static bool TryStartMemory(DownloadRequest req)
        {
            try
            {
                if (ZeonAssetPathHelper.IsFileUrl(req.Url))
                {
                    req.Error =
                        "WebGL/不可持久化平台不支持 file:// CDN，请配置 http(s) RemoteUrl。 url=" + req.Url;
                    return false;
                }

                var fileName = Path.GetFileName(req.SavePath);
                if (BundleMemoryCache.TryGet(fileName, out var cached) &&
                    ValidateBytes(cached, req, out _))
                {
                    req.Progress = 1f;
                    req.DownloadedBytes = cached.Length;
                    req.Status = EDownloadStatus.Succeed;
                    req.OnCompleted?.Invoke(req);
                    return true;
                }

                var uwr = UnityWebRequest.Get(req.Url);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.timeout = Mathf.Max(1, (int)(AssetManager.Config?.TimeoutSeconds ?? 60f));
                uwr.SendWebRequest();

                req.Status = EDownloadStatus.Downloading;
                req.DownloadedBytes = 0;
                Active.Add(new ActiveDownload
                {
                    Request = req,
                    WebRequest = uwr,
                    Stream = null,
                    StartOffset = 0,
                    MemoryMode = true,
                });
                return true;
            }
            catch (Exception e)
            {
                req.Error = e.Message;
                return false;
            }
        }

        private static bool TryStartDisk(DownloadRequest req)
        {
            try
            {
                var dir = Path.GetDirectoryName(req.SavePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var tempDir = Path.GetDirectoryName(req.TempPath);
                if (!string.IsNullOrEmpty(tempDir))
                    Directory.CreateDirectory(tempDir);

                if (File.Exists(req.SavePath) && req.ExpectedCRC != 0)
                {
                    var crc = Crc32Utility.ComputeFile(req.SavePath);
                    if (crc == req.ExpectedCRC)
                    {
                        req.Progress = 1f;
                        req.Status = EDownloadStatus.Succeed;
                        req.OnCompleted?.Invoke(req);
                        return true;
                    }

                    File.Delete(req.SavePath);
                }

                long offset = 0;
                if (req.ResumeSupported && File.Exists(req.TempPath))
                    offset = new FileInfo(req.TempPath).Length;

                var stream = new FileStream(
                    req.TempPath,
                    offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

                var uwr = new UnityWebRequest(req.Url, UnityWebRequest.kHttpVerbGET);
                uwr.downloadHandler = new DownloadHandlerFileAppend(stream);
                uwr.disposeDownloadHandlerOnDispose = true;
                uwr.timeout = Mathf.Max(1, (int)(AssetManager.Config?.TimeoutSeconds ?? 60f));

                if (offset > 0)
                    uwr.SetRequestHeader("Range", "bytes=" + offset + "-");

                uwr.SendWebRequest();

                req.Status = EDownloadStatus.Downloading;
                req.DownloadedBytes = offset;
                Active.Add(new ActiveDownload
                {
                    Request = req,
                    WebRequest = uwr,
                    Stream = stream,
                    StartOffset = offset,
                    MemoryMode = false,
                });
                return true;
            }
            catch (Exception e)
            {
                req.Error = e.Message;
                return false;
            }
        }

        private static bool TickActive(ActiveDownload active)
        {
            return active.MemoryMode ? TickMemory(active) : TickDisk(active);
        }

        private static bool TickMemory(ActiveDownload active)
        {
            var req = active.Request;
            var uwr = active.WebRequest;
            if (uwr == null)
            {
                Fail(req, "WebRequest is null");
                return true;
            }

            req.DownloadedBytes = (long)uwr.downloadedBytes;
            if (req.ExpectedSize > 0)
                req.Progress = Mathf.Clamp01((float)req.DownloadedBytes / req.ExpectedSize);
            else
                req.Progress = uwr.downloadProgress;

            if (!uwr.isDone)
                return false;

#if UNITY_2020_2_OR_NEWER
            bool netError = uwr.result != UnityWebRequest.Result.Success;
#else
            bool netError = uwr.isNetworkError || uwr.isHttpError;
#endif
            if (netError)
                return RetryOrFail(req, uwr.error ?? ("HTTP " + uwr.responseCode));

            var bytes = uwr.downloadHandler?.data;
            if (!ValidateBytes(bytes, req, out var error))
            {
                req.Error = error;
                return RetryOrFail(req, error);
            }

            var fileName = Path.GetFileName(req.SavePath);
            BundleMemoryCache.Set(fileName, bytes);

            // 可选尽力写盘（失败不影响成功）
            TryBestEffortWrite(req.SavePath, bytes);

            req.Progress = 1f;
            req.Status = EDownloadStatus.Succeed;
            req.OnCompleted?.Invoke(req);
            return true;
        }

        private static bool TickDisk(ActiveDownload active)
        {
            var req = active.Request;
            var uwr = active.WebRequest;
            if (uwr == null)
            {
                Fail(req, "WebRequest is null");
                return true;
            }

            long total = active.StartOffset;
            if (uwr.downloadHandler is DownloadHandlerFileAppend append)
                total = append.TotalWritten;
            else
                total = active.StartOffset + (long)uwr.downloadedBytes;

            req.DownloadedBytes = total;
            if (req.ExpectedSize > 0)
                req.Progress = Mathf.Clamp01((float)total / req.ExpectedSize);
            else
                req.Progress = uwr.downloadProgress;

            if (!uwr.isDone)
                return false;

#if UNITY_2020_2_OR_NEWER
            bool netError = uwr.result != UnityWebRequest.Result.Success;
#else
            bool netError = uwr.isNetworkError || uwr.isHttpError;
#endif
            long code = uwr.responseCode;

            if (active.StartOffset > 0 && code == 200)
            {
                DisposeActive(active);
                TryDelete(req.TempPath);
                req.ResumeSupported = false;
                return RetryOrFail(req, "Server ignored Range (HTTP 200), restart full download.");
            }

            bool httpOk = !netError || code == 206;
            if (!httpOk)
            {
                if (active.StartOffset > 0 && code == 416)
                {
                    DisposeActive(active);
                    TryDelete(req.TempPath);
                    req.ResumeSupported = false;
                    return RetryOrFail(req, uwr.error ?? ("HTTP " + code));
                }

                return RetryOrFail(req, uwr.error ?? ("HTTP " + code));
            }

            try
            {
                active.Stream?.Flush();
                active.Stream?.Close();
                active.Stream = null;
            }
            catch
            {
                // ignored
            }

            if (req.ExpectedCRC == 0 && req.ExpectedSize <= 0)
                ZeonAssetLog.Warn($"Download without CRC/Size: {req.Url}");

            if (!ValidateAndCommitDisk(req))
                return RetryOrFail(req, req.Error ?? "CRC validate failed");

            req.Progress = 1f;
            req.Status = EDownloadStatus.Succeed;
            req.OnCompleted?.Invoke(req);
            return true;
        }

        private static bool ValidateBytes(byte[] bytes, DownloadRequest req, out string error)
        {
            error = null;
            if (bytes == null || bytes.Length == 0)
            {
                error = "Downloaded bytes empty.";
                return false;
            }

            if (req.ExpectedSize > 0 && bytes.Length != req.ExpectedSize)
            {
                error = $"Size mismatch: got {bytes.Length}, expect {req.ExpectedSize}";
                return false;
            }

            if (req.ExpectedCRC != 0)
            {
                var crc = Crc32Utility.Compute(bytes);
                if (crc != req.ExpectedCRC)
                {
                    error = $"CRC mismatch: got {crc}, expect {req.ExpectedCRC}";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateAndCommitDisk(DownloadRequest req)
        {
            if (!File.Exists(req.TempPath))
            {
                req.Error = "Temp file missing after download.";
                return false;
            }

            var tmpInfo = new FileInfo(req.TempPath);
            if (req.ExpectedSize > 0 && tmpInfo.Length != req.ExpectedSize)
            {
                req.Error = $"Size mismatch: got {tmpInfo.Length}, expect {req.ExpectedSize}";
                TryDelete(req.TempPath);
                return false;
            }

            if (req.ExpectedCRC != 0)
            {
                var crc = Crc32Utility.ComputeFile(req.TempPath);
                if (crc != req.ExpectedCRC)
                {
                    req.Error = $"CRC mismatch: got {crc}, expect {req.ExpectedCRC}";
                    TryDelete(req.TempPath);
                    return false;
                }
            }

            try
            {
                if (File.Exists(req.SavePath))
                    File.Delete(req.SavePath);
                File.Move(req.TempPath, req.SavePath);
                return true;
            }
            catch (Exception e)
            {
                req.Error = e.Message;
                return false;
            }
        }

        private static void TryBestEffortWrite(string savePath, byte[] bytes)
        {
            if (string.IsNullOrEmpty(savePath) || bytes == null)
                return;
            try
            {
                var dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(savePath, bytes);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"Memory download OK, disk cache skipped: {e.Message}");
            }
        }

        private static bool RetryOrFail(DownloadRequest req, string error)
        {
            req.Error = error;
            req.RetryCount++;
            if (req.RetryCount <= req.MaxRetry)
            {
                if (req.AlternateUrls != null && req.AlternateUrls.Length > 1)
                {
                    var next = CdnUrlUtility.PickNextUrl(req.AlternateUrls, req.Url);
                    if (!string.IsNullOrEmpty(next) &&
                        !string.Equals(next, req.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        ZeonAssetLog.Warn(
                            $"CDN failover → {next} (from {req.Url}), retry {req.RetryCount}/{req.MaxRetry}");
                        req.Url = next;
                    }
                    else
                    {
                        ZeonAssetLog.Warn(
                            $"Download retry {req.RetryCount}/{req.MaxRetry}: {req.Url} ({error})");
                    }
                }
                else
                {
                    ZeonAssetLog.Warn(
                        $"Download retry {req.RetryCount}/{req.MaxRetry}: {req.Url} ({error})");
                }

                Waiting.Add(req);
                req.Status = EDownloadStatus.Waiting;
                return true;
            }

            req.Status = EDownloadStatus.Failed;
            req.OnCompleted?.Invoke(req);
            return true;
        }

        private static void Fail(DownloadRequest req, string error)
        {
            req.Error = error;
            req.Status = EDownloadStatus.Failed;
            req.OnCompleted?.Invoke(req);
        }

        private static void DisposeActive(ActiveDownload active)
        {
            if (active == null)
                return;
            try
            {
                active.WebRequest?.Dispose();
            }
            catch
            {
                // ignored
            }

            try
            {
                active.Stream?.Dispose();
            }
            catch
            {
                // ignored
            }

            active.WebRequest = null;
            active.Stream = null;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }

    internal sealed class DownloadHandlerFileAppend : DownloadHandlerScript
    {
        private readonly FileStream _stream;
        public long TotalWritten { get; private set; }

        public DownloadHandlerFileAppend(FileStream stream) : base(new byte[64 * 1024])
        {
            _stream = stream;
            TotalWritten = stream.CanSeek ? stream.Length : 0;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
                return false;
            _stream.Write(data, 0, dataLength);
            TotalWritten += dataLength;
            return true;
        }
    }
}
