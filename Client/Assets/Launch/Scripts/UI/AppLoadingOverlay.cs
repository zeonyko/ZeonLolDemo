using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Game.ZeonAsset;

namespace Launch
{
    /// <summary>
    /// 应用加载 Overlay（热更 / 进战场共用）。读 <see cref="LoadingProgress"/>。
    /// Prefab 挂在 Launch 场景 <see cref="AppUiRoot"/> 的 updateOverlayPrefab。
    /// </summary>
    public sealed class AppLoadingOverlay : MonoBehaviour
    {
        [SerializeField] private GameObject progressRoot;
        [SerializeField] private Image fillImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text percentText;
        [SerializeField] private GameObject dialogRoot;
        [SerializeField] private Text dialogTitle;
        [SerializeField] private Text dialogBody;
        [SerializeField] private Button storeButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button gmButton;
        [SerializeField] private GameObject dimRoot;

        [Header("检查更新展示")]
        [Tooltip("「正在检查更新」至少停留这么久。热更仍立刻跑完，只挡画面。")]
        [SerializeField] private float checkHoldSeconds = 1.6f;
        [Tooltip("检查/下载结束后，「已是最新」或「更新完成」再停一会儿。")]
        [SerializeField] private float doneHoldSeconds = 0.5f;

        private static AppLoadingOverlay _instance;
        private Text _gmLabel;
        private float _activeSince = -1f;
        private float _doneSince = -1f;
        private bool _sawDownload;
        private ELoadingPhase _trackedPhase = ELoadingPhase.Idle;

        // 下载 ETA：按字节增量估速
        private long _etaSampleBytes;
        private float _etaSampleTime = -1f;
        private long _etaSessionTotal = -1;
        private float _etaSpeedBytesPerSec;
        private float _etaSeconds = -1f;

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
            ZeonAssetDebugger.EnsureInstance();
            ResolveRefs();
            EnsureGmButton();
            BindButtons();
            // Prefab 默认可能亮着 Progress；先强制收起，再跟 LoadingProgress。
            Show(false, false);
            Apply(LoadingProgress.Current);
        }

        private void ResolveRefs()
        {
            if (progressRoot == null)
            {
                var t = transform.Find("Progress");
                if (t != null)
                    progressRoot = t.gameObject;
            }

            if (dimRoot == null)
            {
                var t = transform.Find("Dim");
                if (t != null)
                    dimRoot = t.gameObject;
            }

            if (dialogRoot == null)
            {
                var t = transform.Find("Dialog");
                if (t != null)
                    dialogRoot = t.gameObject;
            }

            if (fillImage == null && progressRoot != null)
            {
                var fill = progressRoot.transform.Find("Bar/Fill");
                if (fill != null)
                    fillImage = fill.GetComponent<Image>();
            }

            if (statusText == null && progressRoot != null)
            {
                var t = progressRoot.transform.Find("Status");
                if (t != null)
                    statusText = t.GetComponent<Text>();
            }

            if (percentText == null && progressRoot != null)
            {
                var t = progressRoot.transform.Find("Percent");
                if (t != null)
                    percentText = t.GetComponent<Text>();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            Apply(LoadingProgress.Current);
        }

        private void BindButtons()
        {
            if (storeButton != null)
            {
                storeButton.onClick.RemoveListener(OnStore);
                storeButton.onClick.AddListener(OnStore);
            }

            if (retryButton != null)
            {
                retryButton.onClick.RemoveListener(OnRetry);
                retryButton.onClick.AddListener(OnRetry);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuit);
                quitButton.onClick.AddListener(OnQuit);
            }

            if (gmButton != null)
            {
                gmButton.onClick.RemoveListener(OnToggleGm);
                gmButton.onClick.AddListener(OnToggleGm);
            }
        }

        private void Apply(LoadingSnapshot snap)
        {
            if (snap == null)
            {
                ResetDownloadEta();
                ResetHold();
                Show(false, false);
                return;
            }

            TrackSession(snap);

            if (snap.Phase == ELoadingPhase.Failed)
            {
                ResetDownloadEta();
                ResetHold();
                Show(false, true);
                SetText(dialogTitle, DialogTitle(snap));
                SetText(dialogBody, DialogBody(snap));
                SetActive(storeButton, snap.PromptKind == ELoadingFailKind.ForceAppUpdate);
                SetActive(retryButton, snap.PromptKind == ELoadingFailKind.Error);
                return;
            }

            if (IsBusy(snap) || IsHoldingDone(snap))
            {
                DrawProgress(snap);
                return;
            }

            ResetHold();
            Show(false, false);
        }

        private static bool IsBusy(LoadingSnapshot snap)
        {
            return snap.IsActive &&
                   snap.Phase != ELoadingPhase.Idle &&
                   snap.Phase != ELoadingPhase.Succeeded &&
                   snap.Phase != ELoadingPhase.Failed;
        }

        private bool IsHoldingDone(LoadingSnapshot snap)
        {
            if (snap.Phase != ELoadingPhase.Succeeded || _activeSince < 0f)
                return false;

            var doneHold = Mathf.Max(0f, doneHoldSeconds);
            if (!snap.IsHotUpdateSession)
                return Time.unscaledTime < (_doneSince < 0f ? Time.unscaledTime : _doneSince) + doneHold;

            var checkHold = Mathf.Max(0f, checkHoldSeconds);
            var doneAt = _doneSince < 0f ? Time.unscaledTime : _doneSince;
            var doneStart = _sawDownload
                ? doneAt
                : Mathf.Max(doneAt, _activeSince + checkHold);
            return Time.unscaledTime < doneStart + doneHold;
        }

        private void TrackSession(LoadingSnapshot snap)
        {
            if (snap.Phase == ELoadingPhase.Downloading && snap.TotalBytes > 0)
                _sawDownload = true;

            if (IsBusy(snap) && (_trackedPhase == ELoadingPhase.Idle ||
                                 _trackedPhase == ELoadingPhase.Succeeded ||
                                 _activeSince < 0f))
            {
                _activeSince = Time.unscaledTime;
                _doneSince = -1f;
                _sawDownload = snap.Phase == ELoadingPhase.Downloading && snap.TotalBytes > 0;
            }

            if (snap.Phase == ELoadingPhase.Succeeded && _doneSince < 0f)
                _doneSince = Time.unscaledTime;

            _trackedPhase = snap.Phase;
        }

        private void DrawProgress(LoadingSnapshot snap)
        {
            var hotUpdate = snap.IsHotUpdateSession;
            var checkHold = Mathf.Max(0.01f, checkHoldSeconds);
            var elapsed = _activeSince < 0f ? 0f : Time.unscaledTime - _activeSince;
            // 仅热更会话：无下载时用「检查更新」演出垫时间；业务门直接跟 Message。
            var stillCheckingHold = hotUpdate &&
                                    snap.Phase == ELoadingPhase.Succeeded &&
                                    !_sawDownload &&
                                    elapsed < checkHold;

            string status;
            float fill;
            string percent;
            if (stillCheckingHold)
            {
                fill = Mathf.Lerp(0.08f, 0.92f, EaseOut(elapsed / checkHold));
                status = "正在检查更新";
                percent = fill.ToString("P0");
            }
            else if (snap.Phase == ELoadingPhase.Succeeded)
            {
                ResetDownloadEta();
                fill = 1f;
                if (!hotUpdate)
                    status = string.IsNullOrEmpty(snap.Message) ? "进入完成" : snap.Message;
                else
                    status = _sawDownload ? "更新完成" : "已是最新";
                percent = "100%";
            }
            else if (snap.Phase == ELoadingPhase.Downloading)
            {
                TickDownloadEta(snap);
                // 优先用字节比例，避免 Progress 残留导致黄条一直满。
                if (snap.TotalBytes > 0)
                    fill = Mathf.Clamp01((float)snap.CurrentBytes / snap.TotalBytes);
                else if (snap.TotalCount > 0)
                    fill = Mathf.Clamp01((float)snap.FinishedCount / snap.TotalCount);
                else
                    fill = Mathf.Clamp01(snap.Progress);
                status = StatusLine(snap);
                percent = ProgressLine(snap);
            }
            else if (!hotUpdate)
            {
                ResetDownloadEta();
                fill = Mathf.Clamp01(snap.Progress);
                status = StatusLine(snap);
                percent = fill.ToString("P0");
            }
            else
            {
                ResetDownloadEta();
                fill = Mathf.Max(snap.Progress, Mathf.Lerp(0.08f, 0.75f, EaseOut(elapsed / checkHold)));
                status = StatusLine(snap);
                percent = fill.ToString("P0");
            }

            Show(true, false);
            SetText(statusText, status);
            SetFill(fill);
            SetText(percentText, percent);
        }

        private void SetFill(float amount)
        {
            if (fillImage != null)
                fillImage.fillAmount = Mathf.Clamp01(amount);
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        private void ResetHold()
        {
            _activeSince = -1f;
            _doneSince = -1f;
        }

        private void ResetDownloadEta()
        {
            _etaSampleBytes = 0;
            _etaSampleTime = -1f;
            _etaSessionTotal = -1;
            _etaSpeedBytesPerSec = 0f;
            _etaSeconds = -1f;
        }

        private void TickDownloadEta(LoadingSnapshot snap)
        {
            if (snap.TotalBytes != _etaSessionTotal)
            {
                ResetDownloadEta();
                _etaSessionTotal = snap.TotalBytes;
                _etaSampleBytes = snap.CurrentBytes;
                _etaSampleTime = Time.unscaledTime;
                return;
            }

            var now = Time.unscaledTime;
            if (_etaSampleTime < 0f)
            {
                _etaSampleBytes = snap.CurrentBytes;
                _etaSampleTime = now;
                return;
            }

            var dt = now - _etaSampleTime;
            if (dt < 0.35f)
                return;

            var delta = snap.CurrentBytes - _etaSampleBytes;
            _etaSampleBytes = snap.CurrentBytes;
            _etaSampleTime = now;

            if (delta <= 0 || dt <= 0f)
                return;

            var instant = delta / dt;
            _etaSpeedBytesPerSec = _etaSpeedBytesPerSec <= 1f
                ? instant
                : Mathf.Lerp(_etaSpeedBytesPerSec, instant, 0.35f);

            var remain = snap.TotalBytes - snap.CurrentBytes;
            if (remain <= 0)
            {
                _etaSeconds = 0f;
                return;
            }

            if (_etaSpeedBytesPerSec > 1f)
                _etaSeconds = remain / _etaSpeedBytesPerSec;
        }

        private static string StatusLine(LoadingSnapshot snap)
        {
            string baseMsg = !string.IsNullOrEmpty(snap.Message)
                ? snap.Message
                : DefaultStatus(snap.Phase);

            if (snap.Phase == ELoadingPhase.Downloading && snap.TotalBytes > 0)
                return baseMsg + " · " + LoadingProgress.FormatApproxSize(snap.TotalBytes);

            return baseMsg;
        }

        private static string DefaultStatus(ELoadingPhase phase)
        {
            switch (phase)
            {
                case ELoadingPhase.Checking:
                    return "正在检查更新";
                case ELoadingPhase.Downloading:
                    return "正在下载资源";
                case ELoadingPhase.LoadingCode:
                    return "正在加载代码";
                case ELoadingPhase.WarmingShaders:
                    return "正在预热 Shader";
                default:
                    return "正在初始化";
            }
        }

        private string ProgressLine(LoadingSnapshot snap)
        {
            if (snap.Phase != ELoadingPhase.Downloading || snap.TotalBytes <= 0)
                return $"{snap.Progress:P0}";

            float p = Mathf.Clamp01((float)snap.CurrentBytes / snap.TotalBytes);
            var bytes =
                $"{LoadingProgress.FormatBytes(snap.CurrentBytes)}/{LoadingProgress.FormatBytes(snap.TotalBytes)}";
            var eta = _etaSeconds >= 0f
                ? LoadingProgress.FormatEta(_etaSeconds)
                : "预计计算中";
            return $"{bytes}  {p:P0}  {eta}";
        }

        private static string DialogTitle(LoadingSnapshot snap)
        {
            switch (snap.PromptKind)
            {
                case ELoadingFailKind.ForceAppUpdate:
                    return string.IsNullOrEmpty(snap.PromptTitle) ? "force_update" : snap.PromptTitle;
                case ELoadingFailKind.Maintenance:
                    return string.IsNullOrEmpty(snap.PromptTitle) ? "maintenance" : snap.PromptTitle;
                default:
                    return "加载失败";
            }
        }

        private static string DialogBody(LoadingSnapshot snap)
        {
            if (snap.PromptKind == ELoadingFailKind.Error)
            {
                if (!string.IsNullOrEmpty(snap.Error))
                    return snap.Error;
                if (!string.IsNullOrEmpty(snap.PromptMessage))
                    return snap.PromptMessage;
                return "加载失败，请重试。";
            }

            if (!string.IsNullOrEmpty(snap.PromptMessage))
                return snap.PromptMessage;

            switch (snap.PromptKind)
            {
                case ELoadingFailKind.ForceAppUpdate:
                    return "force_update=true，需要安装新包。";
                case ELoadingFailKind.Maintenance:
                    return "status=maintenance，服务端维护中。";
                default:
                    return "加载失败，请重试。";
            }
        }

        private void Show(bool progress, bool dialog)
        {
            EnsureEventSystem();

            var canvas = GetComponent<Canvas>();
            if (canvas != null)
                canvas.enabled = true;

            SetActive(progressRoot, progress);
            SetActive(dialogRoot, dialog);
            SetActive(EnsureDim(), progress || dialog);
            SetActive(gmButton, true);
            if (gmButton != null)
                gmButton.transform.SetAsLastSibling();
        }

        private GameObject EnsureDim()
        {
            if (dimRoot != null)
                return dimRoot;
            var t = transform.Find("Dim");
            if (t != null)
                dimRoot = t.gameObject;
            return dimRoot;
        }

        private void EnsureGmButton()
        {
            if (gmButton != null)
            {
                _gmLabel = gmButton.GetComponentInChildren<Text>();
                return;
            }

            // Prefab 丢引用时的兜底；尺寸以 AppLoadingOverlay 里的 GmButton 为准。
            var go = new GameObject("GmButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(140f, 56f);
            rt.anchoredPosition = new Vector2(12f, -12f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.13f, 0.15f, 0.92f);
            image.raycastTarget = true;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            _gmLabel = labelGo.GetComponent<Text>();
            _gmLabel.alignment = TextAnchor.MiddleCenter;
            _gmLabel.fontSize = 24;
            _gmLabel.fontStyle = FontStyle.Bold;
            _gmLabel.color = new Color(0.92f, 0.94f, 0.96f, 1f);
            _gmLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _gmLabel.raycastTarget = false;
            _gmLabel.text = "资源";
            gmButton = go.GetComponent<Button>();
        }

        private static void OnToggleGm()
        {
            ZeonAssetDebugger.Toggle();
        }

        private void OnStore()
        {
            var url = LoadingProgress.Current?.StoreUrl;
            if (!string.IsNullOrEmpty(url))
                Application.OpenURL(url);
        }

        private void OnRetry()
        {
            LoadingProgress.RequestRetry();
        }

        private static void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void EnsureEventSystem()
        {
            // EventSystem 由 AppUiRoot 统一创建；此处仅兜底。
            if (FindObjectOfType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem");
            DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
                text.text = value;
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
                component.gameObject.SetActive(active);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null)
                go.SetActive(active);
        }
    }
}
