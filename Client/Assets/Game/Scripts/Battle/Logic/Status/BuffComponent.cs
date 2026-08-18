using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>实体身上的 Buff：以数据中心为准，本地倒计时，驱动状态和外观。</summary>
    public class BuffComponent : Component
    {
        /// <summary>单个 Buff 的本地记录（倒计时/去重，不以这里为准）。</summary>
        private sealed class LocalBuff
        {
            public int BuffId;                 // Buff 配置 Id
            public int StackCount;              // 当前叠加层数
            public float Remaining;             // 本地剩余时间，每帧倒计时
            public long SourceEntityId;         // 施加者实体 Id
            public int SourceSkillId;           // 施加来源技能 Id
            public EntityStateTag AppliedTags;  // 这个 Buff 写进状态的标记，移除时要还回去
        }

        private readonly List<LocalBuff> _active = new List<LocalBuff>(4);
        private StateComponent _state;
        private StatusFxComponent _statusFx;

        #region 生命周期
        public override void OnAwake()
        {
            _state = Owner.GetComponent<StateComponent>();
            _statusFx = Owner.GetComponent<StatusFxComponent>();
        }

        public override void OnStart()
        {
            // 出生时数据中心已有 Buff，立刻对齐
            if (BattleCache.Instance != null
                && BattleCache.Instance.TryGet(Owner.Id, out var data)
                && data.Buffs != null
                && data.Buffs.Length > 0)
            {
                ApplySnapshot(data.Buffs);
            }
        }

        public override void OnUpdate(float dt)
        {
            if (_active.Count == 0 || dt <= 0f) return;

            bool removed = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var b = _active[i];
                b.Remaining -= dt;
                if (b.Remaining > 0.0001f) continue;

                ClearTags(b.AppliedTags);
                _active.RemoveAt(i);
                removed = true;
            }

            if (removed)
                NotifyStatusFx();
        }
        #endregion

        #region 跟服务器当前状态对齐
        /// <summary>用服务器当前 Buff 覆盖本地；属性同步/出生写入后调用。</summary>
        public void ApplySnapshot(BuffInstanceData[] snapshot)
        {
            snapshot ??= System.Array.Empty<BuffInstanceData>();

            var nextIds = CollectSnapshotIds(snapshot);
            bool changed = RemoveBuffsNotInSnapshot(nextIds);
            changed |= UpsertBuffsFromSnapshot(snapshot);

            if (changed)
                NotifyStatusFx();
        }

        /// <summary>服务器这份 Buff 列表里有哪些编号，用来判断本地该删哪些。</summary>
        static HashSet<int> CollectSnapshotIds(BuffInstanceData[] snapshot)
        {
            var nextIds = new HashSet<int>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != null && snapshot[i].BuffId != 0)
                    nextIds.Add(snapshot[i].BuffId);
            }
            return nextIds;
        }

        /// <summary>清掉服务器当前状态里已经没有的本地 Buff，并还回状态标记。</summary>
        bool RemoveBuffsNotInSnapshot(HashSet<int> nextIds)
        {
            bool changed = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (nextIds.Contains(_active[i].BuffId)) continue;
                ClearTags(_active[i].AppliedTags);
                _active.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        /// <summary>按服务器列表：没有的补上，已有的刷新时间和层数。</summary>
        bool UpsertBuffsFromSnapshot(BuffInstanceData[] snapshot)
        {
            bool changed = false;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var s = snapshot[i];
                if (s == null || s.BuffId == 0) continue;

                var local = Find(s.BuffId);
                if (local == null)
                {
                    AddLocal(s);
                    changed = true;
                }
                else
                {
                    bool refresh = Mathf.Abs(local.Remaining - s.RemainingDuration) > 0.05f
                                   || local.StackCount != s.StackCount;
                    local.Remaining = s.RemainingDuration;
                    local.StackCount = s.StackCount > 0 ? s.StackCount : 1;
                    local.SourceEntityId = s.SourceEntityId;
                    local.SourceSkillId = s.SourceSkillId;
                    if (refresh)
                    {
                        ClearTags(local.AppliedTags);
                        local.AppliedTags = ApplyTagsFromConfig(s.BuffId, s.RemainingDuration);
                        changed = true;
                    }
                }
            }
            return changed;
        }
        #endregion

        #region 状态查询
        public void CollectActiveBuffIds(List<int> into)
        {
            if (into == null) return;
            into.Clear();
            for (int i = 0; i < _active.Count; i++)
                into.Add(_active[i].BuffId);
        }

        public bool HasBuff(int buffId)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].BuffId == buffId) return true;
            }
            return false;
        }

        public float GetRemaining(int buffId)
        {
            var b = Find(buffId);
            return b != null ? b.Remaining : 0f;
        }

        LocalBuff Find(int buffId)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].BuffId == buffId) return _active[i];
            }
            return null;
        }
        #endregion

        #region 内部辅助：Buff 增删与 State Tag 联动
        void NotifyStatusFx()
        {
            if (_statusFx == null) return;
            var ids = new List<int>(_active.Count);
            CollectActiveBuffIds(ids);
            _statusFx.SyncFromBuffs(ids);
        }

        void AddLocal(BuffInstanceData s)
        {
            var tags = ApplyTagsFromConfig(s.BuffId, s.RemainingDuration);
            _active.Add(new LocalBuff
            {
                BuffId = s.BuffId,
                StackCount = s.StackCount > 0 ? s.StackCount : 1,
                Remaining = s.RemainingDuration,
                SourceEntityId = s.SourceEntityId,
                SourceSkillId = s.SourceSkillId,
                AppliedTags = tags
            });
        }

        /// <summary>按 Buff 配置把状态位写入 State 组件，返回写入的 Tag 供后续回收。</summary>
        EntityStateTag ApplyTagsFromConfig(int buffId, float duration)
        {
            if (_state == null) return EntityStateTag.None;
            if (!BuffCatalog.TryGet(buffId, out var cfg) || cfg == null)
                return EntityStateTag.None;

            float dur = duration > 0f ? duration : cfg.DefaultDuration;
            float slowMag = ResolveSlowMagnitude(cfg);
            _state.ApplyStatusFlags(cfg.StatusFlags, dur, slowMag);
            return EntityStateMachine.StatusFlagsToTag(cfg.StatusFlags);
        }

        void ClearTags(EntityStateTag tags)
        {
            if (_state == null || tags == EntityStateTag.None) return;
            _state.ForceClearState(tags);
        }

        /// <summary>从属性修饰符里取最大的减速百分比，用于减速类 Buff 的 Tag 附带参数。</summary>
        static float ResolveSlowMagnitude(BuffConfig cfg)
        {
            if (cfg?.AttributeModifiers == null) return 0f;
            float mag = 0f;
            for (int i = 0; i < cfg.AttributeModifiers.Length; i++)
            {
                var m = cfg.AttributeModifiers[i];
                if (m == null) continue;
                if (m.Attribute != AttributeType.MoveSpeed) continue;
                if (m.Operation == ModifyType.PercentAdd && m.Value < 0f)
                {
                    float v = -m.Value;
                    if (v > mag) mag = v;
                }
            }
            return mag;
        }
        #endregion
    }
}
