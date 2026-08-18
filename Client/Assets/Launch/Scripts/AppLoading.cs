using System;
using UnityEngine;
using Game.ZeonAsset;

namespace Launch
{
    /// <summary>
    /// 应用层加载门面：热更 / 进战场共用同一 Overlay。
    /// Game 只依赖本类型；ZeonAsset 只写 <see cref="LoadingProgress"/>。
    /// </summary>
    public static class AppLoading
    {
        private static readonly Service Impl = new Service();

        /// <summary>Launch 启动时注册，供 ZeonAsset 写入进度。</summary>
        public static void Bind()
        {
            LoadingProgress.Port = Impl;
        }

        public static void BeginEnterBattle(string message = "正在进入战场")
        {
            Impl.Begin(message, isHotUpdateSession: false);
            Impl.SetProgress(ELoadingPhase.LoadingCode, 0f, message);
        }

        public static void BeginPrepareResources(string message = "正在准备资源")
        {
            Impl.Begin(message, isHotUpdateSession: true);
        }

        public static void SetProgress(float progress, string message)
        {
            Impl.SetProgress(ELoadingPhase.LoadingCode, progress, message);
        }

        public static void SetDownloadProgress(
            float progress,
            string message,
            long currentBytes,
            long totalBytes,
            int finishedCount,
            int totalCount)
        {
            Impl.SetDownload(currentBytes, totalBytes, finishedCount, totalCount);
            Impl.SetProgress(ELoadingPhase.Downloading, progress, message);
        }

        public static void Succeed(string message = null) => Impl.Succeed(message);

        public static void Fail(string title, string message) =>
            Impl.Fail(ELoadingFailKind.Error, title, message, null, message);

        public static void Reset() => Impl.Reset();

        public static event Action RetryRequested
        {
            add => Impl.RetryRequested += value;
            remove => Impl.RetryRequested -= value;
        }

        public static event Action RepairRequested
        {
            add => Impl.RepairRequested += value;
            remove => Impl.RepairRequested -= value;
        }

        private sealed class Service : ILoadingProgress
        {
            public LoadingSnapshot Current { get; private set; } = new LoadingSnapshot();

            public event Action Changed;
            public event Action RetryRequested;
            public event Action RepairRequested;

            public void Begin(string message, bool isHotUpdateSession)
            {
                Current = new LoadingSnapshot
                {
                    IsActive = true,
                    IsHotUpdateSession = isHotUpdateSession,
                    Phase = isHotUpdateSession ? ELoadingPhase.Checking : ELoadingPhase.LoadingCode,
                    Message = message ?? string.Empty,
                };
                Raise();
            }

            public void SetProgress(ELoadingPhase phase, float progress, string message)
            {
                if (!Current.IsActive && phase != ELoadingPhase.Failed)
                    Current.IsActive = true;

                Current.Phase = phase;
                Current.Progress = Mathf.Clamp01(progress);
                if (!string.IsNullOrEmpty(message))
                    Current.Message = message;
                Raise();
            }

            public void SetDownload(long currentBytes, long totalBytes, int finishedCount, int totalCount)
            {
                Current.CurrentBytes = Math.Max(0, currentBytes);
                Current.TotalBytes = Math.Max(0, totalBytes);
                Current.FinishedCount = Math.Max(0, finishedCount);
                Current.TotalCount = Math.Max(0, totalCount);
                if (Current.Phase == ELoadingPhase.Checking || Current.Phase == ELoadingPhase.Idle)
                    Current.Phase = ELoadingPhase.Downloading;
                // 黄条跟字节走，避免只 SetDownload、Progress 仍停在检查阶段的高值。
                if (Current.TotalBytes > 0)
                    Current.Progress = Mathf.Clamp01((float)Current.CurrentBytes / Current.TotalBytes);
                else if (Current.TotalCount > 0)
                    Current.Progress = Mathf.Clamp01((float)Current.FinishedCount / Current.TotalCount);
                if (string.IsNullOrEmpty(Current.Message) || Current.Message.IndexOf("检查", StringComparison.Ordinal) >= 0)
                    Current.Message = "正在下载资源";
                Raise();
            }

            public void Succeed(string message = null)
            {
                Current.IsActive = false;
                Current.Phase = ELoadingPhase.Succeeded;
                Current.Progress = 1f;
                if (!string.IsNullOrEmpty(message))
                    Current.Message = message;
                else
                    Current.Message = Current.IsHotUpdateSession ? "更新完成" : "进入完成";
                Current.PromptKind = ELoadingFailKind.None;
                Raise();
            }

            public void Fail(
                ELoadingFailKind kind,
                string title,
                string message,
                string storeUrl,
                string error)
            {
                Current.IsActive = true;
                Current.Phase = ELoadingPhase.Failed;
                Current.PromptKind = kind;
                Current.PromptTitle = title ?? "加载失败";
                Current.PromptMessage = message ?? error ?? "发生未知错误";
                Current.StoreUrl = storeUrl ?? string.Empty;
                Current.Error = error ?? message;
                Raise();
            }

            public void FailIfIdle(string error)
            {
                if (Current.Phase == ELoadingPhase.Failed)
                    return;
                Fail(ELoadingFailKind.Error, "加载失败", error, null, error);
            }

            public void Reset()
            {
                Current = new LoadingSnapshot();
                Raise();
            }

            public void RequestRetry() => RetryRequested?.Invoke();

            public void RequestRepair() => RepairRequested?.Invoke();

            private void Raise() => Changed?.Invoke();
        }
    }
}
