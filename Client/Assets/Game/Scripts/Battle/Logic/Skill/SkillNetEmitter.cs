using Client.Network;
using Shared;

namespace Client.Battle
{
    /// <summary>技能发包入口。战斗逻辑不直接碰网络。</summary>
    public static class SkillNetEmitter
    {
        /// <summary>发施法命令。心跳号要和本地这次施法对得上。</summary>
        public static void SendCastCmd(C2S_Battle_SkillCastCmd req)
        {
            NetworkManager.Instance?.Send(EOpCode.C2S_Battle_SkillCastCmd, req);
        }

        /// <summary>发取消。本地表现应已打断，这里只请服务器取消。</summary>
        public static void SendCancelCmd(int skillId, uint cancelReason, uint clientTick)
        {
            NetworkManager.Instance?.Send(EOpCode.C2S_Battle_SkillCancelCmd, new C2S_Battle_SkillCancelCmd
            {
                SkillId = skillId,
                CancelReason = cancelReason,
                ClientTick = clientTick
            });
        }
    }
}
