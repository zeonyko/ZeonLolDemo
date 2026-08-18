using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Shared;
using Server.Battle;

namespace Server.Network
{
    /// <summary>听连接、收发包、分给登录和战斗。</summary>
    public class NetworkServer
    {
        public static NetworkServer Instance { get; private set; }

        private TcpListener _listener;
        private readonly List<ClientSession> _sessions = new List<ClientSession>();
        private bool _isListening;

        private AuthCommandHandler _auth;
        private BattleCommandHandler _battleNet;

        public int OnlineSessionCount
        {
            get
            {
                lock (_sessions) return _sessions.Count;
            }
        }

        public static NetworkServer Create()
        {
            Instance = new NetworkServer();
            Instance._auth = new AuthCommandHandler();
            Instance._battleNet = new BattleCommandHandler();
            BattleNetPublisher.Bind(new BattleNetPublisher(Instance));
            return Instance;
        }

        public void Start(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isListening = true;
            _listener.BeginAcceptTcpClient(OnAcceptTcpClient, null);
            Console.WriteLine($"[NetworkServer] Listening on 0.0.0.0:{port} (all interfaces)");
            foreach (var ip in GetLanIPv4Addresses())
                Console.WriteLine($"[NetworkServer] Phone / LAN connect → {ip}:{port}");
        }

        private static List<string> GetLanIPv4Addresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;
                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        var s = addr.Address.ToString();
                        if (s.StartsWith("127."))
                            continue;
                        if (!list.Contains(s))
                            list.Add(s);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[NetworkServer] list LAN IP failed: " + e.Message);
            }

            return list;
        }

        public void Dispatch(ClientSession session, EOpCode opCode, string json)
        {
            switch (opCode)
            {
                case EOpCode.C2S_Auth_LoginReq:
                {
                    var msg = NetJson.Parse<C2S_Auth_LoginReq>(json);
                    if (msg != null) _auth?.OnLoginReq(session, msg);
                    break;
                }
                case EOpCode.C2S_Battle_SyncTimeCmd:
                case EOpCode.C2S_Battle_InputCmd:
                case EOpCode.C2S_Battle_SkillCastCmd:
                case EOpCode.C2S_Battle_SkillCancelCmd:
                case EOpCode.C2S_Battle_RematchReadyCmd:
                    _battleNet?.OnReceive(session, opCode, json);
                    break;
                default:
                    Console.WriteLine($"[NetworkServer] Unknown OpCode: {opCode}");
                    break;
            }
        }

        private void OnAcceptTcpClient(IAsyncResult ar)
        {
            if (!_isListening) return;

            try
            {
                TcpClient client = _listener.EndAcceptTcpClient(ar);
                var session = new ClientSession(client);
                lock (_sessions)
                {
                    _sessions.Add(session);
                }
                Console.WriteLine($"[NetworkServer] New connection: {client.Client.RemoteEndPoint} | sessions={_sessions.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkServer] Accept error: {ex.Message}");
            }
            finally
            {
                if (_isListening)
                    _listener.BeginAcceptTcpClient(OnAcceptTcpClient, null);
            }
        }

        public void Update()
        {
            lock (_sessions)
            {
                for (int i = _sessions.Count - 1; i >= 0; i--)
                {
                    var session = _sessions[i];
                    if (!session.IsAlive)
                    {
                        RemoveSession(session, i);
                        continue;
                    }

                    session.PollData();

                    if (!session.IsAlive)
                        RemoveSession(session, i);
                }
            }
        }

        /// <summary>每帧推进玩家移动，并跟服务器对齐位置。</summary>
        public void TickBattle(float dt)
        {
            _battleNet?.TickMovement(dt);
        }

        public void ForEachSession(Action<ClientSession> action)
        {
            if (action == null) return;
            ClientSession[] snapshot;
            lock (_sessions)
            {
                snapshot = _sessions.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
                action(snapshot[i]);
        }

        private void RemoveSession(ClientSession session, int index)
        {
            long id = session.PlayerEntityId;
            _battleNet?.OnClientDisconnected(session);
            session.Close();
            _sessions.RemoveAt(index);
            Console.WriteLine($"[NetworkServer] Session removed EntityId={id} | sessions={_sessions.Count}");
        }

        public void BroadcastAll<T>(EOpCode opCode, T payloadObj)
        {
            lock (_sessions)
            {
                foreach (var session in _sessions)
                {
                    if (session.IsAlive)
                        session.Send(opCode, payloadObj);
                }
            }
        }

        public void BroadcastExcept<T>(ClientSession exceptSession, EOpCode opCode, T payloadObj)
        {
            lock (_sessions)
            {
                foreach (var session in _sessions)
                {
                    if (session != exceptSession && session.IsAlive)
                        session.Send(opCode, payloadObj);
                }
            }
        }

        public void Stop()
        {
            _isListening = false;
            _listener?.Stop();

            lock (_sessions)
            {
                foreach (var session in _sessions)
                    session.Close();
                _sessions.Clear();
            }

            _auth = null;
            _battleNet = null;
            BattleNetPublisher.Unbind();
            if (Instance == this)
                Instance = null;

            Console.WriteLine("[NetworkServer] Stopped");
        }
    }
}
