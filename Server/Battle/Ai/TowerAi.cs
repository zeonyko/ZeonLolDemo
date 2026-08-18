using Shared;

namespace Server.Battle
{
    /// <summary>防御塔：锁住一个目标打。优先打刚打过友军英雄的人，再打小兵，再打英雄。</summary>
    public static class TowerAi
    {
        public static void Tick(Entity tower, Scene scene, float dt, float serverTime)
        {
            if (tower == null || !tower.IsAttackingStructure || tower.Hp <= 0f || scene == null)
                return;

            tower.SnapshotTimer -= dt;
            if (tower.SnapshotTimer > 0f)
                return;
            tower.SnapshotTimer = AramMap.TowerScanInterval;

            var target = ResolveTarget(tower, scene, serverTime);
            if (target == null) return;

            SkillResultService.TryUnitAttack(tower, target, tower.AttackSkillId, serverTime);
        }

        private static Entity ResolveTarget(Entity tower, Scene scene, float serverTime)
        {
            float range = AramMap.TowerAttackRange;
            if (tower.AggroTargetId != 0)
            {
                var locked = scene.GetEntity(tower.AggroTargetId);
                if (IsValidTarget(tower, locked, range))
                    return locked;
                tower.AggroTargetId = 0;
            }

            var next = FindTarget(tower, scene, range, serverTime);
            tower.AggroTargetId = next?.Id ?? 0;
            return next;
        }

        private static bool IsValidTarget(Entity tower, Entity target, float range)
        {
            if (target == null || target.Hp <= 0f) return false;
            if (!Entity.AreEnemies(tower, target)) return false;
            if (!target.IsMinion && !target.IsPlayer) return false;

            float dx = target.PosX - tower.PosX;
            float dz = target.PosZ - tower.PosZ;
            return dx * dx + dz * dz <= range * range;
        }

        private static Entity FindTarget(Entity tower, Scene scene, float range, float serverTime)
        {
            Entity bestCallHelp = null;
            Entity bestMinion = null;
            Entity bestPlayer = null;
            float bestCallSq = range * range;
            float bestMinionSq = range * range;
            float bestPlayerSq = range * range;
            float callWindow = AramMap.TowerCallForHelpWindow;

            foreach (var e in scene.GetAllEntities())
            {
                if (!Entity.AreEnemies(tower, e) || e.Hp <= 0f) continue;

                float dx = e.PosX - tower.PosX;
                float dz = e.PosZ - tower.PosZ;
                float sq = dx * dx + dz * dz;
                if (sq > range * range) continue;

                if (e.IsPlayer)
                {
                    bool calledHelp = serverTime - e.LastDamagedEnemyPlayerAt <= callWindow;
                    if (calledHelp && sq < bestCallSq)
                    {
                        bestCallSq = sq;
                        bestCallHelp = e;
                    }
                    if (sq < bestPlayerSq)
                    {
                        bestPlayerSq = sq;
                        bestPlayer = e;
                    }
                }
                else if (e.IsMinion && sq < bestMinionSq)
                {
                    bestMinionSq = sq;
                    bestMinion = e;
                }
            }

            return bestCallHelp ?? bestMinion ?? bestPlayer;
        }
    }
}
