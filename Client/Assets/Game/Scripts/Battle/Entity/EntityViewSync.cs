using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>按数据中心的脏标记创建或销毁画面上的实体。</summary>
    public static class EntityViewSync
    {
        #region 脏标记缓冲区
        private static readonly List<long> UpsertBuf = new List<long>(64); // 本帧要创建/更新的 Id
        private static readonly List<long> RemoveBuf = new List<long>(32); // 本帧要移除的 Id
        #endregion

        #region 脏标记处理入口
        /// <summary>处理积下来的外观脏标记：先删，再按数据创建。</summary>
        public static void Flush()
        {
            var cache = BattleCache.Instance;
            if (cache == null) return;

            cache.TakeViewDirty(UpsertBuf, RemoveBuf);

            for (int i = 0; i < RemoveBuf.Count; i++)
                Despawn(RemoveBuf[i]);

            for (int i = 0; i < UpsertBuf.Count; i++)
                TrySpawnFromCache(UpsertBuf[i]);
        }
        #endregion

        #region 创建判断与生成
        /// <summary>以后可按类型/距离过滤；自己始终创建。</summary>
        public static bool ShouldCreate(EntityData data)
        {
            if (data == null || data.Id == 0) return false;

            long localId = BattleSystem.Instance?.LocalPlayerId ?? 0;
            if (data.Id == localId)
                return true;

            return true;
        }

        /// <summary>按缓存数据生成实体。自己走替换，别人只在还没有时创建。</summary>
        public static void TrySpawnFromCache(long entityId)
        {
            var cache = BattleCache.Instance;
            if (cache == null || !cache.TryGet(entityId, out var data))
                return;

            if (!ShouldCreate(data))
                return;

            var em = EntityManager.Instance;
            if (em == null) return;

            long localId = BattleSystem.Instance?.LocalPlayerId ?? 0;
            var existing = em.GetEntity(data.Id);

            if (data.Id == localId)
            {
                if (existing != null && existing.IsLocalPlayer)
                    return;

                if (existing != null)
                    em.RemoveEntity(data.Id);

                EntityFactory.CreateLocalPlayer(data);
                return;
            }

            if (existing != null)
                return;

            EntityFactory.CreateEntity(data);
        }
        #endregion

        #region 死亡与移除
        /// <summary>死亡：小兵/建筑/野怪播完动画后移除；玩家留下等复活。</summary>
        public static void HandleDead(long entityId)
        {
            if (entityId == 0) return;

            var entity = EntityManager.Instance?.GetEntity(entityId);
            if (entity == null)
            {
                BattleCache.Instance?.Discard(entityId);
                return;
            }

            bool isPlayer = IsPlayerEntity(entityId) || entity.IsLocalPlayer;
            entity.GetComponent<StateComponent>()?.ClearAll();

            if (isPlayer)
                HandlePlayerDeath(entity);
            else
                HandleNpcDeath(entityId, entity);
        }

        // 玩家留下等复活，但仍播死亡动画
        private static void HandlePlayerDeath(Entity entity)
        {
            if (entity.IsLocalPlayer)
            {
                var session = BattleSystem.Instance?.Session;
                if (session != null)
                {
                    session.LocalPlayerDead = true;
                    float delay = GameConstants.CalcPlayerRespawnDelay(session.MatchElapsed);
                    session.LocalRespawnEndsAt = UnityEngine.Time.time + delay;
                }
            }

            entity.GetComponent<CombatViewComponent>()?.PlayDeath(null);
        }

        // 小兵/野怪/建筑：播完死亡动画后清数据并移除
        private static void HandleNpcDeath(long entityId, Entity entity)
        {
            void Finish()
            {
                BattleCache.Instance?.Discard(entityId);
                EntityManager.Instance?.RemoveEntity(entityId);
            }

            var combatView = entity.GetComponent<CombatViewComponent>();
            if (combatView != null)
                combatView.PlayDeath(Finish);
            else
                Finish();
        }

        private static bool IsPlayerEntity(long entityId)
        {
            if (BattleCache.Instance == null
                || !BattleCache.Instance.TryGet(entityId, out var data)
                || data == null)
                return false;

            return data.EntityType == EEntityType.Player;
        }

        /// <summary>直接移除。自己永远不会走这条路。</summary>
        public static void Despawn(long entityId)
        {
            long localId = BattleSystem.Instance?.LocalPlayerId ?? 0;
            if (entityId == 0 || entityId == localId) return;
            EntityManager.Instance?.RemoveEntity(entityId);
        }
        #endregion

        #region 剧情演员生成
        /// <summary>剧情/演示用演员，不跟服务器对齐位置。</summary>
        public static Entity SpawnStoryActor(EntityData data, Color color)
        {
            return EntityFactory.CreateStoryActor(data, color);
        }
        #endregion
    }
}
