using System;
using Server.Network;
using Shared;

namespace Server.Battle
{
    /// <summary>战斗发包入口，转给网络层。</summary>
    public sealed class BattleNetPublisher : IBattleNetPublisher
    {
        public static IBattleNetPublisher Instance { get; private set; }

        private readonly NetworkServer _net;

        public BattleNetPublisher(NetworkServer net)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
        }

        public static void Bind(IBattleNetPublisher publisher)
        {
            Instance = publisher;
        }

        public static void Unbind()
        {
            Instance = null;
        }

        public void BroadcastAll<T>(EOpCode opCode, T payloadObj)
        {
            _net.BroadcastAll(opCode, payloadObj);
        }

        public void BroadcastExcept<T>(ClientSession exceptSession, EOpCode opCode, T payloadObj)
        {
            _net.BroadcastExcept(exceptSession, opCode, payloadObj);
        }

        public void Send<T>(ClientSession session, EOpCode opCode, T payloadObj)
        {
            session?.Send(opCode, payloadObj);
        }

        public void ForEachSession(Action<ClientSession> action)
        {
            _net.ForEachSession(action);
        }
    }
}
