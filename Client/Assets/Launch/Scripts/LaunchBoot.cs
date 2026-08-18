using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.ZeonAsset;

namespace Launch
{
    /// <summary>
    /// AOT 启动器：唯一启动场景入口。驱动 ZeonAsset，加载热更 DLL，再反射进入 <see cref="IGameApp"/>。
    /// 不加载任何玩法资源；游戏 UI / 场景由热更程序集通过 ZeonAsset 加载。
    /// </summary>
    public class LaunchBoot : MonoBehaviour
    {
        [Tooltip("仅编辑器：EditorSimulate 直读资源；HostPlay 走热更。PC / Android 安装包忽略此项，固定 HostPlay。")]
        public EPlayMode PlayMode = EPlayMode.EditorSimulate;

        [Tooltip("仅编辑器 HostPlay：测 Full / OnDemand / Auto。EditorSimulate 忽略。PC / 安装包固定 OnDemand。")]
        public EBundleDownloadPolicy DownloadPolicy = EBundleDownloadPolicy.Auto;

        private bool _entered;
        private bool _running;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            DevicePerf.ApplyAtBoot();
            AppLoading.Bind();
            AppUiRoot.Ensure();
        }

        private void OnEnable()
        {
            LoadingProgress.RetryRequested += OnRetry;
            LoadingProgress.RepairRequested += OnRepair;
        }

        private void OnDisable()
        {
            LoadingProgress.RetryRequested -= OnRetry;
            LoadingProgress.RepairRequested -= OnRepair;
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private void OnRetry()
        {
            if (_entered)
                return;
            StopAllCoroutines();
            _running = false;
            AssetManager.Destroy();
            LoadingProgress.Reset();
            StartCoroutine(Run());
        }

        private void OnRepair()
        {
            var sandbox = DiskCacheManager.GetSandboxRoot();
            DiskCacheManager.ClearSandbox(sandbox);
            BootDispatchCache.Clear();
            Debug.Log("[Launch] 已清沙盒: " + sandbox);

            if (!_entered)
            {
                OnRetry();
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator Run()
        {
            if (_running)
                yield break;
            _running = true;

            var playMode = GetRuntimePlayMode();
#if UNITY_EDITOR
            WarnIfEditorHostPlayOnMobileTarget(playMode);
#endif

            // EditorSimulate：不请求调度，不依赖 boot_config。
            // HostPlay / 真机：只走远程 VersionCheck（沙盒下次地址 → boot_config 主/备）。
            if (playMode == EPlayMode.HostPlay)
                yield return BootConfig.EnsureLoadedCoroutine();

            var config = ZeonAssetConfig.CreateDefault();
            config.PlayMode = playMode;
            config.AutoUpdateOnInit = playMode == EPlayMode.HostPlay;
            config.EnableShaderWarmup = playMode != EPlayMode.EditorSimulate;

            if (playMode == EPlayMode.HostPlay)
            {
                config.DownloadPolicy = GetRuntimeDownloadPolicy();
#if UNITY_EDITOR
                // 编辑器 Application.platform 是 WindowsEditor；CDN 目录按 Build Target（Android 等）
                config.Platform = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#endif
                if (!TryApplyRemoteVersionCheck(config, out var error))
                {
                    LoadingProgress.Fail(
                        ELoadingFailKind.Error, "提示",
                        "加载失败，请重试。", null, error);
                    Debug.LogError("[Launch] " + error);
                    _running = false;
                    yield break;
                }

                config.CodeHotUpdateHook = new HybridClrCodeHotUpdateHook();
                Debug.Log(
                    $"[Launch] PlayMode=HostPlay, DownloadPolicy={config.DownloadPolicy}, " +
                    $"platform={config.ResolvePlatform()}, " +
                    $"versionCheck={config.VersionCheckUrl}, urls={FormatUrls(config.VersionCheckUrls)}");
            }
            else
            {
                Debug.Log("[Launch] PlayMode=EditorSimulate（不请求 VersionCheck / 不使用 DownloadPolicy）");
            }

            var init = AssetManager.InitializeAsync(config);
            yield return init;

            if (!init.IsSucceed)
            {
                Debug.LogError("[Launch] Initialize failed: " + init.Error);
                _running = false;
                yield break;
            }

            if (playMode != EPlayMode.EditorSimulate && AssetManager.ActiveManifest == null)
            {
                LoadingProgress.Fail(
                    ELoadingFailKind.Error, "提示",
                    "加载失败，请重试。", null, "No Manifest");
                _running = false;
                yield break;
            }

            if (!GameAppBootstrap.TryStart())
            {
                LoadingProgress.Fail(
                    ELoadingFailKind.Error, "提示",
                    "加载失败，请重试。", null, "GameApp Missing");
                _running = false;
                yield break;
            }

            // 已进入 Login：启动加载会话必须收掉，避免 Overlay 卡在「检查更新」。
            LoadingProgress.Reset();

            _entered = true;
            _running = false;
        }

        /// <summary>编辑器读 Inspector；安装包固定 HostPlay，避免场景调试值进包。</summary>
        private EPlayMode GetRuntimePlayMode()
        {
#if UNITY_EDITOR
            return PlayMode;
#else
            return EPlayMode.HostPlay;
#endif
        }

        /// <summary>仅 HostPlay：编辑器读 Inspector；安装包固定 OnDemand。</summary>
        private EBundleDownloadPolicy GetRuntimeDownloadPolicy()
        {
#if UNITY_EDITOR
            return DownloadPolicy;
#else
            return EBundleDownloadPolicy.OnDemand;
#endif
        }

#if UNITY_EDITOR
        /// <summary>Android/iOS AB 在 Windows DX11 编辑器里 HostPlay，自定义 Shader 会粉紫。</summary>
        private static void WarnIfEditorHostPlayOnMobileTarget(EPlayMode playMode)
        {
            if (playMode != EPlayMode.HostPlay)
                return;

            var target = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            if (target != UnityEditor.BuildTarget.Android && target != UnityEditor.BuildTarget.iOS)
                return;

            Debug.LogWarning(
                "[LaunchBoot] HostPlay + " + target +
                "：正在加载移动平台 AB。Windows 编辑器（DX11）里地图/材质常会粉紫，属平台不匹配，不是资源坏了。" +
                "看画面请用 EditorSimulate，或切 StandaloneWindows64 后重新 Build/Publish；真机效果请装包验证。");
        }
#endif

        /// <summary>HostPlay / 真机：沙盒下次地址 → BootConfig 主/备。无 Mock。</summary>
        private static bool TryApplyRemoteVersionCheck(ZeonAssetConfig config, out string error)
        {
            error = null;
            var urls = ResolveVersionCheckUrls();
            if (urls.Count == 0)
            {
                error =
                    "引导调度地址未配置或仍是占位域名。请检查 StreamingAssets/Launch/boot_config.json 的 version_check_url，" +
                    "并启动 Tools/local_dev_server（或正式 VersionCheck）。";
                return false;
            }

            config.VersionCheckUrl = urls[0];
            config.VersionCheckUrls = urls.ToArray();
            config.RemoteUrl = null;
            return true;
        }

        private static List<string> ResolveVersionCheckUrls()
        {
            var urls = new List<string>();
            AddUrl(urls, BootDispatchCache.TryRead());
            AddUrl(urls, BootConfig.VersionCheckUrl);
            AddUrl(urls, BootConfig.BackupVersionCheckUrl);
            return urls;
        }

        private static void AddUrl(List<string> urls, string url)
        {
            url = (url ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url))
                return;
            if (url.IndexOf("zeon.local", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            for (int i = 0; i < urls.Count; i++)
            {
                if (string.Equals(urls[i], url, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            urls.Add(url);
        }

        private static string FormatUrls(string[] urls)
        {
            if (urls == null || urls.Length == 0)
                return "(none)";
            return string.Join(" | ", urls);
        }
    }
}
