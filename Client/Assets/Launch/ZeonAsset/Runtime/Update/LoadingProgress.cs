using System;
using UnityEngine;

namespace Game.ZeonAsset
{
    public enum ELoadingPhase
    {
        Idle,
        Checking,
        Downloading,
        LoadingCode,
        WarmingShaders,
        Succeeded,
        Failed,
    }

    public enum ELoadingFailKind
    {
        None,
        ForceAppUpdate,
        Maintenance,
        Error,
    }

    /// <summary>加载 UI 快照。由 <see cref="ILoadingProgress"/> 持有；ZeonAsset 不引用具体 UI。</summary>
    public sealed class LoadingSnapshot
    {
        public bool IsActive;
        /// <summary>true = 热更会话（可做「检查更新」演出）；false = 进战场等业务门。</summary>
        public bool IsHotUpdateSession;
        public ELoadingPhase Phase;
        public float Progress;
        public string Message;
        public long CurrentBytes;
        public long TotalBytes;
        public int FinishedCount;
        public int TotalCount;
        public ELoadingFailKind PromptKind;
        public string PromptTitle;
        public string PromptMessage;
        public string StoreUrl;
        public string Error;

        public LoadingSnapshot Clone() => (LoadingSnapshot)MemberwiseClone();
    }

    /// <summary>加载进度端口。由 Launch <c>AppLoading</c> 实现并注册到 <see cref="LoadingProgress.Port"/>。</summary>
    public interface ILoadingProgress
    {
        LoadingSnapshot Current { get; }

        event Action Changed;
        event Action RetryRequested;
        event Action RepairRequested;

        void Begin(string message, bool isHotUpdateSession);
        void SetProgress(ELoadingPhase phase, float progress, string message);
        void SetDownload(long currentBytes, long totalBytes, int finishedCount, int totalCount);
        void Succeed(string message = null);
        void Fail(ELoadingFailKind kind, string title, string message, string storeUrl, string error);
        void FailIfIdle(string error);
        void Reset();
        void RequestRetry();
        void RequestRepair();
    }

    /// <summary>
    /// ZeonAsset / 资源流程写入加载进度的唯一入口。
    /// 未注册 <see cref="Port"/> 时全部为空操作（无 UI 也能跑完热更）。
    /// </summary>
    public static class LoadingProgress
    {
        private static readonly LoadingSnapshot Empty = new LoadingSnapshot();
        private static ILoadingProgress _port;

        public static ILoadingProgress Port
        {
            get => _port;
            set
            {
                if (_port != null)
                {
                    _port.Changed -= ForwardChanged;
                    _port.RetryRequested -= ForwardRetry;
                    _port.RepairRequested -= ForwardRepair;
                }

                _port = value;
                if (_port != null)
                {
                    _port.Changed += ForwardChanged;
                    _port.RetryRequested += ForwardRetry;
                    _port.RepairRequested += ForwardRepair;
                }
            }
        }

        public static LoadingSnapshot Current => _port?.Current ?? Empty;

        public static event Action Changed;
        public static event Action RetryRequested;
        public static event Action RepairRequested;

        public static void Begin(string message = "正在检查更新", bool isHotUpdateSession = true) =>
            _port?.Begin(message, isHotUpdateSession);

        public static void SetProgress(ELoadingPhase phase, float progress, string message) =>
            _port?.SetProgress(phase, progress, message);

        public static void SetDownload(long currentBytes, long totalBytes, int finishedCount, int totalCount) =>
            _port?.SetDownload(currentBytes, totalBytes, finishedCount, totalCount);

        public static void Succeed(string message = null) => _port?.Succeed(message);

        public static void Fail(
            ELoadingFailKind kind,
            string title,
            string message,
            string storeUrl,
            string error) =>
            _port?.Fail(kind, title, message, storeUrl, error);

        public static void FailIfIdle(string error) => _port?.FailIfIdle(error);

        public static void Reset() => _port?.Reset();

        public static void RequestRetry() => _port?.RequestRetry();

        public static void RequestRepair() => _port?.RequestRepair();

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
                return "0 B";
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024f).ToString("0.#") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.##") + " MB";
        }

        public static string FormatApproxSize(long bytes)
        {
            if (bytes <= 0)
                return "约 0 B";
            if (bytes < 1024 * 1024)
                return "约 " + (bytes / 1024f).ToString("0.#") + " KB";
            return "约 " + (bytes / (1024f * 1024f)).ToString("0.#") + " MB";
        }

        public static string FormatEta(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                return "预计计算中";
            if (seconds < 1f)
                return "预计约 1s";
            if (seconds < 60f)
                return "预计约 " + Mathf.CeilToInt(seconds) + "s";
            if (seconds < 3600f)
                return "预计约 " + Mathf.CeilToInt(seconds / 60f) + "m";
            return "预计约 " + Mathf.CeilToInt(seconds / 3600f) + "h";
        }

        private static void ForwardChanged() => Changed?.Invoke();
        private static void ForwardRetry() => RetryRequested?.Invoke();
        private static void ForwardRepair() => RepairRequested?.Invoke();
    }
}
