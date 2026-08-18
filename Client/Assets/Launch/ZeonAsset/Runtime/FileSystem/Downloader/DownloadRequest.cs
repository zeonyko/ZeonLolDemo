using System;

namespace Game.ZeonAsset
{
    public enum EDownloadStatus
    {
        None = 0,
        Waiting = 1,
        Downloading = 2,
        Succeed = 3,
        Failed = 4,
    }

    /// <summary>
    /// 单个文件下载请求。
    /// </summary>
    public class DownloadRequest
    {
        public string Url;
        /// <summary>多 CDN 候选 URL；失败重试时轮换。</summary>
        public string[] AlternateUrls;
        public string SavePath;
        /// <summary>可选：独立临时路径（CDN 布局下为 Cache/tmp/{hash}.tmp）。</summary>
        public string TempPathOverride;
        public uint ExpectedCRC;
        public long ExpectedSize;
        public int RetryCount;
        public int MaxRetry;

        public EDownloadStatus Status = EDownloadStatus.None;
        public string Error;
        public float Progress;
        public long DownloadedBytes;
        public bool ResumeSupported = true;

        public string TempPath =>
            string.IsNullOrEmpty(TempPathOverride) ? SavePath + ".tmp" : TempPathOverride;

        public bool IsDone => Status == EDownloadStatus.Succeed || Status == EDownloadStatus.Failed;
        public bool IsSucceed => Status == EDownloadStatus.Succeed;

        public Action<DownloadRequest> OnCompleted;

        public static DownloadRequest Create(string url, string savePath, uint crc = 0, long size = 0, int maxRetry = 2)
        {
            return new DownloadRequest
            {
                Url = url,
                SavePath = savePath,
                ExpectedCRC = crc,
                ExpectedSize = size,
                MaxRetry = maxRetry,
                Status = EDownloadStatus.Waiting,
            };
        }

        public static DownloadRequest CreateWithFailover(
            string[] urls,
            string savePath,
            uint crc = 0,
            long size = 0,
            int maxRetry = 2)
        {
            var primary = urls != null && urls.Length > 0 ? urls[0] : null;
            var req = Create(primary, savePath, crc, size, maxRetry);
            req.AlternateUrls = urls;
            return req;
        }
    }
}
