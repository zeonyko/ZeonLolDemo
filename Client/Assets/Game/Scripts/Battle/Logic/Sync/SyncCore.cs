using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    // 客户端位置同步：本地先动，再跟服务器对齐。
    // 服务器确认序号之前的输入丢掉；之后的输入从服务器位置重演。

    #region 同步调试开关
    /// <summary>同步调试开关（GM 面板读写）。</summary>
    public static class SyncDebugSettings
    {
        /// <summary>脚底标记：本地权威位(S) / 远端英雄原始包位(R)</summary>
        public static bool ShowMarkers = false;

        /// <summary>别人位置平滑跟上</summary>
        public static bool RemoteInterpolation = true;

        /// <summary>自己先动，再跟服务器对齐</summary>
        public static bool LocalPrediction = true;
    }
    #endregion

    #region 本地预测缓冲
    /// <summary>一条已本地先动、等着跟服务器对齐的输入</summary>
    public struct PendingPrediction
    {
        public uint Sequence;   // 这条输入对应的本地心跳号
        public float MoveX;     // 水平移动意图 X
        public float MoveZ;     // 水平移动意图 Z
        public bool Jump;       // 是否包含跳跃
        public float DeltaTime; // 这条输入对应的帧时长
    }

    /// <summary>本地先动的输入队列。服务器确认后丢掉已模拟的，剩下的从服务器位置重演。</summary>
    public class PredictionBuffer
    {
        private readonly Queue<PendingPrediction> _pending = new Queue<PendingPrediction>();
        private readonly int _maxSize;

        public int Count => _pending.Count;

        public PredictionBuffer(int maxSize = 128)
        {
            _maxSize = maxSize;
        }

        /// <summary>排入一条待确认输入；满了就丢掉最旧的。</summary>
        public void Enqueue(PendingPrediction prediction)
        {
            _pending.Enqueue(prediction);
            while (_pending.Count > _maxSize)
                _pending.Dequeue();
        }

        /// <summary>丢掉服务器已确认过的输入。</summary>
        public void AcknowledgeUpTo(uint ackSequence)
        {
            while (_pending.Count > 0 && _pending.Peek().Sequence <= ackSequence)
                _pending.Dequeue();
        }

        public IEnumerable<PendingPrediction> GetPending() => _pending;

        public void Clear() => _pending.Clear();
    }
    #endregion

    #region 跟服务器对齐
    /// <summary>服务器位置到了：退回再把未确认输入重演一遍</summary>
    public static class ClientReconciliation
    {
        /// <summary>这次服务器回包带的位置，以及确认到哪一号输入。</summary>
        public struct AuthoritativeState
        {
            public uint LastProcessedSequence;
            public MovementState MoveState;
        }

        /// <summary>从服务器位置重演还没确认的输入。技能位移不进队列，落点由服务器位置带着。</summary>
        public static bool TryReconcile(
            AuthoritativeState auth,
            PredictionBuffer buffer,
            Vector3 currentLocalPos,
            float speedMultiplier,
            out MovementState correctedState,
            out bool shouldHardSnap)
        {
            correctedState = auth.MoveState;
            shouldHardSnap = false;

            buffer.AcknowledgeUpTo(auth.LastProcessedSequence);
            float mul = speedMultiplier > 0f ? speedMultiplier : 1f;

            foreach (var pending in buffer.GetPending())
            {
                // pending 存的是方向意图；倍率用当前本地 State（与服端 Buff 对齐）
                correctedState = MovementSimulator.Simulate(correctedState, new MoveInput
                {
                    MoveX = pending.MoveX,
                    MoveZ = pending.MoveZ,
                    Jump = pending.Jump,
                    DeltaTime = pending.DeltaTime,
                    SpeedMultiplier = mul
                });
            }

            Vector3 correctedPos = new Vector3(correctedState.PosX, correctedState.PosY, correctedState.PosZ);
            float error = Vector3.Distance(currentLocalPos, correctedPos);

            if (error <= GameConstants.ReconcileIgnoreDistance)
                return false;

            shouldHardSnap = error >= GameConstants.ReconcileSnapDistance;
            return true;
        }
    }
    #endregion

    #region 别人位置平滑跟上
        /// <summary>一条带时间戳的别人位置，用来平滑跟上。</summary>
    public struct TransformSnapshot
    {
        public Vector3 Position;
        public float Timestamp;

        public TransformSnapshot(Vector3 position, float timestamp)
        {
            Position = position;
            Timestamp = timestamp;
        }
    }

    /// <summary>别人位置缓冲，在这里平滑跟上一次</summary>
    public class SnapshotInterpolator
    {
        private readonly List<TransformSnapshot> _buffer = new List<TransformSnapshot>();
        private readonly int _maxBufferSize;
        private readonly float _interpolationDelay;

        public SnapshotInterpolator(
            float interpolationDelay = GameConstants.SnapshotInterpolationDelay,
            int maxBufferSize = 20)
        {
            _interpolationDelay = interpolationDelay;
            _maxBufferSize = maxBufferSize;
        }

        /// <summary>清空缓冲并以给定位置/时间重新起始（用于 HardSnap）。</summary>
        public void Reset(Vector3 position, float time)
        {
            _buffer.Clear();
            _buffer.Add(new TransformSnapshot(position, time));
        }

        /// <summary>记下刚收到的别人位置。太旧的丢掉。</summary>
        public void PushSnapshot(Vector3 position, float receiveTime)
        {
            _buffer.Add(new TransformSnapshot(position, receiveTime));
            if (_buffer.Count > _maxBufferSize)
                _buffer.RemoveAt(0);
        }

        /// <summary>按画面延迟取样位置；超出缓冲范围就用两端。</summary>
        public bool TrySample(float now, out Vector3 renderPosition)
        {
            renderPosition = Vector3.zero;
            if (_buffer.Count == 0)
                return false;

            float renderTime = now - _interpolationDelay;

            if (renderTime >= _buffer[_buffer.Count - 1].Timestamp)
            {
                renderPosition = _buffer[_buffer.Count - 1].Position;
                return true;
            }

            if (renderTime <= _buffer[0].Timestamp)
            {
                renderPosition = _buffer[0].Position;
                return true;
            }

            for (int i = 0; i < _buffer.Count - 1; i++)
            {
                TransformSnapshot from = _buffer[i];
                TransformSnapshot to = _buffer[i + 1];

                if (renderTime < from.Timestamp || renderTime > to.Timestamp)
                    continue;

                float duration = to.Timestamp - from.Timestamp;
                float t = duration > 0.0001f ? (renderTime - from.Timestamp) / duration : 1f;
                renderPosition = Vector3.Lerp(from.Position, to.Position, t);

                if (i > 0)
                    _buffer.RemoveRange(0, i);

                return true;
            }

            renderPosition = _buffer[_buffer.Count - 1].Position;
            return true;
        }
    }
    #endregion
}
