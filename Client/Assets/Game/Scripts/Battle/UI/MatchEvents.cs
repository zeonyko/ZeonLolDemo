using Shared;

namespace Client.Battle
{
    /// <summary>对局事件：胜负/大厅/开始/隐藏/准备，给 UI 听。</summary>
    public static class MatchEvents
    {
        public static event System.Action<bool, float> Result;
        public static event System.Action Start;
        public static event System.Action Lobby;
        public static event System.Action Hide;
        public static event System.Action<S2C_Battle_RematchReadyPacket> ReadyState;

        public static void RaiseResult(bool victory, float duration) => Result?.Invoke(victory, duration);
        public static void RaiseStart() => Start?.Invoke();
        public static void RaiseLobby() => Lobby?.Invoke();
        public static void RaiseHide() => Hide?.Invoke();
        public static void RaiseReadyState(S2C_Battle_RematchReadyPacket state) => ReadyState?.Invoke(state);

        public static void Clear()
        {
            Result = null;
            Start = null;
            Lobby = null;
            Hide = null;
            ReadyState = null;
        }
    }
}
