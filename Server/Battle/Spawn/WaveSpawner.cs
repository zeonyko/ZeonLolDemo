using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>一波小兵怎么配：3 近战 + 3 远程，偶尔加攻城；兵营毁了出超级兵。</summary>
    public static class WaveSpawner
    {
        /// <summary>列出这一波要出的兵（间隔稍后填）。</summary>
        public static List<Scene.PendingMinionSpawn> BuildWaveSpawns(
            Scene scene, int teamId, int waveIndex, bool broadcastSpawn)
        {
            var list = new List<Scene.PendingMinionSpawn>(8);
            string side = teamId == ETeamId.Blue ? "Blue" : "Red";

            if (scene != null && scene.ShouldSpawnSuperMinions(teamId))
            {
                // 同一门口出生，靠间隔拉成单列
                AppendLaneSpawn(list, teamId, EEntityType.MinionSuper, $"{side}_Super", broadcastSpawn);
                AppendLaneSpawn(list, teamId, EEntityType.MinionMelee, $"{side}_Melee_1", broadcastSpawn);
                AppendLaneSpawn(list, teamId, EEntityType.MinionMelee, $"{side}_Melee_2", broadcastSpawn);
                return list;
            }

            // 近战→远程→攻城，同一条路逐个出门
            for (int i = 0; i < 3; i++)
            {
                AppendLaneSpawn(list, teamId, EEntityType.MinionMelee, $"{side}_Melee_{i + 1}",
                    broadcastSpawn);
            }

            for (int i = 0; i < 3; i++)
            {
                AppendLaneSpawn(list, teamId, EEntityType.MinionRanged, $"{side}_Caster_{i + 1}",
                    broadcastSpawn);
            }

            if (AramMap.WaveHasSiege(waveIndex))
            {
                AppendLaneSpawn(list, teamId, EEntityType.MinionSiege, $"{side}_Siege", broadcastSpawn);
            }

            return list;
        }

        /// <summary>从中线门口出生，排成一列。</summary>
        public static void AppendLaneSpawn(
            List<Scene.PendingMinionSpawn> list, int teamId, int entityType, string name,
            bool broadcastSpawn)
        {
            AramMap.GetMinionSpawn(teamId, alongOffset: 0f, across: 0f, out float x, out float z);
            list.Add(new Scene.PendingMinionSpawn
            {
                TeamId = teamId,
                EntityType = entityType,
                Name = name,
                X = x,
                Z = z,
                LaneAcross = 0f,
                BroadcastSpawn = broadcastSpawn
            });
        }
    }
}
