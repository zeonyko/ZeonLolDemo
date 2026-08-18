using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.ZeonAsset;
using Launch;

namespace Login
{
    /// <summary>
    /// 场景切换执行器（DontDestroyOnLoad）：
    /// HostPlay 下可先按 Tag 预下载缺包（进度走 LoadingProgress），再 LoadScene。
    /// 多场景复用：传入各自 location + requireTags 即可。
    /// </summary>
    public sealed class AppSceneRunner : MonoBehaviour
    {
        public static AppSceneRunner Instance { get; private set; }

        private SceneHandle _current;
        private bool _busy;
        private bool _retryDownload;

        public bool IsBusy => _busy;

        public static AppSceneRunner EnsureHost()
        {
            if (Instance != null)
                return Instance;

            var host = new GameObject("[AppSceneRunner]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            Instance = host.AddComponent<AppSceneRunner>();
            return Instance;
        }

        /// <param name="requireTags">
        /// HostPlay 下进场景前必须下齐的 Tag（如战斗 "Game"）。
        /// EditorSimulate / null / 空：跳过预下载，直接加载。
        /// </param>
        public void LoadScene(
            string location,
            string[] requireTags,
            Action<GameObject> onEntered = null,
            Action<string> onFail = null)
        {
            if (_busy || string.IsNullOrEmpty(location))
                return;
            StartCoroutine(LoadRoutine(location, requireTags, onEntered, onFail));
        }

        private IEnumerator LoadRoutine(
            string location,
            string[] requireTags,
            Action<GameObject> onEntered,
            Action<string> onFail)
        {
            _busy = true;

            yield return EnsureTagsDownloaded(requireTags);

            // 主层 UI 在 DontDestroyOnLoad 上：必须在切场景前清掉，
            // 否则会在新场景 Awake(AttachMain) 之后再清，把刚挂上的 LoginPanel/HUD 毁掉。
            AppUiRoot.ClearMain();

            var load = AssetManager.LoadSceneAsync(location, LoadSceneMode.Single);
            yield return load;

            if (!load.IsSucceed)
            {
                var error = load.Error ?? "unknown";
                Debug.LogError("[Login] Load scene failed: " + location + "  " + error);
                _busy = false;
                onFail?.Invoke(error);
                yield break;
            }

            var previous = _current;
            _current = load.Result;
            previous?.Release();

            if (onEntered != null)
            {
                var root = new GameObject("[SceneUi]");
                onEntered.Invoke(root);
            }

            Debug.Log("[Login] Entered: " + location);
            _busy = false;
        }

        /// <summary>
        /// HostPlay：按 Tag 下齐缺包并驱动更新 UI。
        /// EditorSimulate：不下载。
        /// </summary>
        private IEnumerator EnsureTagsDownloaded(string[] tags)
        {
            if (!ShouldDownloadTags(tags))
                yield break;

            LoadingProgress.RetryRequested += OnDownloadRetry;

            try
            {
                while (true)
                {
                    _retryDownload = false;
                    LoadingProgress.Begin("正在准备资源");

                    var op = AssetManager.CreateDownloaderByTags(tags);
                    if (op.TotalCount <= 0)
                    {
                        LoadingProgress.Succeed();
                        yield break;
                    }

                    LoadingProgress.SetProgress(
                        ELoadingPhase.Downloading, 0f, "正在下载资源");
                    LoadingProgress.SetDownload(0, op.TotalBytes, 0, op.TotalCount);

                    while (!op.IsDone)
                    {
                        LoadingProgress.SetDownload(
                            op.CurrentBytes, op.TotalBytes, op.FinishedCount, op.TotalCount);
                        LoadingProgress.SetProgress(
                            ELoadingPhase.Downloading, op.Progress, "正在下载资源");
                        yield return null;
                    }

                    if (op.IsSucceed)
                    {
                        LoadingProgress.Succeed("资源就绪");
                        Debug.Log(
                            $"[Login] Tag 预下载完成 tags={string.Join(",", tags)} " +
                            $"bundles={op.TotalCount} size={LoadingProgress.FormatBytes(op.TotalBytes)}");
                        yield break;
                    }

                    var error = string.IsNullOrEmpty(op.Error) ? "下载失败" : op.Error;
                    Debug.LogError("[Login] Tag 预下载失败: " + error);
                    LoadingProgress.Fail(
                        ELoadingFailKind.Error,
                        "下载失败",
                        "资源下载失败，请重试。",
                        null,
                        error);

                    while (!_retryDownload)
                        yield return null;
                }
            }
            finally
            {
                LoadingProgress.RetryRequested -= OnDownloadRetry;
            }
        }

        private void OnDownloadRetry()
        {
            _retryDownload = true;
        }

        private static bool ShouldDownloadTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return false;
            if (!AssetManager.IsInitialized || AssetManager.Config == null)
                return false;
            if (AssetManager.Config.PlayMode == EPlayMode.EditorSimulate)
                return false;
            return true;
        }

        private void OnDestroy()
        {
            LoadingProgress.RetryRequested -= OnDownloadRetry;
            if (Instance == this)
                Instance = null;
            _current?.Release();
        }
    }
}
