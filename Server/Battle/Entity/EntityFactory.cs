using Shared;

namespace Server.Battle
{
    /// <summary>创建单位。普攻和血量以单位配置表为准。</summary>
    public static class EntityFactory
    {
        public static Entity CreatePlayer(long id, string name, int teamId)
        {
            AramMap.GetPlayerSpawn(teamId, out float x, out float z);
            var e = new Entity
            {
                Id = id,
                Name = name,
                EntityType = EEntityType.Player,
                TeamId = teamId,
                MoveState = MovementState.FromPosition(x, GameConstants.GroundY, z),
                SpawnX = x,
                SpawnZ = z
            };
            ApplyUnitProfile(e);
            return e;
        }

        public static Entity CreateStructure(long id, int teamId, int entityType, string name)
        {
            float x, z;
            if (entityType == EEntityType.Crystal)
                AramMap.GetCrystalPos(teamId, out x, out z);
            else if (entityType == EEntityType.Barracks)
                AramMap.GetBarracksPos(teamId, out x, out z);
            else
                AramMap.GetTowerPos(teamId, out x, out z);

            var e = new Entity
            {
                Id = id,
                Name = name,
                EntityType = entityType,
                TeamId = teamId,
                SpawnX = x,
                SpawnZ = z,
                MoveState = MovementState.FromPosition(x, GameConstants.GroundY, z)
            };
            ApplyUnitProfile(e);
            return e;
        }

        public static Entity CreateMinion(
            long id, int teamId, int entityType, string name, float x, float z)
        {
            var e = new Entity
            {
                Id = id,
                Name = name,
                EntityType = entityType,
                TeamId = teamId,
                SpawnX = x,
                SpawnZ = z,
                MoveState = MovementState.FromPosition(x, GameConstants.GroundY, z),
                AiState = EMonsterAiState.Chase,
                LanePathIndex = AramMap.ResolveLanePathIndex(teamId, x, z)
            };
            ApplyUnitProfile(e);
            return e;
        }

        public static Entity CreateTrainingDummy(long id, int ownerTeamId, float x, float z, string name)
        {
            var e = new Entity
            {
                Id = id,
                Name = name,
                EntityType = EEntityType.Monster,
                TeamId = ETeamId.EnemyOf(ownerTeamId),
                IsTrainingDummy = true,
                SpawnX = x,
                SpawnZ = z,
                MoveState = MovementState.FromPosition(x, GameConstants.GroundY, z),
                AiState = EMonsterAiState.Patrol
            };
            ApplyUnitProfile(e);
            // 木桩：普攻跟怪物表，血量用木桩固定值
            e.MaxHp = GameConstants.TrainingDummyHp;
            e.Hp = GameConstants.TrainingDummyHp;
            return e;
        }

        static void ApplyUnitProfile(Entity e)
        {
            if (e == null) return;
            e.AttackSkillId = UnitCatalog.ResolveAttackSkillId(e.EntityType);
            float hp = UnitProfileHp.Resolve(e.EntityType);
            e.MaxHp = hp;
            e.Hp = hp;
        }

        public static int PickBalancedTeam(Scene scene)
        {
            int blue = 0;
            int red = 0;
            if (scene != null)
            {
                foreach (var ent in scene.GetAllEntities())
                {
                    if (ent == null || !ent.IsPlayer) continue;
                    if (ent.TeamId == ETeamId.Blue) blue++;
                    else if (ent.TeamId == ETeamId.Red) red++;
                }
            }

            if (blue <= red) return ETeamId.Blue;
            return ETeamId.Red;
        }
    }
}
