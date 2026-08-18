using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Launch
{
    /// <summary>
    /// 全局 UI 宿主（DontDestroyOnLoad）。Launch 场景挂一份，上面拖壳 Prefab 引用。
    /// 玩法 Prefab 只做面板内容，不要自带 Canvas / EventSystem。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppUiRoot : MonoBehaviour
    {
        public const int MainSortOrder = 0;
        public const int SystemSortOrder = 1000;
        public const int DebugSortOrder = 29990;
        public const int OverlaySortOrder = 30000;

        [SerializeField] GameObject updateOverlayPrefab;
        [SerializeField] GameObject debuggerPanelPrefab;

        public static AppUiRoot Instance { get; private set; }

        public RectTransform MainLayer { get; private set; }
        public RectTransform SystemLayer { get; private set; }
        public RectTransform DebugLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }

        private bool _built;
        private bool _shellSpawned;

        public static AppUiRoot Ensure()
        {
            if (Instance == null)
            {
                var existing = FindObjectOfType<AppUiRoot>();
                if (existing != null)
                {
                    Instance = existing;
                    DontDestroyOnLoad(existing.gameObject);
                }
                else
                {
                    var host = new GameObject("[AppUiRoot]");
                    DontDestroyOnLoad(host);
                    Instance = host.AddComponent<AppUiRoot>();
                }
            }

            Instance.Initialize();
            return Instance;
        }

        /// <summary>清空主层（切场景前调用，避免 Login / HUD 叠在一起）。</summary>
        public static void ClearMain()
        {
            if (Instance == null || Instance.MainLayer == null)
                return;

            for (int i = Instance.MainLayer.childCount - 1; i >= 0; i--)
            {
                var child = Instance.MainLayer.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// 挂到主层（登录、HUD、结算条等可并存）。
        /// 切场景请先 <see cref="ClearMain"/>（见 AppSceneRunner），这里不再互清。
        /// </summary>
        public static void AttachMain(Transform panel)
        {
            if (panel == null)
                return;

            var root = Ensure();
            EnsureEventSystem();
            StripCanvasStack(panel.gameObject);
            if (panel.parent != root.MainLayer)
                panel.SetParent(root.MainLayer, false);
            StretchFull(panel as RectTransform);
        }

        /// <summary>挂到系统层（热更/阻断弹层）。</summary>
        public static void AttachSystem(Transform panel)
        {
            if (panel == null)
                return;
            var root = Ensure();
            StripCanvasStack(panel.gameObject);
            panel.SetParent(root.SystemLayer, false);
            StretchFull(panel as RectTransform);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            if (!_built)
            {
                Build();
                _built = true;
            }

            SpawnShellUi();
        }

        private void Build()
        {
            EnsureEventSystem();
            MainLayer = CreateCanvasLayer("MainCanvas", MainSortOrder);
            SystemLayer = CreateCanvasLayer("SystemCanvas", SystemSortOrder);
            DebugLayer = CreateCanvasLayer("DebugCanvas", DebugSortOrder);
            OverlayLayer = CreateCanvasLayer("OverlayCanvas", OverlaySortOrder);
        }

        private void SpawnShellUi()
        {
            if (_shellSpawned)
                return;
            _shellSpawned = true;

            SpawnShell(debuggerPanelPrefab, "ZeonAssetDebuggerPanel", DebugLayer,
                "资源 GM Prefab（ZeonAssetDebuggerPanel）");
            SpawnShell(updateOverlayPrefab, "AppLoadingOverlay", OverlayLayer,
                "加载 Overlay Prefab（AppLoadingOverlay）");
        }

        private void SpawnShell(GameObject prefab, string name, RectTransform layer, string label)
        {
            if (prefab == null)
            {
                Debug.LogError("[AppUiRoot] 未指定 " + label + "。请在 Launch 场景的 AppUiRoot 上拖好引用。");
                return;
            }

            var go = Instantiate(prefab, layer, false);
            go.name = name;
            StripCanvasStack(go);
            StretchFull(go.transform as RectTransform);
        }

        private RectTransform CreateCanvasLayer(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rt = go.GetComponent<RectTransform>();
            StretchFull(rt);
            return rt;
        }

        private static void EnsureEventSystem()
        {
            var systems = Object.FindObjectsOfType<EventSystem>();
            EventSystem keep = null;
            for (int i = 0; i < systems.Length; i++)
            {
                var es = systems[i];
                if (es == null)
                    continue;
                // 优先保留壳侧 DontDestroyOnLoad 的那份
                if (es.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    keep = es;
                    break;
                }

                if (keep == null)
                    keep = es;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                var es = systems[i];
                if (es != null && es != keep)
                    Object.Destroy(es.gameObject);
            }

            if (keep != null)
                return;

            var go = new GameObject("EventSystem");
            DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private static void StripCanvasStack(GameObject go)
        {
            var raycaster = go.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                DestroyImmediate(raycaster);

            var scaler = go.GetComponent<CanvasScaler>();
            if (scaler != null)
                DestroyImmediate(scaler);

            var canvas = go.GetComponent<Canvas>();
            if (canvas != null)
                DestroyImmediate(canvas);
        }

        private static void StretchFull(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
