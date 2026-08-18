using System;
using Server.Network;
using Shared;

namespace Server.Battle
{
    /// <summary>战斗发包入口，不直接碰网络层。</summary>
    public interface IBattleNetPublisher
    {
        void BroadcastAll<T>(EOpCode opCode, T payloadObj);
        void BroadcastExcept<T>(ClientSession exceptSession, EOpCode opCode, T payloadObj);
        void Send<T>(ClientSession session, EOpCode opCode, T payloadObj);
        void ForEachSession(Action<ClientSession> action);
    }
}
