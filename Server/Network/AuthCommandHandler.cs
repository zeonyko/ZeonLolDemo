using System;
using Shared;
using Server.Battle;

namespace Server.Network
{
    /// <summary>处理登录：建角色、回包、同步场上单位。</summary>
    public class AuthCommandHandler
    {
        public void OnLoginReq(ClientSession session, C2S_Auth_LoginReq req)
        {
            var world = WorldManager.Instance;
            var scene = world?.GetDefaultScene();
            if (world == null || scene == null)
            {
                Console.WriteLine("[Auth] Login failed: world not ready");
                return;
            }

            var player = world.CreatePlayer(req.PlayerName, scene);
            session.PlayerEntityId = player.Id;

            session.Send(EOpCode.S2C_Auth_LoginRsp, new S2C_Auth_LoginRsp
            {
                EntityId = player.Id,
                PlayerName = player.Name,
                PosX = player.PosX,
                PosY = player.PosY,
                PosZ = player.PosZ,
                TeamId = player.TeamId
            });

            session.Send(EOpCode.S2C_Battle_SyncEntitiesPacket, new S2C_Battle_SyncEntitiesPacket
            {
                Entities = scene.GetAllEntitiesData().ToArray()
            });

            NetworkServer.Instance.BroadcastExcept(session, EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
            {
                EntityId = player.Id,
                StateType = (int)EEntityStateType.Spawn,
                EntityData = player.ToEntityData()
            });

            if (!scene.MatchPlaying)
            {
                if (scene.MatchEnded)
                {
                    session.Send(EOpCode.S2C_Battle_MatchResultPacket, new S2C_Battle_MatchResultPacket
                    {
                        WinnerTeamId = scene.WinnerTeamId,
                        MatchDuration = scene.MatchElapsed,
                        DestroyedCrystalId = 0
                    });
                }
                else
                {
                    session.Send(EOpCode.S2C_Battle_MatchLobbyPacket, new S2C_Battle_MatchLobbyPacket());
                }

                scene.BroadcastRematchReadyPacket();
            }

            Console.WriteLine($"[Auth] Login ok: {req.PlayerName} Id={player.Id}");
        }
    }
}
