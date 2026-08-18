using System.Collections;
using Client.Network;
using Shared;
using UnityEngine;
using UnityEngine.UI;
using Launch;

namespace Client.Battle
{
    /// <summary>大厅准备条和胜负结算卡。布局在 Prefabs/UI/MatchResultPanel。</summary>
    public sealed class MatchResultPanel : MonoBehaviour
    {
        const string PrefabKey = "Prefabs/UI/MatchResultPanel";

        static readonly Color ReadyIdle = new Color(0.239f, 0.369f, 0.322f, 1f);
        static readonly Color ReadyOn = new Color(0.28f, 0.28f, 0.30f, 1f);
        static readonly Color Victory = new Color(0.91f, 0.79f, 0.45f, 1f);
        static readonly Color Defeat = new Color(0.79f, 0.42f, 0.42f, 1f);

        [SerializeField] GameObject lobbyBar;
        [SerializeField] GameObject resultRoot;
        [SerializeField] Text resultTitle;
        [SerializeField] Text resultDetail;
        [SerializeField] Text resultReadyLabel;
        [SerializeField] Button resultReadyButton;
        [SerializeField] Text resultReadyButtonText;
        [SerializeField] Image resultReadyButtonImage;
        [SerializeField] Text lobbyReadyLabel;
        [SerializeField] Button lobbyReadyButton;
        [SerializeField] Text lobbyReadyButtonText;
        [SerializeField] Image lobbyReadyButtonImage;

        static MatchResultPanel _instance;
        bool _localReady;
        int _readyCount;
        int _totalCount = 1;

        bool IsShowing =>
            (lobbyBar != null && lobbyBar.activeSelf)
            || (resultRoot != null && resultRoot.activeSelf);

        public static void Ensure()
        {
            if (_instance != null) return;
            BattleAssets.Run(EnsureAsync());
        }

        public static IEnumerator EnsureAsync()
        {
            if (_instance != null)
            {
                _instance.SubscribeEvents();
                yield break;
            }

            if (TryInstantiateFromLoaded())
                yield break;

            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(PrefabKey, p => prefab = p);
            if (prefab == null)
            {
                Debug.LogError("[MatchResultPanel] 找不到 " + PrefabKey);
                yield break;
            }

            FinishInstantiate(prefab);
        }

        static bool TryInstantiateFromLoaded()
        {
            _instance = Object.FindObjectOfType<MatchResultPanel>();
            if (_instance != null)
            {
                _instance.SubscribeEvents();
                return true;
            }

            var prefab = BattleAssets.Load<GameObject>(PrefabKey);
            if (prefab == null) return false;
            FinishInstantiate(prefab);
            return true;
        }

        static void FinishInstantiate(GameObject prefab)
        {
            var go = Object.Instantiate(prefab);
            go.name = "MatchResultPanel";
            _instance = go.GetComponent<MatchResultPanel>();
            _instance?.SubscribeEvents();
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            _instance.UnsubscribeEvents();
            _instance = null;
        }

        void Awake()
        {
            _instance = this;
            AppUiRoot.AttachMain(transform);
            BindReadyButton(lobbyReadyButton);
            BindReadyButton(resultReadyButton);
            HideViews();
            SubscribeEvents();
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            if (_instance == this)
                _instance = null;
        }

        void BindReadyButton(Button button)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnReadyClicked);
        }

        void SubscribeEvents()
        {
            UnsubscribeEvents();
            MatchEvents.Result += OnMatchResultEvent;
            MatchEvents.Start += OnMatchStartEvent;
            MatchEvents.Lobby += OnMatchLobbyEvent;
            MatchEvents.Hide += OnMatchHideEvent;
            MatchEvents.ReadyState += OnReadyStateEvent;
        }

        void UnsubscribeEvents()
        {
            MatchEvents.Result -= OnMatchResultEvent;
            MatchEvents.Start -= OnMatchStartEvent;
            MatchEvents.Lobby -= OnMatchLobbyEvent;
            MatchEvents.Hide -= OnMatchHideEvent;
            MatchEvents.ReadyState -= OnReadyStateEvent;
        }

        static void OnMatchResultEvent(bool victory, float durationSec) => Show(victory, durationSec);
        static void OnMatchLobbyEvent() => ShowLobby();
        static void OnMatchStartEvent() => Hide();
        static void OnMatchHideEvent() => Hide();
        static void OnReadyStateEvent(S2C_Battle_RematchReadyPacket state) => ApplyReadyState(state);

        public static void Show(bool victory, float durationSec)
        {
            Ensure();
            _instance?.ApplyResult(victory, durationSec);
        }

        public static void ShowLobby()
        {
            Ensure();
            _instance?.ApplyLobby();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._localReady = false;
            _instance.HideViews();
        }

        public static void ApplyReadyState(S2C_Battle_RematchReadyPacket state)
        {
            Ensure();
            if (_instance == null) return;

            var session = BattleSystem.Instance?.Session;
            if (!_instance.IsShowing)
            {
                if (session != null && !session.MatchPlaying && !session.MatchEnded)
                    _instance.ApplyLobby();
                else if (session == null || session.MatchPlaying)
                    return;
            }

            _instance._readyCount = state?.ReadyCount ?? 0;
            _instance._totalCount = Mathf.Max(1, state?.TotalCount ?? 1);

            long localId = BattleSystem.Instance?.LocalPlayerId ?? 0;
            _instance._localReady = false;
            if (state?.ReadyEntityIds != null && localId != 0)
            {
                for (int i = 0; i < state.ReadyEntityIds.Length; i++)
                {
                    if (state.ReadyEntityIds[i] != localId) continue;
                    _instance._localReady = true;
                    break;
                }
            }

            _instance.RefreshReadyUi();
        }

        void ApplyResult(bool victory, float durationSec)
        {
            SetView(lobby: false, result: true);
            _localReady = false;
            _readyCount = 0;
            _totalCount = 1;
            if (resultTitle != null)
            {
                resultTitle.text = victory ? "胜利" : "失败";
                resultTitle.color = victory ? Victory : Defeat;
            }

            int m = Mathf.FloorToInt(durationSec / 60f);
            int s = Mathf.FloorToInt(durationSec % 60f);
            if (resultDetail != null)
                resultDetail.text = $"对局时长  {m:00}:{s:00}";
            RefreshReadyUi();
        }

        void ApplyLobby()
        {
            SetView(lobby: true, result: false);
            _localReady = false;
            _readyCount = 0;
            _totalCount = 1;
            RefreshReadyUi();
        }

        void SetView(bool lobby, bool result)
        {
            if (lobbyBar != null && lobbyBar.activeSelf != lobby)
                lobbyBar.SetActive(lobby);
            if (resultRoot != null && resultRoot.activeSelf != result)
                resultRoot.SetActive(result);
        }

        void HideViews()
        {
            SetView(lobby: false, result: false);
        }

        void OnReadyClicked()
        {
            bool next = !_localReady;
            NetworkManager.Instance?.Send(EOpCode.C2S_Battle_RematchReadyCmd, new C2S_Battle_RematchReadyCmd
            {
                Ready = next ? 1 : 0
            });
            _localReady = next;
            RefreshReadyUi();
        }

        void RefreshReadyUi()
        {
            var session = BattleSystem.Instance?.Session;
            bool inResult = session != null && session.MatchEnded;

            SetReadyLabel(resultReadyLabel, inResult
                ? $"准备  {_readyCount}/{_totalCount}"
                : $"全员准备后开战：{_readyCount}/{_totalCount}");
            SetReadyLabel(lobbyReadyLabel, $"全员准备后开战：{_readyCount}/{_totalCount}");

            string resultBtn = inResult
                ? (_localReady ? "已确认…" : "返回演练场")
                : (_localReady ? "取消准备" : "准备开战");
            string lobbyBtn = _localReady ? "取消准备" : "准备开战";
            SetReadyButton(resultReadyButtonText, resultReadyButtonImage, resultBtn);
            SetReadyButton(lobbyReadyButtonText, lobbyReadyButtonImage, lobbyBtn);
        }

        static void SetReadyLabel(Text label, string text)
        {
            if (label != null)
                label.text = text;
        }

        void SetReadyButton(Text label, Image image, string text)
        {
            if (label != null)
                label.text = text;
            if (image != null)
                image.color = _localReady ? ReadyOn : ReadyIdle;
        }
    }
}
