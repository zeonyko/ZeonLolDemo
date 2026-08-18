using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗数据中心：实体当前状态只从这里写和查。外观创建/销毁由脏标记驱动。</summary>
    public class BattleCache
    {
        #region 全局一份
        public static BattleCache Instance { get; private set; }

        public static BattleCache Create()
        {
            Instance = new BattleCache();
            return Instance;
        }

        public void Clear()
        {
            _entities.Clear();
            _viewUpsertDirty.Clear();
            _viewRemoveDirty.Clear();
        }

        public void Shutdown()
        {
            Clear();
            if (Instance == this)
                Instance = null;
        }
        #endregion

        #region 数据与“要刷新外观”标记
        private readonly Dictionary<long, EntityData> _entities = new Dictionary<long, EntityData>();
        private readonly HashSet<long> _viewUpsertDirty = new HashSet<long>();
        private readonly HashSet<long> _viewRemoveDirty = new HashSet<long>();
        #endregion

        #region 只读查询
        public bool TryGet(long id, out EntityData data) => _entities.TryGetValue(id, out data);

        public float GetHp(long id) => TryGet(id, out var d) ? d.Hp : 0f;

        public float GetMaxHp(long id) => TryGet(id, out var d) ? d.MaxHp : 0f;

        public IReadOnlyDictionary<long, EntityData> GetAll() => _entities;
        #endregion

        #region 取出要刷新的外观
        /// <summary>取出并清空外观脏标记：先删再增改。</summary>
        public void TakeViewDirty(List<long> upsertIds, List<long> removeIds)
        {
            upsertIds.Clear();
            removeIds.Clear();

            foreach (long id in _viewRemoveDirty)
                removeIds.Add(id);
            _viewRemoveDirty.Clear();

            foreach (long id in _viewUpsertDirty)
            {
                if (_entities.ContainsKey(id))
                    upsertIds.Add(id);
            }
            _viewUpsertDirty.Clear();
        }
        #endregion

        #region 写入实体当前状态
        /// <summary>写入/合并实体当前状态。markViewDirty=false 表示只改数据、不马上刷外观（本地即将自己创建时用）。</summary>
        public void ApplyEntity(EntityData src, bool markViewDirty = true)
        {
            if (src == null || src.Id == 0) return;

            var next = Clone(src);
            next.Name = ResolveEntityName(next);

            bool existed = MergeWithPrevious(next, out float oldHp, out float oldMaxHp, out string oldName);

            _entities[next.Id] = next;
            _viewRemoveDirty.Remove(next.Id);
            if (markViewDirty)
                _viewUpsertDirty.Add(next.Id);

            SyncEntityView(next, existed, oldHp, oldMaxHp, oldName);
        }

        /// <summary>用旧状态补全缺的血量，并记下旧值方便通知 UI。</summary>
        private bool MergeWithPrevious(EntityData next, out float oldHp, out float oldMaxHp, out string oldName)
        {
            oldHp = 0f;
            oldMaxHp = 0f;
            oldName = null;

            bool existed = _entities.TryGetValue(next.Id, out var prev);
            if (existed && prev != null)
            {
                oldHp = prev.Hp;
                oldMaxHp = prev.MaxHp;
                oldName = prev.Name;
                if (next.MaxHp <= 0f)
                {
                    next.MaxHp = prev.MaxHp;
                    next.Hp = prev.Hp;
                }
            }
            else if (next.MaxHp <= 0f)
            {
                next.MaxHp = GameConstants.DefaultMaxHp;
                if (next.Hp < 0f)
                    next.Hp = next.MaxHp;
            }

            return existed;
        }

        /// <summary>把最新状态同步到已有实体：改名、血量变化、Buff 当前状态。</summary>
        private void SyncEntityView(EntityData next, bool existed, float oldHp, float oldMaxHp, string oldName)
        {
            var entity = EntityManager.Instance?.GetEntity(next.Id);
            if (entity == null) return;

            if (!string.IsNullOrEmpty(next.Name) && next.Name != entity.Name)
                entity.Name = next.Name;

            if (!string.IsNullOrEmpty(next.Name) && next.Name != oldName)
                entity.DispatchEvent("OnDisplayNameChanged", next.Name);

            if (existed)
            {
                if (Mathf.Abs(oldMaxHp - next.MaxHp) >= 0.0001f)
                {
                    entity.DispatchEvent("OnAttrChanged", new AttrChange
                    {
                        Id = EAttrId.MaxHp,
                        OldValue = oldMaxHp,
                        NewValue = next.MaxHp
                    });
                }

                if (Mathf.Abs(oldHp - next.Hp) >= 0.0001f)
                {
                    entity.DispatchEvent("OnAttrChanged", new AttrChange
                    {
                        Id = EAttrId.Hp,
                        OldValue = oldHp,
                        NewValue = next.Hp
                    });
                }
            }

            // Buff 当前状态：出生/属性同步时一起写入
            entity.GetComponent<BuffComponent>()?.ApplySnapshot(next.Buffs);
        }
        #endregion

        #region 本地种子 / 属性写入
        /// <summary>本地先记一份数据：不覆盖已有状态，不标外观脏（调用方自己创建实体）。</summary>
        public void EnsureSeed(long id, string name, float hp, float maxHp, int teamId = 0, int entityType = 0)
        {
            if (id == 0 || _entities.ContainsKey(id)) return;

            float resolvedMax = maxHp > 0f ? maxHp : GameConstants.DefaultMaxHp;
            float resolvedHp = hp >= 0f ? hp : resolvedMax;
            ApplyEntity(new EntityData
            {
                Id = id,
                Name = name,
                Hp = resolvedHp,
                MaxHp = resolvedMax,
                TeamId = teamId,
                EntityType = entityType
            }, markViewDirty: false);
        }

        public void SetHp(long entityId, float hp)
        {
            if (entityId == 0 || !_entities.TryGetValue(entityId, out var data))
                return;

            float max = data.MaxHp;
            float newHp = hp;
            if (max > 0f)
            {
                if (newHp < 0f) newHp = 0f;
                else if (newHp > max) newHp = max;
            }
            else if (newHp < 0f)
                newHp = 0f;

            float old = data.Hp;
            if (Mathf.Abs(old - newHp) < 0.0001f)
                return;

            data.Hp = newHp;

            EntityManager.Instance?.GetEntity(entityId)?.DispatchEvent("OnAttrChanged", new AttrChange
            {
                Id = EAttrId.Hp,
                OldValue = old,
                NewValue = newHp
            });
        }
        #endregion

        #region 移除实体
        public void RemoveEntity(long entityId)
        {
            if (entityId == 0) return;

            _entities.Remove(entityId);
            _viewUpsertDirty.Remove(entityId);
            _viewRemoveDirty.Add(entityId);
        }

        /// <summary>只清数据，不排队删外观（剧情重开前重置用）。</summary>
        public void Discard(long entityId)
        {
            if (entityId == 0) return;
            _entities.Remove(entityId);
            _viewUpsertDirty.Remove(entityId);
            _viewRemoveDirty.Remove(entityId);
        }
        #endregion

        #region 工具方法：命名解析与深拷贝
        public static string ResolveEntityName(EntityData data)
        {
            if (data == null) return "";
            if (!string.IsNullOrEmpty(data.Name))
                return data.Name;

            return data.EntityType switch
            {
                EEntityType.MinionMelee => $"Melee_{data.Id}",
                EEntityType.MinionRanged => $"Caster_{data.Id}",
                EEntityType.MinionSiege => $"Siege_{data.Id}",
                EEntityType.MinionSuper => $"Super_{data.Id}",
                EEntityType.Tower => $"Tower_{data.Id}",
                EEntityType.Barracks => $"InnerTower_{data.Id}",
                EEntityType.Crystal => $"Crystal_{data.Id}",
                EEntityType.Monster => $"Goblin_{data.Id}",
                EEntityType.Boss => $"Boss_{data.Id}",
                _ => $"Player_{data.Id}"
            };
        }

        private static EntityData Clone(EntityData src)
        {
            return new EntityData
            {
                Id = src.Id,
                Name = src.Name,
                PosX = src.PosX,
                PosY = src.PosY,
                PosZ = src.PosZ,
                Hp = src.Hp,
                MaxHp = src.MaxHp,
                EntityType = src.EntityType,
                TeamId = src.TeamId,
                Buffs = CloneBuffs(src.Buffs)
            };
        }

        private static BuffInstanceData[] CloneBuffs(BuffInstanceData[] src)
        {
            if (src == null || src.Length == 0)
                return System.Array.Empty<BuffInstanceData>();
            var dst = new BuffInstanceData[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var b = src[i];
                dst[i] = b == null
                    ? new BuffInstanceData()
                    : new BuffInstanceData
                    {
                        BuffId = b.BuffId,
                        StackCount = b.StackCount,
                        RemainingDuration = b.RemainingDuration,
                        SourceEntityId = b.SourceEntityId,
                        SourceSkillId = b.SourceSkillId
                    };
            }
            return dst;
        }
        #endregion
    }
}
