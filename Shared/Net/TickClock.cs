using System;

namespace Shared
{
    /// <summary>两端对表：用来回延迟估网络延迟，并记下本地帧号。</summary>
    public sealed class TickClock
    {
        #region --- 全局一份 / 当前状态 ---

        public static TickClock Instance { get; } = new TickClock();

        /// <summary>当前本地帧号。</summary>
        public uint ClientTick { get; private set; }
        /// <summary>单程延迟（秒），大约是来回延迟的一半。</summary>
        public float OneWayDelaySeconds { get; private set; }
        /// <summary>来回延迟（秒）。</summary>
        public float RttSeconds { get; private set; }
        /// <summary>对过表没有。</summary>
        public bool HasSync { get; private set; }

        public void Reset()
        {
            ClientTick = 0;
            OneWayDelaySeconds = 0f;
            RttSeconds = 0f;
            HasSync = false;
        }

        #endregion

        #region --- 帧号推进 ---

        /// <summary>本地帧号加一并返回。</summary>
        public uint NextClientTick()
        {
            ClientTick++;
            return ClientTick;
        }

        /// <summary>本地帧号加一。</summary>
        public void AdvanceClientTick()
        {
            ClientTick++;
        }

        #endregion

        #region --- 对表 ---

        /// <summary>收到对表包后更新来回延迟。</summary>
        /// <remarks>现在只用到来回延迟的一半，帧号先留着。</remarks>
        public void OnSyncResponse(long clientSendMs, long serverTimeMs, uint serverTick, long nowMs)
        {
            _ = serverTick;
            long rttMs = nowMs - clientSendMs;
            if (rttMs < 0) rttMs = 0;
            if (rttMs > 2000) rttMs = 2000;

            RttSeconds = rttMs / 1000f;
            OneWayDelaySeconds = RttSeconds * 0.5f;
            HasSync = true;
        }

        /// <summary>现在的 Unix 毫秒时间。</summary>
        public long NowClientMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        #endregion
    }
}
