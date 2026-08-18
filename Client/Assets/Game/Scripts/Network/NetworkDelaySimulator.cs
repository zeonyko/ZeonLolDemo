using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Network
{
    /// <summary>网络延迟模拟。开关和延迟由 GM 面板调节。</summary>
    public class NetworkDelaySimulator
    {
        public static NetworkDelaySimulator Instance { get; private set; }

        #region 可调参数（战斗 GM 控制）
        public bool EnableSimulation = true;
        public int RTTMS = 80;
        public int JitterMS = 20;
        public float OutboundLossChance = 0f;
        #endregion

        #region 内部数据结构
        private struct TimedOutbound
        {
            public float DeliverAt;
            public byte[] Data;
        }

        private struct TimedInbound
        {
            public float DeliverAt;
            public EOpCode OpCode;
            public string Json;
        }

        // 网络线程先丢进无锁队列，主线程每帧再按时间排序后投递
        private struct RawInbound
        {
            public EOpCode OpCode;
            public string Json;
        }
        #endregion

        #region 延迟队列与投递回调
        private readonly ConcurrentQueue<RawInbound> _rawInbound = new ConcurrentQueue<RawInbound>();
        private readonly List<TimedOutbound> _delayedOutbound = new List<TimedOutbound>(64);
        private readonly List<TimedInbound> _delayedInbound = new List<TimedInbound>(64);

        private float _lastOutboundDeliverAt;
        private float _lastInboundDeliverAt;

        private Action<byte[]> _writeSocket;
        private Action<EOpCode, string> _deliverInbound;

        public int PendingOutbound => _delayedOutbound.Count;
        public int PendingInbound => _delayedInbound.Count + _rawInbound.Count;
        #endregion

        #region 创建 / 绑定 / 关停
        public static NetworkDelaySimulator Create(bool enable, int rttMs, int jitterMs, float outboundLossChance = 0f)
        {
            Instance = new NetworkDelaySimulator
            {
                EnableSimulation = enable,
                RTTMS = rttMs,
                JitterMS = jitterMs,
                OutboundLossChance = outboundLossChance
            };
            return Instance;
        }

        public void Shutdown()
        {
            _delayedOutbound.Clear();
            _delayedInbound.Clear();
            while (_rawInbound.TryDequeue(out _)) { }
            if (Instance == this)
                Instance = null;
        }

        public void Bind(Action<byte[]> writeSocket, Action<EOpCode, string> deliverInbound)
        {
            _writeSocket = writeSocket;
            _deliverInbound = deliverInbound;
        }
        #endregion

        #region 每帧驱动
        public void Tick()
        {
            PumpRawInboundToDelayedOrDeliver();
            FlushDelayed();
        }
        #endregion

        #region 延迟时间计算
        /// <summary>按半程延迟加抖动算出投递时间，且不早于上次，保证顺序。</summary>
        private float NextDeliverAt(ref float lastDeliverAt)
        {
            float now = Time.realtimeSinceStartup;
            float ms = RTTMS * 0.5f;
            if (JitterMS > 0)
                ms += UnityEngine.Random.Range(0, JitterMS + 1);

            float deliverAt = now + Mathf.Max(0f, ms) * 0.001f;
            if (deliverAt <= lastDeliverAt)
                deliverAt = lastDeliverAt + 0.0001f;

            lastDeliverAt = deliverAt;
            return deliverAt;
        }
        #endregion

        #region 出站：加入延迟队列（或直接丢包）
        /// <summary>发出去的包先排队模拟延迟。返回 false 表示没开模拟，应马上真发。</summary>
        public bool TryEnqueueOutbound(byte[] rawBytes)
        {
            if (_writeSocket == null)
                return false;

            if (!EnableSimulation || RTTMS <= 0)
                return false;

            if (OutboundLossChance > 0f && UnityEngine.Random.value < OutboundLossChance)
                return true;

            _delayedOutbound.Add(new TimedOutbound
            {
                DeliverAt = NextDeliverAt(ref _lastOutboundDeliverAt),
                Data = rawBytes
            });
            return true;
        }
        #endregion

        #region 入站：网络线程入队 → 延迟队列 → 主线程处理
        /// <summary>网络线程只入队，不直接处理，避免跨线程出事。</summary>
        public void EnqueueInboundFromNetworkThread(EOpCode opCode, string json)
        {
            _rawInbound.Enqueue(new RawInbound { OpCode = opCode, Json = json });
        }

        public bool ShouldInterceptInbound => EnableSimulation && RTTMS > 0;

        public bool ShouldInterceptOutbound => EnableSimulation && RTTMS > 0;

        /// <summary>把网络线程暂存的包转入延迟队列；没开模拟就马上投递。</summary>
        private void PumpRawInboundToDelayedOrDeliver()
        {
            while (_rawInbound.TryDequeue(out var raw))
            {
                if (!EnableSimulation || RTTMS <= 0)
                {
                    _deliverInbound?.Invoke(raw.OpCode, raw.Json);
                    continue;
                }

                _delayedInbound.Add(new TimedInbound
                {
                    DeliverAt = NextDeliverAt(ref _lastInboundDeliverAt),
                    OpCode = raw.OpCode,
                    Json = raw.Json
                });
            }
        }

        /// <summary>到期的收发包依次真正发出去/交给处理。</summary>
        private void FlushDelayed()
        {
            float now = Time.realtimeSinceStartup;

            while (_delayedOutbound.Count > 0 && _delayedOutbound[0].DeliverAt <= now)
            {
                _writeSocket?.Invoke(_delayedOutbound[0].Data);
                _delayedOutbound.RemoveAt(0);
            }

            while (_delayedInbound.Count > 0 && _delayedInbound[0].DeliverAt <= now)
            {
                _deliverInbound?.Invoke(_delayedInbound[0].OpCode, _delayedInbound[0].Json);
                _delayedInbound.RemoveAt(0);
            }
        }
        #endregion
    }
}
