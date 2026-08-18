using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.ZeonAsset
{
    /// <summary>
    /// GM：日志 + 资源调试 + 性能。左上「资源」打开；编辑器 F12。
    /// 界面是 Prefab（ZeonAssetDebuggerPanel），这里只填内容。
    /// </summary>
    public sealed class ZeonAssetDebugger : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.F12;

        public enum EMainTab
        {
            Log,
            Assets,
            Perf,
        }

        private enum ETab
        {
            Inspect,
            Resident,
            Boot,
        }

        private enum ELoadKind
        {
            Asset,
            RawFile,
            SceneAdditive,
        }

        private static ZeonAssetDebugger _instance;

        private ZeonAssetDebuggerView _view;
        private bool _visible;
        private bool _bound;
        private bool _syncingScroll;
        private EMainTab _mainTab;
        private ETab _tab;
        private ELoadKind _loadKind;
        private string _inputPath = string.Empty;
        private string _filter = string.Empty;
        private string _status = string.Empty;
        private string _error = string.Empty;
        private ZeonAssetDebugInfo _info;
        private string _lastLogText;
        private bool _followLog = true;
        private float _repairArmedAt = -10f;
        private bool _uploadBusy;
        private string _uploadStatus = string.Empty;
        private bool _busy;
        private float _nextRefresh = -1f;
        private ELoadingPhase _lastAutoOpenPhase = ELoadingPhase.Idle;

        private readonly Dictionary<string, AssetHandle> _assetLoads = new Dictionary<string, AssetHandle>();
        private readonly Dictionary<string, RawFileHandle> _rawLoads = new Dictionary<string, RawFileHandle>();
        private readonly Dictionary<string, SceneHandle> _sceneLoads = new Dictionary<string, SceneHandle>();
        private readonly Dictionary<string, GameObject> _previews = new Dictionary<string, GameObject>();
        private Transform _previewRoot;

        public static void Toggle()
        {
            EnsureInstance();
            if (_instance != null)
                _instance._visible = !_instance._visible;
        }

        public static void Show(EMainTab tab = EMainTab.Log)
        {
            EnsureInstance();
            if (_instance == null)
                return;
            _instance._visible = true;
            _instance._mainTab = tab;
            if (tab == EMainTab.Log)
                _instance._followLog = true;
            _instance.RefreshUi();
        }

        public static void EnsureInstance()
        {
            if (_instance != null)
                return;

            _instance = FindObjectOfType<ZeonAssetDebugger>();
            if (_instance == null)
                Debug.LogError("[ZeonAsset] 资源 GM 未创建。请从 Launch 场景启动（AppUiRoot 会动态挂上 ZeonAssetDebuggerPanel）。");
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
            ZeonAssetScreenLog.Ensure();
            _view = GetComponent<ZeonAssetDebuggerView>();
            BindView();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            UnloadOwned();
        }

        private void Update()
        {
            DevicePerf.Tick();
            ZeonAssetScreenLog.Pump();
            ZeonAssetScreenLog.SuppressUnityConsole();
            MaybeAutoOpenOnFail();
            if (Input.GetKeyDown(ToggleKey))
                _visible = !_visible;

            if (_view == null)
                _view = GetComponent<ZeonAssetDebuggerView>();
            BindView();
            if (_view == null)
                return;

            ZeonAssetDebuggerView.SetActive(_view.WindowRoot, _visible);
            ZeonAssetDebuggerView.SetActive(_view.HudRoot, DevicePerf.HudEnabled);
            if (DevicePerf.HudEnabled)
                RefreshHud();
            if (_visible && Time.unscaledTime >= _nextRefresh)
            {
                RefreshUi();
                _nextRefresh = Time.unscaledTime + 0.25f;
            }
        }

        private void MaybeAutoOpenOnFail()
        {
            var snap = LoadingProgress.Current;
            var phase = snap != null ? snap.Phase : ELoadingPhase.Idle;
            if (phase == ELoadingPhase.Failed && _lastAutoOpenPhase != ELoadingPhase.Failed)
            {
                _visible = true;
                _mainTab = EMainTab.Log;
                _followLog = true;
                RefreshUi();
            }

            _lastAutoOpenPhase = phase;
        }

        private void BindView()
        {
            if (_bound)
                return;
            if (_view == null)
                _view = GetComponent<ZeonAssetDebuggerView>();
            if (_view == null || _view.CloseButton == null)
                return;
            _bound = true;

            Bind(_view.CloseButton, () => _visible = false);
            Bind(_view.TabLog, () => SelectMain(EMainTab.Log));
            Bind(_view.TabAssets, () => SelectMain(EMainTab.Assets));
            Bind(_view.TabPerf, () => SelectMain(EMainTab.Perf));
            Bind(_view.RepairButton, OnRepair);
            Bind(_view.FollowButton, () =>
            {
                _followLog = !_followLog;
                if (_followLog)
                    ScrollLogToEnd();
                RefreshUi();
            });
            Bind(_view.ScrollEndButton, () =>
            {
                _followLog = true;
                ScrollLogToEnd();
                RefreshUi();
            });
            Bind(_view.ClearLogButton, () =>
            {
                ZeonAssetScreenLog.Clear();
                _lastLogText = null;
                RefreshUi();
            });
            Bind(_view.UploadButton, StartUploadLog);
            Bind(_view.SubInspect, () => SelectSub(ETab.Inspect));
            Bind(_view.SubResident, () => SelectSub(ETab.Resident));
            Bind(_view.SubBoot, () => SelectSub(ETab.Boot));
            Bind(_view.KindAsset, () => SelectKind(ELoadKind.Asset));
            Bind(_view.KindRaw, () => SelectKind(ELoadKind.RawFile));
            Bind(_view.KindScene, () => SelectKind(ELoadKind.SceneAdditive));
            Bind(_view.QueryButton, DoQuery);
            Bind(_view.LoadButton, () =>
            {
                if (!_busy)
                    StartCoroutine(LoadRoutine());
            });
            Bind(_view.UnloadButton, () => UnloadOwned(_inputPath));
            Bind(_view.DumpLeakButton, () =>
            {
                if (!AssetManager.IsInitialized)
                    return;
                Debug.Log(AssetManager.DumpRefs(false));
                _status = "已输出到 Console";
                _error = null;
                RefreshUi();
            });
            Bind(_view.UnloadUnusedButton, () =>
            {
                if (!AssetManager.IsInitialized)
                    return;
                AssetManager.ForceUnloadUnusedBundles();
                _status = "已触发闲置 Bundle 卸载";
                _error = null;
                DoQuery();
            });
            Bind(_view.CopyBootButton, CopyBootReport);
            Bind(_view.HudToggle, () =>
            {
                DevicePerf.HudEnabled = !DevicePerf.HudEnabled;
                RefreshUi();
            });
            Bind(_view.Fps30, () => DevicePerf.SetTargetFps(30));
            Bind(_view.Fps60, () => DevicePerf.SetTargetFps(60));
            Bind(_view.RestorePower, DevicePerf.ApplyMobileDefaults);
            Bind(_view.DumpPerf, () => Debug.Log(DevicePerf.BuildDump()));

            if (_view.PathInput != null)
                _view.PathInput.onValueChanged.AddListener(v => _inputPath = v ?? string.Empty);
            if (_view.FilterInput != null)
                _view.FilterInput.onValueChanged.AddListener(v =>
                {
                    _filter = v ?? string.Empty;
                    RefreshUi();
                });
            if (_view.LogScroll != null)
            {
                _view.LogScroll.onValueChanged.AddListener(pos =>
                {
                    if (_syncingScroll || !_followLog)
                        return;
                    // 稍微上滑就停跟随，方便翻看历史。
                    if (pos.y > 0.02f)
                        _followLog = false;
                });
            }
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void SelectMain(EMainTab tab)
        {
            _mainTab = tab;
            if (tab == EMainTab.Log)
                _followLog = true;
            RefreshUi();
        }

        private void SelectSub(ETab tab)
        {
            _tab = tab;
            RefreshUi();
        }

        private void SelectKind(ELoadKind kind)
        {
            _loadKind = kind;
            RefreshUi();
        }

        private void RefreshHud()
        {
            if (_view == null || _view.HudText == null)
                return;
            _view.HudText.text =
                $"{DevicePerf.FpsAvg:F0} fps  {DevicePerf.FrameMs:F0}ms  {DevicePerf.MonoBytes / (1024f * 1024f):F0}MB";
        }

        private void RefreshUi()
        {
            if (_view == null)
                return;

            _nextRefresh = Time.unscaledTime + 0.25f;
            ZeonAssetDebuggerView.SetActive(_view.WindowRoot, _visible);
            ZeonAssetDebuggerView.Paint(_view.TabLog, _mainTab == EMainTab.Log);
            ZeonAssetDebuggerView.Paint(_view.TabAssets, _mainTab == EMainTab.Assets);
            ZeonAssetDebuggerView.Paint(_view.TabPerf, _mainTab == EMainTab.Perf);
            ZeonAssetDebuggerView.SetActive(_view.LogPage, _mainTab == EMainTab.Log);
            ZeonAssetDebuggerView.SetActive(_view.AssetsPage, _mainTab == EMainTab.Assets);
            ZeonAssetDebuggerView.SetActive(_view.PerfPage, _mainTab == EMainTab.Perf);

            if (_view.FooterText != null)
            {
                if (!string.IsNullOrEmpty(_error))
                    _view.FooterText.text = _error;
                else if (!string.IsNullOrEmpty(_status))
                    _view.FooterText.text = _status;
                else
                    _view.FooterText.text = "拖标题栏移动 · 左上「资源」/ F12";
                ZeonAssetDebuggerView.FitPreferredHeight(_view.FooterText, 40f);
            }

            if (_mainTab == EMainTab.Log)
                RefreshLog();
            else if (_mainTab == EMainTab.Perf)
                RefreshPerf();
            else
                RefreshAssets();

            ZeonAssetDebuggerView.FitAllScrolls(_view.WindowRoot);
            if (_mainTab == EMainTab.Log && _followLog)
                ScrollLogToEnd();
        }

        private void RefreshLog()
        {
            ZeonAssetScreenLog.Pump();
            ZeonAssetDebuggerView.SetLabel(_view.RepairButton,
                Time.unscaledTime - _repairArmedAt < 2.5f ? "再点确认清沙盒" : "修复(清沙盒)");
            ZeonAssetDebuggerView.SetLabel(_view.FollowButton, _followLog ? "跟随·开" : "跟随·关");
            ZeonAssetDebuggerView.SetLabel(_view.UploadButton, _uploadBusy ? "上报中…" : "上报日志");
            if (_view.UploadButton != null)
                _view.UploadButton.interactable = !_uploadBusy;

            var meta = "文件: " + ZeonAssetScreenLog.FilePath;
            if (!string.IsNullOrEmpty(_uploadStatus))
                meta = _uploadStatus + "\n" + meta;
            SetText(_view.LogMetaText, meta);
            ZeonAssetDebuggerView.FitPreferredHeight(_view.LogMetaText, 48f);

            var text = ZeonAssetScreenLog.Text;
            if (text == _lastLogText)
                return;
            _lastLogText = text;

            var lines = ZeonAssetScreenLog.GetLines();
            if (lines == null || lines.Count == 0)
            {
                SetText(_view.LogBodyText, "还没有日志。失败时看红色 [E] 行，或切到「资源调试 → 启动」。");
                return;
            }

            var sb = new StringBuilder(text.Length + 64);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                if (IsErrorLine(line))
                    sb.Append("<color=#ed6666>").Append(EscapeRich(line)).Append("</color>\n");
                else if (IsWarnLine(line))
                    sb.Append("<color=#f2bd61>").Append(EscapeRich(line)).Append("</color>\n");
                else
                    sb.Append(EscapeRich(line)).Append('\n');
            }

            SetText(_view.LogBodyText, sb.ToString());
        }

        private void ScrollLogToEnd()
        {
            if (_view == null || _view.LogScroll == null)
                return;
            ZeonAssetDebuggerView.FitScrollText(_view.LogBodyText);
            Canvas.ForceUpdateCanvases();
            _syncingScroll = true;
            _view.LogScroll.verticalNormalizedPosition = 0f;
            _syncingScroll = false;
        }

        private void RefreshPerf()
        {
            ZeonAssetDebuggerView.SetLabel(_view.HudToggle, DevicePerf.HudEnabled ? "角标·开" : "角标·关");
            ZeonAssetDebuggerView.SetLabel(_view.DumpPerf, "一键快照");
            var cam = Camera.main;
            var quality = QualitySettings.names != null &&
                          QualitySettings.GetQualityLevel() < QualitySettings.names.Length
                ? QualitySettings.names[QualitySettings.GetQualityLevel()]
                : "-";
            var extra = DevicePerf.ExtraPerfLines?.Invoke();
            SetText(_view.PerfBody,
                "FPS          " + DevicePerf.Fps.ToString("F0") + "  (均 " + DevicePerf.FpsAvg.ToString("F0") + ")\n" +
                "帧时         " + DevicePerf.FrameMs.ToString("F1") + " ms\n" +
                "目标帧率     " + Application.targetFrameRate + "\n" +
                "VSync        " + QualitySettings.vSyncCount + "\n" +
                "MSAA         " + QualitySettings.antiAliasing + "\n" +
                "阴影         " + QualitySettings.shadows + "\n" +
                "相机 HDR     " + (cam != null && cam.allowHDR ? "开" : "关") + "\n" +
                "相机 MSAA    " + (cam != null && cam.allowMSAA ? "开" : "关") + "\n" +
                "Mono         " + (DevicePerf.MonoBytes / (1024f * 1024f)).ToString("F1") + " MB\n" +
                "分辨率       " + Screen.width + "x" + Screen.height + "\n" +
                "品质档       " + quality +
                (string.IsNullOrEmpty(extra) ? string.Empty : "\n" + extra));
            ZeonAssetDebuggerView.FitScrollText(_view.PerfBody);
        }

        private void RefreshAssets()
        {
            ZeonAssetDebuggerView.Paint(_view.SubInspect, _tab == ETab.Inspect);
            ZeonAssetDebuggerView.Paint(_view.SubResident, _tab == ETab.Resident);
            ZeonAssetDebuggerView.Paint(_view.SubBoot, _tab == ETab.Boot);
            ZeonAssetDebuggerView.SetActive(_view.InspectRoot, _tab == ETab.Inspect);
            ZeonAssetDebuggerView.SetActive(_view.ResidentRoot, _tab == ETab.Resident);
            ZeonAssetDebuggerView.SetActive(_view.BootRoot, _tab == ETab.Boot);
            SetText(_view.HeaderStatus, BuildHeaderStatus());
            ZeonAssetDebuggerView.FitPreferredHeight(_view.HeaderStatus, 72f);
            SyncInput(_view.PathInput, _inputPath);
            SyncInput(_view.FilterInput, _filter);
            ZeonAssetDebuggerView.Paint(_view.KindAsset, _loadKind == ELoadKind.Asset);
            ZeonAssetDebuggerView.Paint(_view.KindRaw, _loadKind == ELoadKind.RawFile);
            ZeonAssetDebuggerView.Paint(_view.KindScene, _loadKind == ELoadKind.SceneAdditive);
            ZeonAssetDebuggerView.SetLabel(_view.LoadButton, _busy ? "加载中…" : "加载");
            if (_view.LoadButton != null)
                _view.LoadButton.interactable = AssetManager.IsInitialized && !_busy;
            if (_view.UnloadButton != null)
                _view.UnloadButton.interactable = AssetManager.IsInitialized && !_busy && HasOwned(_inputPath);
            if (_view.DumpLeakButton != null)
                _view.DumpLeakButton.interactable = AssetManager.IsInitialized;
            if (_view.UnloadUnusedButton != null)
                _view.UnloadUnusedButton.interactable = AssetManager.IsInitialized;

            if (_tab == ETab.Inspect)
            {
                SetText(_view.InspectBody, BuildInspectText());
                ZeonAssetDebuggerView.FitScrollText(_view.InspectBody);
            }
            else if (_tab == ETab.Resident)
            {
                SetText(_view.ResidentBody, BuildResidentText());
                ZeonAssetDebuggerView.FitScrollText(_view.ResidentBody);
            }
            else
            {
                SetText(_view.BootBody, BuildBootText());
                ZeonAssetDebuggerView.FitScrollText(_view.BootBody);
            }
        }

        private string BuildHeaderStatus()
        {
            var sb = new StringBuilder(256);
            var ready = AssetManager.IsInitialized;
            sb.AppendLine(ready
                ? "资源系统：已就绪（可以查路径 / 加载）"
                : "资源系统：未就绪（热更失败或尚未跑完 HostBoot）");
            var cfg = AssetManager.Config;
            if (cfg != null)
            {
                sb.AppendLine(cfg.PlayMode == EPlayMode.HostPlay
                    ? "运行模式：HostPlay = 真机热更（VersionCheck + CDN），不是编辑器模拟"
                    : "运行模式：EditorSimulate = 编辑器 AssetDatabase，不走 CDN");
            }

            var snap = LoadingProgress.Current;
            if (snap != null && snap.Phase != ELoadingPhase.Idle)
            {
                sb.Append("热更阶段：").Append(PhaseLabel(snap.Phase));
                if (snap.Phase == ELoadingPhase.Failed)
                {
                    var err = !string.IsNullOrEmpty(snap.Error)
                        ? snap.Error
                        : (!string.IsNullOrEmpty(snap.PromptMessage)
                            ? snap.PromptMessage
                            : (!string.IsNullOrEmpty(snap.Message) ? snap.Message : "见「日志」或「启动」页"));
                    sb.Append(" — ").Append(err);
                }
                else if (!string.IsNullOrEmpty(snap.Message))
                {
                    sb.Append(" — ").Append(snap.Message);
                }

                sb.AppendLine();
            }

            var manifest = AssetManager.ActiveManifest;
            if (manifest != null)
            {
                var name = string.IsNullOrEmpty(manifest.ManifestFileName) ? "(memory)" : manifest.ManifestFileName;
                sb.Append("当前清单：").Append(name).Append("    hash=").Append(manifest.ManifestHash ?? "-");
            }
            else if (!ready)
            {
                sb.Append("当前清单：无（启动未成功时正常）");
            }

            return sb.ToString();
        }

        private string BuildInspectText()
        {
            if (_info == null)
                return "输入路径或 Address，查看清单归属、引用和 Bundle 状态。\n加载只增加调试器自己的引用，不会动业务句柄。";

            var sb = new StringBuilder(512);
            sb.Append("Location    ").AppendLine(Dash(_info.Location));
            if (!string.IsNullOrEmpty(_info.Note))
                sb.AppendLine(_info.Note);
            sb.AppendLine();
            sb.AppendLine("清单");
            if (_info.FoundInManifest)
            {
                sb.Append("Address     ").AppendLine(Dash(_info.Address));
                sb.Append("AssetPath   ").AppendLine(Dash(_info.AssetPath));
                sb.Append("RawFile     ").AppendLine(_info.IsRawFile ? "是" : "否");
                sb.Append("Bundle      ").AppendLine(Dash(_info.BundleName));
                sb.Append("File        ").Append(_info.BundleFileName).Append("  ")
                    .AppendLine(ZeonAssetDebugInfo.FormatBytes(_info.BundleFileSize));
                sb.Append("Hash        ").AppendLine(Dash(_info.BundleHash));
                if (_info.Tags != null && _info.Tags.Length > 0)
                    sb.Append("Tags        ").AppendLine(string.Join(", ", _info.Tags));
                if (_info.DependBundles != null && _info.DependBundles.Length > 0)
                    sb.Append("Deps        ").AppendLine(string.Join(", ", _info.DependBundles));
            }
            else
            {
                sb.AppendLine("未收录。EditorSimulate 会直读磁盘，HostPlay 则路径可能不对。");
            }

            sb.AppendLine();
            sb.AppendLine("运行时");
            if (_info.AssetLoaded)
                sb.Append("Asset       已加载   ref=").Append(_info.AssetRefCount).Append("   ")
                    .AppendLine(_info.AssetTypeName ?? "-");
            else
                sb.AppendLine("Asset       未加载");

            if (_info.BundleCached)
            {
                sb.Append("Bundle      驻留   ref=").Append(_info.BundleRefCount).Append("   ").Append(_info.BundleState);
                if (_info.BundleDelayUnload)
                    sb.Append("   延迟卸载");
                sb.AppendLine();
                sb.Append("Path        ").AppendLine(Dash(_info.BundleFullPath));
            }
            else
            {
                sb.AppendLine("Bundle      不在内存");
            }

            return sb.ToString();
        }

        private string BuildResidentText()
        {
            var sb = new StringBuilder(1024);
            sb.Append("Asset ").Append(AssetLoaderManager.CachedCount)
                .Append("    Bundle ").AppendLine(BundleLoaderManager.CachedCount.ToString());
            sb.AppendLine();
            sb.AppendLine("Asset");
            int shown = 0;
            foreach (var loader in AssetLoaderManager.Enumerate())
            {
                if (loader == null || (!MatchFilter(loader.AssetPath) && !MatchFilter(loader.Address)))
                    continue;
                if (shown++ > 200)
                {
                    sb.AppendLine("… 已截断");
                    break;
                }

                var path = loader.AssetPath ?? loader.Address;
                sb.Append("  [").Append(loader.RefCount).Append("] ")
                    .Append(ShortName(path));
                if (loader.OwnerBundle != null)
                    sb.Append("    ").Append(loader.OwnerBundle.BundleName);
                sb.AppendLine();
            }

            if (shown == 0)
                sb.AppendLine("  没有驻留 Asset。");

            sb.AppendLine();
            sb.AppendLine("Bundle");
            shown = 0;
            foreach (var loader in BundleLoaderManager.Enumerate())
            {
                if (loader == null || (!MatchFilter(loader.BundleName) && !MatchFilter(loader.FileName)))
                    continue;
                if (shown++ > 200)
                {
                    sb.AppendLine("… 已截断");
                    break;
                }

                sb.Append("  [").Append(loader.RefCount).Append("] ").Append(loader.State);
                if (loader.IsInDelayUnload)
                    sb.Append(" delay");
                sb.Append("    ").AppendLine(loader.BundleName);
            }

            if (shown == 0)
                sb.AppendLine("  没有驻留 Bundle。");

            return sb.ToString();
        }

        private static string BuildBootText()
        {
            var report = ZeonAssetBootReport.Last;
            return string.IsNullOrEmpty(report)
                ? "还没有启动报告。先 Play 一次 HostPlay。"
                : report;
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }

        private static void SyncInput(InputField field, string value)
        {
            if (field == null)
                return;
            value = value ?? string.Empty;
            if (field.text != value)
                field.SetTextWithoutNotify(value);
        }

        private static string Dash(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static bool IsErrorLine(string line)
        {
            return line.IndexOf("[E] ", StringComparison.Ordinal) >= 0 ||
                   line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf(" FAIL ", StringComparison.Ordinal) >= 0;
        }

        private static bool IsWarnLine(string line)
        {
            return line.IndexOf("[W] ", StringComparison.Ordinal) >= 0;
        }

        private static string EscapeRich(string line)
        {
            return line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void StartUploadLog()
        {
            if (_uploadBusy)
                return;
            _uploadBusy = true;
            _uploadStatus = "上报中… " + ZeonDeviceLogUploader.ResolveUploadUrl();
            RefreshUi();
            StartCoroutine(ZeonDeviceLogUploader.UploadCoroutine((ok, msg) =>
            {
                _uploadBusy = false;
                _uploadStatus = (ok ? "[OK] " : "[FAIL] ") + msg;
                if (ok)
                    Debug.Log("[Launch] device log uploaded: " + msg);
                else
                    Debug.LogWarning("[Launch] device log upload failed: " + msg);
                RefreshUi();
            }));
        }

        private void OnRepair()
        {
            if (Time.unscaledTime - _repairArmedAt > 2.5f)
            {
                _repairArmedAt = Time.unscaledTime;
                _status = "再点一次确认清沙盒";
                RefreshUi();
                return;
            }

            _repairArmedAt = -10f;
            LoadingProgress.RequestRepair();
            RefreshUi();
        }

        private static string PhaseLabel(ELoadingPhase phase)
        {
            switch (phase)
            {
                case ELoadingPhase.Checking: return "检查更新";
                case ELoadingPhase.Downloading: return "下载资源";
                case ELoadingPhase.LoadingCode: return "装热更代码";
                case ELoadingPhase.WarmingShaders: return "预热 Shader";
                case ELoadingPhase.Succeeded: return "已完成";
                case ELoadingPhase.Failed: return "失败";
                default: return "空闲";
            }
        }

        private static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "-";
            var slash = Mathf.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }

        private void CopyBootReport()
        {
            var text = ZeonAssetBootReport.Last;
            if (string.IsNullOrEmpty(text))
            {
                _status = "还没有启动报告";
                RefreshUi();
                return;
            }

            GUIUtility.systemCopyBuffer = text;
            _status = "已复制启动报告";
            _error = null;
            RefreshUi();
        }

        private bool MatchFilter(string text)
        {
            if (string.IsNullOrEmpty(_filter))
                return true;
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DoQuery()
        {
            _error = null;
            _info = ZeonAssetDebugQuery.Query(_inputPath);
            _status = _info.FoundInManifest || _info.AssetLoaded || _info.BundleCached ? "已命中" : "未命中";
            RefreshUi();
        }

        private IEnumerator LoadRoutine()
        {
            var path = (_inputPath ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path))
            {
                _error = "路径为空";
                RefreshUi();
                yield break;
            }

            if (!AssetManager.IsInitialized)
            {
                _error = "AssetManager 未初始化";
                RefreshUi();
                yield break;
            }

            if (_busy)
                yield break;

            _busy = true;
            _error = null;
            _status = "加载中…";
            RefreshUi();

            switch (_loadKind)
            {
                case ELoadKind.Asset:
                {
                    var op = AssetManager.LoadAssetAsync<UnityEngine.Object>(path);
                    yield return op;
                    if (!op.IsSucceed)
                    {
                        _error = op.Error;
                        _status = "加载失败";
                        break;
                    }

                    UnloadOwnedAsset(path);
                    _assetLoads[path] = op.Result;
                    _status = SpawnPreview(path, op.Result != null ? op.Result.AssetObject : null);
                    break;
                }
                case ELoadKind.RawFile:
                {
                    var op = AssetManager.LoadRawFileAsync(path);
                    yield return op;
                    if (!op.IsSucceed)
                    {
                        _error = op.Error;
                        _status = "加载失败";
                        break;
                    }

                    UnloadOwnedRaw(path);
                    _rawLoads[path] = op.Result;
                    _status = "已加载 Raw  bytes=" + (op.Result.GetData()?.Length ?? 0);
                    break;
                }
                case ELoadKind.SceneAdditive:
                {
                    var op = AssetManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                    yield return op;
                    if (!op.IsSucceed)
                    {
                        _error = op.Error;
                        _status = "加载失败";
                        break;
                    }

                    UnloadOwnedScene(path);
                    _sceneLoads[path] = op.Result;
                    _status = "已加载场景";
                    break;
                }
            }

            _busy = false;
            DoQuery();
        }

        private bool HasOwned(string path)
        {
            path = (path ?? string.Empty).Trim();
            return _assetLoads.ContainsKey(path) || _rawLoads.ContainsKey(path) || _sceneLoads.ContainsKey(path);
        }

        private void UnloadOwned(string path)
        {
            path = (path ?? string.Empty).Trim();
            bool any = UnloadOwnedAsset(path) | UnloadOwnedRaw(path) | UnloadOwnedScene(path);
            _status = any ? "已卸载" : "没有调试器持有的句柄";
            _error = any ? null : "业务引用请走业务 Release，调试器不会强行扣引用";
            DoQuery();
        }

        private void UnloadOwned()
        {
            foreach (var pair in _assetLoads)
                pair.Value?.Release();
            _assetLoads.Clear();
            foreach (var pair in _rawLoads)
                pair.Value?.Release();
            _rawLoads.Clear();
            foreach (var pair in _sceneLoads)
            {
                TryUnloadUnityScene(pair.Value);
                pair.Value?.Release();
            }

            _sceneLoads.Clear();
            DestroyAllPreviews();
        }

        private bool UnloadOwnedAsset(string path)
        {
            if (!_assetLoads.TryGetValue(path, out var handle))
                return false;
            handle?.Release();
            _assetLoads.Remove(path);
            return true;
        }

        private bool UnloadOwnedRaw(string path)
        {
            if (!_rawLoads.TryGetValue(path, out var handle))
                return false;
            handle?.Release();
            _rawLoads.Remove(path);
            return true;
        }

        private bool UnloadOwnedScene(string path)
        {
            if (!_sceneLoads.TryGetValue(path, out var handle))
                return false;
            TryUnloadUnityScene(handle);
            handle?.Release();
            _sceneLoads.Remove(path);
            return true;
        }

        private static void TryUnloadUnityScene(SceneHandle handle)
        {
            var scenePath = handle?.ScenePath;
            if (string.IsNullOrEmpty(scenePath))
                return;
            var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.UnloadSceneAsync(scene);
        }

        private string SpawnPreview(string path, UnityEngine.Object asset)
        {
            DestroyPreview(path);
            if (asset == null)
                return "已加载，但资源为空";

            var prefab = asset as GameObject;
            if (prefab == null)
                return "已加载 " + asset.GetType().Name + "（非 Prefab，未放入场景）";

            var instance = Instantiate(prefab, PreviewRoot());
            instance.name = prefab.name + " (Preview)";
            PlaceInView(instance);
            _previews[path] = instance;
            return "已放到视野中心";
        }

        private Transform PreviewRoot()
        {
            if (_previewRoot == null)
            {
                var go = new GameObject("[ZeonAssetPreview]");
                go.transform.SetParent(transform, false);
                _previewRoot = go.transform;
            }

            return _previewRoot;
        }

        private void PlaceInView(GameObject instance)
        {
            if (instance.GetComponent<RectTransform>() != null ||
                instance.GetComponentInChildren<Canvas>(true) != null)
                PlaceUiCenter(instance);
            else
                PlaceWorldCenter(instance);
        }

        private void PlaceUiCenter(GameObject instance)
        {
            var ownCanvas = instance.GetComponent<Canvas>() ?? instance.GetComponentInChildren<Canvas>(true);
            Canvas canvas;
            if (ownCanvas != null)
            {
                canvas = ownCanvas;
            }
            else
            {
                canvas = EnsurePreviewCanvas();
                instance.transform.SetParent(canvas.transform, false);
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            canvas.enabled = true;

            if (instance.transform.localScale.sqrMagnitude < 0.0001f)
                instance.transform.localScale = Vector3.one;

            var rootRt = instance.GetComponent<RectTransform>();
            if (rootRt != null && canvas.transform == instance.transform)
            {
                foreach (Transform child in instance.transform)
                    CenterRect(child as RectTransform);
            }
            else
            {
                CenterRect(instance.GetComponent<RectTransform>());
            }

            Canvas.ForceUpdateCanvases();
        }

        private Canvas EnsurePreviewCanvas()
        {
            var existing = PreviewRoot().GetComponentInChildren<Canvas>(true);
            if (existing != null)
                return existing;

            var go = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(PreviewRoot(), false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return canvas;
        }

        private static void CenterRect(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            if (rt.localScale.sqrMagnitude < 0.0001f)
                rt.localScale = Vector3.one;
        }

        private static void PlaceWorldCenter(GameObject instance)
        {
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0)
                cam = Camera.allCameras[0];

            instance.transform.rotation = Quaternion.identity;
            var bounds = ComputeWorldBounds(instance);
            var radius = Mathf.Max(0.5f, bounds.extents.magnitude);

            if (cam == null)
            {
                instance.transform.position += Vector3.zero - bounds.center;
                return;
            }

            float dist;
            if (cam.orthographic)
            {
                dist = Mathf.Max(cam.nearClipPlane + radius + 1f, 5f);
            }
            else
            {
                var fov = Mathf.Max(10f, cam.fieldOfView) * 0.5f * Mathf.Deg2Rad;
                dist = radius / Mathf.Tan(fov) * 1.35f;
                dist = Mathf.Clamp(dist, cam.nearClipPlane + radius + 0.5f, 80f);
            }

            var viewCenter = cam.transform.position + cam.transform.forward * dist;
            instance.transform.position += viewCenter - bounds.center;
        }

        private static Bounds ComputeWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private void DestroyPreview(string path)
        {
            if (string.IsNullOrEmpty(path) || !_previews.TryGetValue(path, out var go))
                return;
            _previews.Remove(path);
            if (go != null)
                Destroy(go);
        }

        private void DestroyAllPreviews()
        {
            foreach (var pair in _previews)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);
            }

            _previews.Clear();
        }
    }
}
