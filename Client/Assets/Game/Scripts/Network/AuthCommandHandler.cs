using Client.Battle;
using Shared;
using UnityEngine;

namespace Client.Network
{
    /// <summary>登录收发。由网络管理器持有并绑定。</summary>
    public class AuthCommandHandler
    {
        private bool _bound; // 防重复绑定

        #region 绑定 / 解绑
        public void Bind(NetworkManager net)
        {
            if (_bound || net == null) return;
            net.RegisterHandler<S2C_Auth_LoginRsp>(EOpCode.S2C_Auth_LoginRsp, OnAuthLoginRsp);
            _bound = true;
        }

        public void Unbind(NetworkManager net)
        {
            if (!_bound || net == null) return;
            net.UnregisterHandler<S2C_Auth_LoginRsp>(EOpCode.S2C_Auth_LoginRsp, OnAuthLoginRsp);
            _bound = false;
        }
        #endregion

        #region 登录请求发送
        public void SendLogin(string playerName)
        {
            NetworkManager.Instance?.Send(EOpCode.C2S_Auth_LoginReq, new C2S_Auth_LoginReq
            {
                PlayerName = playerName
            });
        }
        #endregion

        #region 登录响应处理
        /// <summary>登录成功：记下本机会话，再通知战斗侧。</summary>
        private static void OnAuthLoginRsp(S2C_Auth_LoginRsp loginRsp)
        {
            var battle = BattleSystem.Instance;
            if (battle?.Session == null)
            {
                Debug.LogError("[Auth] LoginRsp 到达但 BattleSystem 未就绪");
                return;
            }

            battle.Session.LocalPlayerId = loginRsp.EntityId;
            battle.Session.LocalPlayerName = string.IsNullOrEmpty(loginRsp.PlayerName)
                ? $"Player_{loginRsp.EntityId}"
                : loginRsp.PlayerName;
            battle.Session.LocalTeamId = loginRsp.TeamId;
            battle.Session.MatchEnded = false;
            battle.Session.MatchPlaying = false;
            battle.Session.LocalPlayerDead = false;
            battle.Session.MatchElapsed = 0f;
            battle.Session.LocalRespawnEndsAt = -1f;

            Debug.Log($"<color=green>[Auth] Login ok Id={battle.Session.LocalPlayerId} Name={battle.Session.LocalPlayerName} Team={loginRsp.TeamId}</color>");
            NetworkManager.Instance?.NotifyBattleLoginSuccess(loginRsp);
        }
        #endregion
    }
}
