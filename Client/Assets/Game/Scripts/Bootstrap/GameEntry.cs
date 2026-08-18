using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Client.Battle;
using Client.Network;
using Game.ZeonAsset;
using Launch;

namespace Client
{
    /// <summary>客户端唯一入口：启动、每帧推进、关闭。</summary>
    public class GameEntry : MonoBehaviour
    {
        #region Inspector 配置
        [Header("Network")]
        [Tooltip("仅编辑器强制覆盖主机。日常留空。真机忽略。")]
        public string ServerIP = "";

        [Tooltip("仅编辑器强制覆盖端口。日常留空则读 Tools/local_dev.json。真机忽略。")]
        public int ServerPort = 0;

        [Header("Delay Simulation")]
        [Tooltip("默认关。真机测延迟时在战斗 GM「延迟」页打开。")]
        public bool EnableDelaySimulation = false;
        public int DelayRTT_MS = 80;
        public int DelayJitter_MS = 20;
        public float OutboundLossChance = 0f;
        #endregion

        #region 运行时状态
        private NetworkManager _network;
        private BattleSystem _battle;
        private bool _booted;
        #endregion

        #region 启动
        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);

            if (!TryResolveServerEndpoint(out var host, out var port, out var source, out var resolveError))
            {
                Debug.LogError("[GameEntry] " + resolveError);
                yield break;
            }

            Debug.Log($"[GameEntry] 连服来源={source} → {host}:{port}");

            BattleAssets.BindRunner(this);
            DevicePerf.ApplyToAllCameras();

            if (BattleScene.Current == null)
                Debug.LogError("[GameEntry] 场景缺少 BattleScene，请挂在 BattleRoot 上");

            _network = NetworkManager.Create(
                host, port,
                EnableDelaySimulation, DelayRTT_MS, DelayJitter_MS, OutboundLossChance);

            _battle = BattleSystem.Create();

            // 进战场门：配置 + 必要暖场 + 连服，全部在加载 UI 下完成后再揭开。
            AppLoading.BeginEnterBattle();
            AppLoading.SetProgress(0.15f, "正在加载配置与资源");

            yield return _battle.BootAsync();
            _booted = true;

            AppLoading.SetProgress(0.75f, "正在连接服务器");

            var connect = _network.ConnectToServerAsync();
            while (!connect.IsCompleted)
                yield return null;

            if (!connect.Result)
            {
                var msg =
                    $"无法连接 {host}:{port}。请先跑 Tools/start_server；" +
                    "HostPlay/真机还需 VersionCheck 下发游戏服（local_dev_server）。";
                Debug.LogError("[GameEntry] " + msg);
                AppLoading.Fail("连接失败", msg);
                yield break;
            }

            string playerName = "Player_" + UnityEngine.Random.Range(100, 999);
            _network.SendLogin(playerName);
            Debug.Log($"[GameEntry] Boot OK → {host}:{port}，已发送登录: {playerName}");

            AppLoading.Succeed("进入完成");
        }

        /// <summary>
        /// EditorSimulate：Inspector 完整覆盖 → Tools/local_dev.json（不读沙盒）。
        /// HostPlay / 真机：Inspector 完整覆盖 → VersionCheck 写入的沙盒 boot_dispatch。
        /// </summary>
        private bool TryResolveServerEndpoint(
            out string host, out int port, out string source, out string error)
        {
            host = null;
            port = 0;
            source = null;
            error = null;

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(ServerIP) && ServerPort > 0)
            {
                host = ServerIP.Trim();
                port = ServerPort;
                source = "Inspector";
                return true;
            }

            if (IsEditorSimulatePlayMode())
            {
                if (TryResolveEditorLocalDev(out host, out port))
                {
                    source = "EditorLocalDev";
                    return true;
                }

                error =
                    "EditorSimulate 需要游戏服地址。请确认存在 Tools/local_dev.json，" +
                    "或在 GameEntry 填 ServerIP/Port。";
                return false;
            }
#endif

            if (BootDispatchCache.TryReadGameServer(out host, out port))
            {
                source = "BootDispatchCache";
                return true;
            }

            error = "没有可用的游戏服地址。HostPlay/真机请先走通 VersionCheck（会写入沙盒）。";
            return false;
        }

        private static bool IsEditorSimulatePlayMode()
        {
            return AssetManager.Config.PlayMode == EPlayMode.EditorSimulate;
        }

#if UNITY_EDITOR
        [Serializable]
        private class LocalDevFile
        {
            public string server_url = string.Empty;
        }

        /// <summary>本机 Play：127.0.0.1 + local_dev.server_url 的端口。</summary>
        private bool TryResolveEditorLocalDev(out string host, out int port)
        {
            host = string.IsNullOrWhiteSpace(ServerIP) ? "127.0.0.1" : ServerIP.Trim();
            port = ServerPort > 0 ? ServerPort : 8888;

            try
            {
                var path = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "..", "Tools", "local_dev.json"));
                if (File.Exists(path) && ServerPort <= 0)
                {
                    var raw = File.ReadAllText(path);
                    var data = JsonUtility.FromJson<LocalDevFile>(raw);
                    if (data != null && TryParseServerPort(data.server_url, out var fromUrl))
                        port = fromUrl;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameEntry] 读 Tools/local_dev.json 失败: " + e.Message);
            }

            return !string.IsNullOrWhiteSpace(host) && port > 0;
        }

        private static bool TryParseServerPort(string serverUrl, out int port)
        {
            port = 0;
            var s = (serverUrl ?? string.Empty).Trim();
            if (s.Length == 0)
                return false;
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(7);
            else if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(8);
            var slash = s.IndexOf('/');
            if (slash >= 0)
                s = s.Substring(0, slash);
            var colon = s.LastIndexOf(':');
            return colon > 0 && int.TryParse(s.Substring(colon + 1), out port) && port > 0;
        }
#endif

        #endregion

        #region 每帧驱动
        private void Update()
        {
            float dt = Time.deltaTime;
            _network?.Tick();
            _battle?.Tick(dt);
        }

        private void LateUpdate()
        {
            _battle?.LateTick(Time.deltaTime);
        }
        #endregion

        #region 关停
        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            if (!_booted) return;
            _booted = false;

            _battle?.Shutdown();
            _network?.Shutdown();
            BattleScene.Shutdown();

            _battle = null;
            _network = null;
        }
        #endregion
    }
}
