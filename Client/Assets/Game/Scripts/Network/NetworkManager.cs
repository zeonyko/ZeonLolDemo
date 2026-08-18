using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Battle;
using UnityEngine;
using Shared;

namespace Client.Network
{
    /// <summary>网络总入口：登录/战斗代理在此绑定。游戏入口只负责创建、每帧推进、关闭。</summary>
    public class NetworkManager
    {
        public static NetworkManager Instance { get; private set; }

        public string ServerIP { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 8888;

        #region 收发流量统计
        public long TotalSentBytes { get; private set; }
        public long TotalReceivedBytes { get; private set; }
        public long TotalSentPackets { get; private set; }
        public long TotalReceivedPackets { get; private set; }
        #endregion

        #region 底层 Socket 连接状态
        private TcpClient _socket;
        private NetworkStream _stream;
        private Thread _receiveThread;   // 后台收包：只读字节、拆包，入队后交给主线程
        private volatile bool _isRunning; // 收包线程和主线程都会读
        #endregion

        #region 消息分派（协议号 → 回调）
        private readonly Dictionary<EOpCode, Action<string>> _handlers = new Dictionary<EOpCode, Action<string>>();
        // 记下原始回调对应的包装，方便精确增删同一个监听
        private readonly Dictionary<(EOpCode Op, Delegate Handler), Action<string>> _typedWrappers =
            new Dictionary<(EOpCode, Delegate), Action<string>>();
        private readonly Queue<Packet> _receiveQueue = new Queue<Packet>(); // 收包线程 → 主线程 的待处理队列
        private readonly object _queueLock = new object();
        #endregion

        #region 业务代理（Auth / Battle / 延迟模拟）
        private NetworkDelaySimulator _delaySim;
        private AuthCommandHandler _auth;
        private BattleNetHandler _battleNet;
        #endregion

        #region 创建 / 挂接业务代理
        public static NetworkManager Create(
            string ip,
            int port,
            bool enableDelaySim = false,
            int delayRttMs = 80,
            int delayJitterMs = 20,
            float outboundLossChance = 0f)
        {
            Instance = new NetworkManager
            {
                ServerIP = ip,
                ServerPort = port
            };
            Instance.BootProxies(enableDelaySim, delayRttMs, delayJitterMs, outboundLossChance);
            return Instance;
        }

        private void BootProxies(bool enableDelaySim, int delayRttMs, int delayJitterMs, float outboundLossChance)
        {
            _delaySim = NetworkDelaySimulator.Create(
                enableDelaySim, delayRttMs, delayJitterMs, outboundLossChance);
            BindDelaySimulator(_delaySim);

            _auth = new AuthCommandHandler();
            _auth.Bind(this);
        }

        /// <summary>开战时挂上战斗协议处理。</summary>
        public void AttachBattle(BattleSession session)
        {
            DetachBattle();
            if (session == null) return;
            _battleNet = new BattleNetHandler(session);
            _battleNet.Bind(this);
        }

        public void DetachBattle()
        {
            _battleNet?.Unbind(this);
            _battleNet = null;
        }

        public void NotifyBattleLoginSuccess(S2C_Auth_LoginRsp loginRsp)
        {
            _battleNet?.OnLoginSuccess(loginRsp);
        }

        public void SendLogin(string playerName)
        {
            _auth?.SendLogin(playerName);
        }

        public void BindDelaySimulator(NetworkDelaySimulator sim)
        {
            sim?.Bind(WriteToSocket, DeliverInboundImmediate);
        }
        #endregion

        #region 主线程每帧驱动
        /// <summary>每帧推进延迟模拟和战斗网络，并把已到的包交给对应处理。</summary>
        public void Tick()
        {
            _delaySim?.Tick();
            _battleNet?.Tick();
            DispatchQueuedPackets();
        }

        /// <summary>把队列里的包交给对应处理。一个包出错不影响后面的包。</summary>
        private void DispatchQueuedPackets()
        {
            lock (_queueLock)
            {
                while (_receiveQueue.Count > 0)
                {
                    var packet = _receiveQueue.Dequeue();
                    if (_handlers.TryGetValue(packet.OpCode, out var handler))
                    {
                        try
                        {
                            handler?.Invoke(packet.PayloadJson);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[NetworkManager] 处理 {packet.OpCode} 异常: {ex.Message}");
                        }
                    }
                }
            }
        }
        #endregion

        #region 连接建立 / 接收线程
        public async Task<bool> ConnectToServerAsync()
        {
            try
            {
                _socket = new TcpClient();
                await _socket.ConnectAsync(ServerIP, ServerPort);
                _stream = _socket.GetStream();
                _isRunning = true;

                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();

                Debug.Log($"<color=green>[NetworkManager] 已连接 {ServerIP}:{ServerPort}</color>");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] 连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>后台一直收字节、拆包，再交给入站通道（可能先模拟延迟）。</summary>
        private void ReceiveLoop()
        {
            var pending = new List<byte>(4096);
            byte[] readBuffer = new byte[8192];

            try
            {
                while (_isRunning && _socket != null && _socket.Connected)
                {
                    int bytesRead = _stream.Read(readBuffer, 0, readBuffer.Length);
                    if (bytesRead <= 0)
                        break;

                    TotalReceivedBytes += bytesRead;
                    for (int i = 0; i < bytesRead; i++)
                        pending.Add(readBuffer[i]);

                    var packets = NetPacketCodec.TryDecodeMany(pending);
                    foreach (var (opCode, json) in packets)
                    {
                        TotalReceivedPackets++;
                        EnqueueInbound(opCode, json);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                    Debug.LogWarning($"[NetworkManager] 接收线程断开: {ex.Message}");
            }
            finally
            {
                CloseConnection();
            }
        }
        #endregion

        #region 入站包投递（经/不经延迟模拟）
        private void EnqueueInbound(EOpCode opCode, string json)
        {
            var sim = NetworkDelaySimulator.Instance;
            bool delayed = sim != null && sim.ShouldInterceptInbound;
            int bytes = NetPacketCodec.HeaderSize + NetPacketCodec.OpCodeSize +
                        Encoding.UTF8.GetByteCount(json ?? string.Empty);
            NetTrace.Record(false, opCode, bytes, delayed, json);

            if (delayed)
            {
                sim.EnqueueInboundFromNetworkThread(opCode, json);
                return;
            }

            DeliverInboundImmediate(opCode, json);
        }

        private void DeliverInboundImmediate(EOpCode opCode, string json)
        {
            lock (_queueLock)
            {
                _receiveQueue.Enqueue(new Packet
                {
                    OpCode = opCode,
                    PayloadJson = json
                });
            }
        }
        #endregion

        #region Handler 注册 / 反注册
        /// <summary>注册监听：内部先解析 JSON，回调直接拿到消息对象。</summary>
        public void RegisterHandler<T>(EOpCode opCode, Action<T> handler) where T : class
        {
            if (handler == null) return;

            var key = (opCode, (Delegate)handler);
            if (_typedWrappers.ContainsKey(key)) return;

            Action<string> wrapper = json =>
            {
                T msg = null;
                try
                {
                    msg = JsonUtility.FromJson<T>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetworkManager] {opCode} 反序列化失败: {ex.Message}");
                    return;
                }

                if (msg == null)
                {
                    Debug.LogWarning($"[NetworkManager] {opCode} 解析结果为空");
                    return;
                }

                handler(msg);
            };

            _typedWrappers[key] = wrapper;
            if (_handlers.ContainsKey(opCode))
                _handlers[opCode] += wrapper;
            else
                _handlers[opCode] = wrapper;
        }

        public void UnregisterHandler<T>(EOpCode opCode, Action<T> handler) where T : class
        {
            if (handler == null) return;

            var key = (opCode, (Delegate)handler);
            if (!_typedWrappers.TryGetValue(key, out var wrapper)) return;

            _typedWrappers.Remove(key);
            if (_handlers.ContainsKey(opCode))
                _handlers[opCode] -= wrapper;
        }
        #endregion

        #region 发送
        public void Send<T>(EOpCode opCode, T payload)
        {
            if (_socket == null || !_socket.Connected || _stream == null)
            {
                Debug.LogWarning("[NetworkManager] 未连接，发送失败");
                return;
            }

            try
            {
                string json = JsonUtility.ToJson(payload);
                byte[] sendBuffer = NetPacketCodec.Encode(opCode, json);

                var sim = NetworkDelaySimulator.Instance;
                bool delayed = sim != null && sim.TryEnqueueOutbound(sendBuffer);
                NetTrace.Record(true, opCode, sendBuffer.Length, delayed, json);
                if (delayed)
                    return;

                WriteToSocket(sendBuffer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] 发送异常: {ex.Message}");
            }
        }

        private void WriteToSocket(byte[] sendBuffer)
        {
            if (_stream == null || !_isRunning) return;

            try
            {
                lock (_stream)
                {
                    _stream.Write(sendBuffer, 0, sendBuffer.Length);
                }
                TotalSentBytes += sendBuffer.Length;
                TotalSentPackets++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] Socket 写入失败: {ex.Message}");
            }
        }
        #endregion

        #region 关停
        public void Shutdown()
        {
            DetachBattle();
            _auth?.Unbind(this);
            _auth = null;

            _delaySim?.Shutdown();
            _delaySim = null;
            NetTrace.Clear();

            CloseConnection();
            if (Instance == this)
                Instance = null;
        }

        private void CloseConnection()
        {
            _isRunning = false;
            try
            {
                if (_socket != null)
                {
                    try { _socket.Client?.Shutdown(SocketShutdown.Both); } catch { }
                    _stream?.Close();
                    _socket.Close();
                }
            }
            catch { }

            _stream = null;
            _socket = null;
        }
        #endregion
    }

    public struct Packet
    {
        public EOpCode OpCode;
        public string PayloadJson;
    }
}
