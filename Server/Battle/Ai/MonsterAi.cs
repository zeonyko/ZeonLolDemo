using System;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>怪物：巡逻、追打、跑太远就回家回血。</summary>
    public static class MonsterAi
    {
        private static readonly Random Rng = new Random();

        public static void Tick(Entity monster, Scene scene, float dt, float serverTime)
        {
            if (monster == null || !monster.IsMonster || monster.Hp <= 0f)
                return;
            if (monster.State.HasState(EntityStateTag.Root | EntityStateTag.Stun | EntityStateTag.Airborne))
            {
                SkillResultService.TryBroadcastUnitSnapshot(monster, dt);
                return;
            }

            switch (monster.AiState)
            {
                case EMonsterAiState.Patrol:
                    TickPatrol(monster, scene, dt);
                    break;
                case EMonsterAiState.Chase:
                    TickChase(monster, scene, dt, serverTime);
                    break;
                case EMonsterAiState.Return:
                    TickReturn(monster, dt);
                    break;
            }

            SkillResultService.TryBroadcastUnitSnapshot(monster, dt);
        }

        private static void TickPatrol(Entity monster, Scene scene, float dt)
        {
            var target = FindNearestPlayer(monster, scene, GameConstants.MonsterAggroRange);
            if (target != null)
            {
                monster.AiState = EMonsterAiState.Chase;
                monster.AggroTargetId = target.Id;
                return;
            }

            monster.PatrolRetargetTimer -= dt;
            if (monster.PatrolRetargetTimer <= 0f)
            {
                PickPatrolPoint(monster);
                monster.PatrolRetargetTimer = 2.5f + (float)Rng.NextDouble() * 2f;
            }

            MoveToward(monster, monster.PatrolTargetX, monster.PatrolTargetZ, dt);
        }

        private static void TickChase(Entity monster, Scene scene, float dt, float serverTime)
        {
            if (monster.DistToSpawnSq() > GameConstants.MonsterLeashRange * GameConstants.MonsterLeashRange)
            {
                EnterReturn(monster);
                return;
            }

            var target = scene.GetEntity(monster.AggroTargetId);
            if (target == null || !target.IsPlayer || target.Hp <= 0f)
                target = FindNearestPlayer(monster, scene, GameConstants.MonsterAggroRange);

            if (target == null)
            {
                EnterReturn(monster);
                return;
            }

            float tdx = target.PosX - monster.SpawnX;
            float tdz = target.PosZ - monster.SpawnZ;
            float leash = GameConstants.MonsterLeashRange;
            if (tdx * tdx + tdz * tdz > leash * leash)
            {
                EnterReturn(monster);
                return;
            }

            monster.AggroTargetId = target.Id;

            float dist = MovementSimulator.Distance3D(
                monster.PosX, monster.PosY, monster.PosZ,
                target.PosX, target.PosY, target.PosZ);

            int atkSkill = monster.AttackSkillId > 0
                ? monster.AttackSkillId
                : UnitCatalog.ResolveAttackSkillId(monster.EntityType);
            if (dist <= SkillRules.GetRange(atkSkill))
                SkillResultService.TryUnitAttack(monster, target, atkSkill, serverTime);
            else
                MoveToward(monster, target.PosX, target.PosZ, dt);
        }

        private static void TickReturn(Entity monster, float dt)
        {
            if (monster.Hp < monster.MaxHp)
            {
                float before = monster.Hp;
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + GameConstants.MonsterHpRegenPerSec * dt);
                if (monster.Hp - before >= 5f || monster.Hp >= monster.MaxHp)
                    SkillResultService.BroadcastHpSync(monster);
            }

            if (monster.DistToSpawnSq() <= 0.25f)
            {
                monster.PosX = monster.SpawnX;
                monster.PosZ = monster.SpawnZ;
                monster.Hp = monster.MaxHp;
                monster.AiState = EMonsterAiState.Patrol;
                monster.AggroTargetId = 0;
                PickPatrolPoint(monster);
                SkillResultService.BroadcastHpSync(monster);
                return;
            }

            MoveToward(monster, monster.SpawnX, monster.SpawnZ, dt);
        }

        private static void EnterReturn(Entity monster)
        {
            monster.AiState = EMonsterAiState.Return;
            monster.AggroTargetId = 0;
        }

        private static void PickPatrolPoint(Entity monster)
        {
            float angle = (float)(Rng.NextDouble() * Math.PI * 2);
            float radius = (float)Rng.NextDouble() * GameConstants.MonsterPatrolRadius;
            monster.PatrolTargetX = monster.SpawnX + MathF.Cos(angle) * radius;
            monster.PatrolTargetZ = monster.SpawnZ + MathF.Sin(angle) * radius;
        }

        private static void MoveToward(Entity monster, float tx, float tz, float dt)
        {
            float dx = tx - monster.PosX;
            float dz = tz - monster.PosZ;
            float mag = MathF.Sqrt(dx * dx + dz * dz);
            if (mag < 0.05f) return;

            float step = GameConstants.MonsterMoveSpeed * monster.State.MoveSpeedMultiplier * dt;
            if (step >= mag)
            {
                monster.PosX = tx;
                monster.PosZ = tz;
            }
            else
            {
                monster.PosX += dx / mag * step;
                monster.PosZ += dz / mag * step;
            }

            monster.PosY = GameConstants.GroundY;
            monster.MoveState.IsGrounded = true;
            monster.MoveState.VelY = 0f;
        }

        private static Entity FindNearestPlayer(Entity monster, Scene scene, float range)
        {
            Entity best = null;
            float bestSq = range * range;

            foreach (var e in scene.GetAllEntities())
            {
                if (!e.IsPlayer || e.Hp <= 0f) continue;
                float dx = e.PosX - monster.PosX;
                float dz = e.PosZ - monster.PosZ;
                float sq = dx * dx + dz * dz;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = e;
                }
            }

            return best;
        }

    }
}
