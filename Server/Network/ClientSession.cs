using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.Json;
using Shared;

namespace Server.Network
{
    public class ClientSession
    {
        public TcpClient Socket { get; private set; }
        public long PlayerEntityId { get; set; }
        /// <summary>单向延迟（秒），用来把出手时间往回拨。</summary>
        public float OneWayDelaySeconds { get; set; }

        private readonly NetworkStream _stream;
        private readonly byte[] _readBuffer = new byte[8192];
        private readonly List<byte> _pendingBytes = new List<byte>(4096);
        private bool _isDead;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true
        };

        public ClientSession(TcpClient socket)
        {
            Socket = socket;
            Socket.NoDelay = true;
            _stream = socket.GetStream();
        }

        /// <summary>连接是否还活着。光看 Connected 不可靠。</summary>
        public bool IsAlive
        {
            get
            {
                if (_isDead || Socket == null)
                    return false;

                try
                {
                    Socket sock = Socket.Client;
                    if (sock == null || !sock.Connected)
                        return false;

                    // 能读但没数据：对端已经关掉
                    if (sock.Poll(0, SelectMode.SelectRead) && sock.Available == 0)
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void MarkDead()
        {
            _isDead = true;
        }

        public void Send<T>(EOpCode opCode, T payloadObj)
        {
            if (!IsAlive) return;

            try
            {
                string jsonPayload = JsonSerializer.Serialize(payloadObj, JsonOptions);
                byte[] data = NetPacketCodec.Encode(opCode, jsonPayload);
                lock (_stream)
                {
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientSession] Send error EntityId={PlayerEntityId}: {ex.Message}");
                MarkDead();
            }
        }

        public void PollData()
        {
            if (!IsAlive) return;

            try
            {
                while (_stream.DataAvailable)
                {
                    int bytesRead = _stream.Read(_readBuffer, 0, _readBuffer.Length);
                    if (bytesRead <= 0)
                    {
                        MarkDead();
                        return;
                    }

                    for (int i = 0; i < bytesRead; i++)
                        _pendingBytes.Add(_readBuffer[i]);
                }

                var packets = NetPacketCodec.TryDecodeMany(_pendingBytes);
                foreach (var (opCode, json) in packets)
                {
                    try
                    {
                        NetworkServer.Instance?.Dispatch(this, opCode, json);
                    }
                    catch (Exception ex)
                    {
                        // 业务报错别踢线，否则再也收不到移动
                        Console.WriteLine(
                            $"[ClientSession] Dispatch {opCode} error EntityId={PlayerEntityId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientSession] PollData error EntityId={PlayerEntityId}: {ex.Message}");
                MarkDead();
            }
        }

        public void Close()
        {
            MarkDead();
            try
            {
                _stream?.Close();
                Socket?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientSession] Close error: {ex.Message}");
            }
        }
    }
}
