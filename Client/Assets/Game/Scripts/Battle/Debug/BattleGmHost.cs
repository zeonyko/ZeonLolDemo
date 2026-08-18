using System.Collections;
using Client.Network;
using Game.ZeonAsset;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Battle
{
    /// <summary>战斗 GM：右上「战斗」呼出。界面是 Prefab，这里只填内容。</summary>
    public sealed class BattleGmHost : MonoBehaviour
    {
        enum ETab
        {
            State = 0,
            Delay = 1,
            Sync = 2,
            Story = 3,
        }

        const string PrefabKey = "Prefabs/UI/BattleGmPanel";

        static BattleGmHost _instance;
        static bool _loading;

        BattleGmView _view;
        bool _open;
        bool _bound;
        ETab _tab = ETab.State;
        float _nextRefresh = -1f;

        public static void Ensure()
        {
            if (_instance != null || _loading)
                return;

            if (TrySpawn(BattleAssets.Load<GameObject>(PrefabKey)))
                return;

            if (BattleAssets.Run(CoEnsure()))
            {
                _loading = true;
                return;
            }

            FailMissing();
        }

        static IEnumerator CoEnsure()
        {
            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(PrefabKey, p => prefab = p);
            _loading = false;
            if (_instance != null)
                yield break;
            if (!TrySpawn(prefab))
                FailMissing();
        }

        static bool TrySpawn(GameObject prefab)
        {
            if (prefab == null || _instance != null)
                return false;
            FinishSpawn(Object.Instantiate(prefab));
            return true;
        }

        static void FailMissing()
        {
            Debug.LogError("[BattleGM] 找不到 Prefabs/UI/BattleGmPanel。");
        }

        static void FinishSpawn(GameObject go)
        {
            go.name = "[BattleGm]";
            Object.DontDestroyOnLoad(go);
            if (_instance == null)
                go.AddComponent<BattleGmHost>();
        }

        public static void Shutdown()
        {
            _loading = false;
            DevicePerf.ExtraPerfLines = null;
            DevicePerf.ExtraDumpLines = null;
            NetTrace.Enabled = false;
            Time.timeScale = 1f;
            if (_instance == null)
                return;
            var go = _instance.gameObject;
            _instance = null;
            Destroy(go);
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _view = GetComponent<BattleGmView>();
            BindView();
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            NetTrace.Enabled = false;
        }

        void Update()
        {
            DevicePerf.Tick();
            if (_view == null)
                _view = GetComponent<BattleGmView>();
            BindView();
            if (_view == null)
                return;

            NetTrace.Enabled = _open;

            ZeonAssetDebuggerView.SetActive(_view.WindowRoot, _open);
            ZeonAssetDebuggerView.SetLabel(_view.ToggleButton, _open ? "关闭\n战斗" : "战斗");
            if (_open && Time.unscaledTime >= _nextRefresh)
            {
                RefreshUi();
                _nextRefresh = Time.unscaledTime + 0.25f;
            }
        }

        void BindView()
        {
            if (_bound || _view == null || _view.ToggleButton == null)
                return;
            _bound = true;

            Bind(_view.ToggleButton, () => _open = !_open);
            Bind(_view.CloseButton, () => _open = false);
            Bind(_view.DumpButton, () => Debug.Log(DevicePerf.BuildDump()));
            Bind(_view.TabState, () => Select(ETab.State));
            Bind(_view.TabDelay, () => Select(ETab.Delay));
            Bind(_view.TabSync, () => Select(ETab.Sync));
            Bind(_view.TabStory, () => Select(ETab.Story));
            Bind(_view.DelaySimButton, ToggleDelaySim);
            Bind(_view.RttPlus, () => NudgeRtt(20));
            Bind(_view.RttMinus, () => NudgeRtt(-20));
            Bind(_view.Time025, () => SetTimeScale(0.25f));
            Bind(_view.Time1, () => SetTimeScale(1f));
            Bind(_view.MarkersButton, () =>
            {
                SyncDebugSettings.ShowMarkers = !SyncDebugSettings.ShowMarkers;
                RefreshUi();
            });
            Bind(_view.LocalPredButton, () =>
            {
                SyncDebugSettings.LocalPrediction = !SyncDebugSettings.LocalPrediction;
                RefreshUi();
            });
            Bind(_view.RemoteInterpButton, () =>
            {
                SyncDebugSettings.RemoteInterpolation = !SyncDebugSettings.RemoteInterpolation;
                RefreshUi();
            });
            Bind(_view.AimRangeButton, () =>
            {
                SkillAimRangeView.ShowIndicator = !SkillAimRangeView.ShowIndicator;
                RefreshUi();
            });

            Bind(_view.StorySuppress, () => StoryManager.Instance?.TryPlay(StoryClips.Suppress()));
            Bind(_view.StoryComeback, () => StoryManager.Instance?.TryPlay(StoryClips.LowHpComeback()));
            Bind(_view.StoryTrade, () => StoryManager.Instance?.TryPlay(StoryClips.TradeToKill()));
            Bind(_view.StoryWhiff, () => StoryManager.Instance?.TryPlay(StoryClips.WhiffPunish()));
            Bind(_view.StoryKite, () => StoryManager.Instance?.TryPlay(StoryClips.Kite()));
            Bind(_view.StoryTwoVOne, () => StoryManager.Instance?.TryPlay(StoryClips.TwoVOneComeback()));
            Bind(_view.StoryStop, () => StoryManager.Instance?.Stop());
        }

        static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void Select(ETab tab)
        {
            _tab = tab;
            RefreshUi();
        }

        void ToggleDelaySim()
        {
            var sim = NetworkDelaySimulator.Instance;
            if (sim == null)
                return;
            sim.EnableSimulation = !sim.EnableSimulation;
            Debug.Log($"[GM] DelaySim={(sim.EnableSimulation ? "ON" : "OFF")}");
            RefreshUi();
        }

        void NudgeRtt(int delta)
        {
            var sim = NetworkDelaySimulator.Instance;
            if (sim == null)
                return;
            sim.RTTMS = Mathf.Clamp(sim.RTTMS + delta, 0, 500);
            Debug.Log($"[GM] RTT={sim.RTTMS}ms");
            RefreshUi();
        }

        static void SetTimeScale(float scale)
        {
            Time.timeScale = scale;
            Debug.Log($"[GM] timeScale={scale}");
        }

        void RefreshUi()
        {
            if (_view == null)
                return;

            ZeonAssetDebuggerView.Paint(_view.TabState, _tab == ETab.State);
            ZeonAssetDebuggerView.Paint(_view.TabDelay, _tab == ETab.Delay);
            ZeonAssetDebuggerView.Paint(_view.TabSync, _tab == ETab.Sync);
            ZeonAssetDebuggerView.Paint(_view.TabStory, _tab == ETab.Story);
            ZeonAssetDebuggerView.SetActive(_view.StatePage, _tab == ETab.State);
            ZeonAssetDebuggerView.SetActive(_view.DelayPage, _tab == ETab.Delay);
            ZeonAssetDebuggerView.SetActive(_view.SyncPage, _tab == ETab.Sync);
            ZeonAssetDebuggerView.SetActive(_view.StoryPage, _tab == ETab.Story);

            SetText(_view.StatusText, BuildStatus());
            ZeonAssetDebuggerView.FitPreferredHeight(_view.StatusText, 56f);
            if (_tab == ETab.State)
            {
                SetText(_view.StateBody, BattleGmSnapshot.StatePageText());
                ZeonAssetDebuggerView.FitScrollText(_view.StateBody);
            }
            if (_tab == ETab.Delay)
                RefreshDelay();
            if (_tab == ETab.Sync)
                RefreshSync();

            ZeonAssetDebuggerView.FitAllScrolls(_view.WindowRoot);
        }

        string BuildStatus()
        {
            var sim = NetworkDelaySimulator.Instance;
            string ping = sim != null && sim.EnableSimulation
                ? $"{sim.RTTMS}±{sim.JitterMS}ms"
                : "off";
            var nm = NetworkManager.Instance;
            string traffic = nm != null
                ? $"↑{BattleGmSnapshot.FormatBytes(nm.TotalSentBytes)} ↓{BattleGmSnapshot.FormatBytes(nm.TotalReceivedBytes)}"
                : "-";
            var cmp = SyncCompareComponent.Instance;
            string sync = cmp != null
                ? $"缓冲={cmp.PendingPredictions} 落后={cmp.LastAuthLead:F2}m 误差={cmp.LastReconcileError:F2}m"
                : "缓冲=-";
            string story = StoryManager.Instance != null && StoryManager.Instance.IsPlaying
                ? StoryManager.Instance.CurrentClipName
                : "无";
            return $"Ping {ping}  {traffic}\n{sync}  剧情:{story}";
        }

        void RefreshDelay()
        {
            var sim = NetworkDelaySimulator.Instance;
            bool on = sim != null && sim.EnableSimulation;
            ZeonAssetDebuggerView.Paint(_view.DelaySimButton, on);
            ZeonAssetDebuggerView.SetLabel(_view.DelaySimButton, on ? "延迟模拟 ON" : "延迟模拟 OFF");
            ZeonAssetDebuggerView.Paint(_view.Time025, Mathf.Abs(Time.timeScale - 0.25f) < 0.01f);
            ZeonAssetDebuggerView.Paint(_view.Time1, Mathf.Abs(Time.timeScale - 1f) < 0.01f);
            string scaleLine = $"\ntimeScale={Time.timeScale:F2}";
            if (Mathf.Abs(Time.timeScale - 1f) > 0.01f)
                scaleLine += "  （仅本地变慢，服务器仍按正常速度）";
            SetText(_view.PacketBody, NetTrace.Format() +
                (sim != null ? $"\nRTT={sim.RTTMS}ms  Jitter={sim.JitterMS}ms  排队出={sim.PendingOutbound} 入={sim.PendingInbound}" : "\nDelaySim 未就绪") +
                scaleLine);
            ZeonAssetDebuggerView.FitScrollText(_view.PacketBody);
        }

        void RefreshSync()
        {
            ZeonAssetDebuggerView.Paint(_view.MarkersButton, SyncDebugSettings.ShowMarkers);
            ZeonAssetDebuggerView.SetLabel(_view.MarkersButton,
                SyncDebugSettings.ShowMarkers ? "脚底标记 ON (S/R)" : "脚底标记 OFF");
            ZeonAssetDebuggerView.Paint(_view.LocalPredButton, SyncDebugSettings.LocalPrediction);
            ZeonAssetDebuggerView.SetLabel(_view.LocalPredButton,
                SyncDebugSettings.LocalPrediction ? "本地预测 ON（跟手）" : "本地预测 OFF（等服务器）");
            ZeonAssetDebuggerView.Paint(_view.RemoteInterpButton, SyncDebugSettings.RemoteInterpolation);
            ZeonAssetDebuggerView.SetLabel(_view.RemoteInterpButton,
                SyncDebugSettings.RemoteInterpolation ? "远端插值 ON（平滑）" : "远端插值 OFF（抖动）");
            ZeonAssetDebuggerView.Paint(_view.AimRangeButton, SkillAimRangeView.ShowIndicator);
            ZeonAssetDebuggerView.SetLabel(_view.AimRangeButton,
                SkillAimRangeView.ShowIndicator ? "范围指示器 ON" : "范围指示器 OFF");
        }

        static void SetText(Text ui, string value)
        {
            if (ui != null)
                ui.text = value ?? string.Empty;
        }
    }
}
