using System;
using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>服务器管 Buff：挂上、刷新、叠层、持续掉血、到期摘掉。改完跟着属性一起发给客户端。</summary>
    public sealed class BuffService
    {
        public static BuffService Instance { get; } = new BuffService();

        /// <summary>某个单位身上一条正在生效的 Buff。</summary>
        private sealed class ActiveBuff
        {
            public int BuffId;
            public int StackCount = 1;
            /// <summary>到期时刻（服务器时间，秒）。</summary>
            public float ExpireAt;
            /// <summary>下次周期伤害时刻（服务器时间，秒）。</summary>
            public float NextTickAt;
            public long SourceEntityId;
            public int SourceSkillId;
            /// <summary>挂上时写入状态机的标记；到期要清掉。</summary>
            public EntityStateTag AppliedTags;
            /// <summary>减速幅度。</summary>
            public float SlowMagnitude;
            /// <summary>≥0 时覆盖配置里的周期伤害；默认 -1 用表。</summary>
            public float PeriodDamageOverride = -1f;
            /// <summary>&gt;0 时覆盖配置里的周期间隔。</summary>
            public float TickIntervalOverride;
        }

        /// <summary>单位 Id → 身上全部 Buff。</summary>
        private readonly Dictionary<long, List<ActiveBuff>> _byEntity =
            new Dictionary<long, List<ActiveBuff>>(64);

        /// <summary>周期伤害命中包用的 HitIndex，避开技能时间轴 Hit 下标。</summary>
        private const uint DotHitIndex = 9000;

        /// <summary>清空全部 Buff（对局结束用）。</summary>
        public void Clear() => _byEntity.Clear();

        /// <summary>清掉某单位身上全部 Buff（销毁时调用）。</summary>
        public void ClearEntity(long entityId)
        {
            if (!_byEntity.TryGetValue(entityId, out var list) || list == null) return;
            list.Clear();
            _byEntity.Remove(entityId);
        }

        /// <summary>导出当前 Buff 列表（剩余时长相对 now）。</summary>
        public BuffInstanceData[] Snapshot(long entityId, float now)
        {
            if (!_byEntity.TryGetValue(entityId, out var list) || list == null || list.Count == 0)
                return Array.Empty<BuffInstanceData>();

            var arr = new BuffInstanceData[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                float remain = b.ExpireAt - now;
                if (remain < 0f) remain = 0f;
                arr[i] = new BuffInstanceData
                {
                    BuffId = b.BuffId,
                    StackCount = b.StackCount,
                    RemainingDuration = remain,
                    SourceEntityId = b.SourceEntityId,
                    SourceSkillId = b.SourceSkillId
                };
            }
            return arr;
        }

        /// <summary>挂上或刷新 Buff。同 Id 会续时；叠层类型决定层数。刷新前要清掉旧状态标记。</summary>
        public bool Apply(
            Entity target,
            int buffId,
            Entity source,
            int skillId,
            float now,
            float chance = 1f,
            float periodDamageOverride = -1f,
            float tickIntervalOverride = 0f,
            float durationOverride = 0f)
        {
            if (target == null || buffId == 0) return false;
            if (chance < 1f && Random.Shared.NextDouble() > chance) return false;
            if (!BuffCatalog.TryGet(buffId, out var cfg) || cfg == null) return false;

            // 瞬时伤害模板不进持久列表
            float duration = durationOverride > 0.05f
                ? durationOverride
                : (cfg.DefaultDuration > 0f ? cfg.DefaultDuration : 1f);
            var tags = EntityStateMachine.StatusFlagsToTag(cfg.StatusFlags);
            float slowMag = ResolveSlowMagnitude(cfg);
            float tickInterval = tickIntervalOverride > 0f
                ? tickIntervalOverride
                : cfg.TickInterval;

            if (!_byEntity.TryGetValue(target.Id, out var list) || list == null)
            {
                list = new List<ActiveBuff>(4);
                _byEntity[target.Id] = list;
            }

            ActiveBuff existing = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].BuffId == buffId)
                {
                    existing = list[i];
                    break;
                }
            }

            if (existing != null)
            {
                // 刷新前清旧标记，避免状态时长和 Buff 到期对不上
                if (existing.AppliedTags != EntityStateTag.None)
                    target.State.ForceClearState(existing.AppliedTags);

                existing.ExpireAt = now + duration;
                existing.SourceEntityId = source?.Id ?? 0;
                existing.SourceSkillId = skillId;
                existing.AppliedTags = tags;
                existing.SlowMagnitude = slowMag;
                if (periodDamageOverride >= 0f)
                    existing.PeriodDamageOverride = periodDamageOverride;
                if (tickIntervalOverride > 0f)
                    existing.TickIntervalOverride = tickIntervalOverride;
                // 叠层：递增并限制在 MaxStacks；否则重置为 1
                if (cfg.BuffStackType == BuffStackType.AddStack)
                {
                    existing.StackCount++;
                    if (cfg.MaxStacks > 0 && existing.StackCount > cfg.MaxStacks)
                        existing.StackCount = cfg.MaxStacks;
                }
                else
                    existing.StackCount = 1;

                // 刷新只延长持续时间，不重置跳伤节奏，避免最后一段时间空转不掉血
                ApplyTags(target, tags, now, duration, slowMag);
                PushEntityData(target);
                return true;
            }

            var buff = new ActiveBuff
            {
                BuffId = buffId,
                StackCount = 1,
                ExpireAt = now + duration,
                SourceEntityId = source?.Id ?? 0,
                SourceSkillId = skillId,
                AppliedTags = tags,
                SlowMagnitude = slowMag,
                PeriodDamageOverride = periodDamageOverride,
                TickIntervalOverride = tickIntervalOverride > 0f ? tickIntervalOverride : 0f,
                NextTickAt = tickInterval > 0f ? now + tickInterval : float.MaxValue
            };
            list.Add(buff);
            ApplyTags(target, tags, now, duration, slowMag);
            PushEntityData(target);
            return true;
        }

        /// <summary>按命中结果批量挂 Buff。木桩跳过定身/眩晕，击飞仍可挂。</summary>
        public void ApplyPayloads(
            Entity caster,
            Entity recipient,
            BuffApplySpec[] buffs,
            int skillId,
            float now)
        {
            if (buffs == null || buffs.Length == 0 || recipient == null || caster == null)
                return;

            int statusMask = (int)recipient.State.CurrentStateMask;
            var matched = new List<BuffApplySpec>(buffs.Length);
            BuffApplyFilter.CollectMatching(
                buffs,
                caster.Id, caster.TeamId,
                recipient.Id, recipient.TeamId,
                statusMask,
                matched);

            for (int i = 0; i < matched.Count; i++)
            {
                var b = matched[i];
                // 木桩仍豁免定身/眩晕，但允许击飞以便演练可见
                if (recipient.IsTrainingDummy)
                {
                    if (BuffCatalog.TryGet(b.BuffId, out var cfg) && cfg != null)
                    {
                        int hard = SkillEffectFlags.Root | SkillEffectFlags.Stun;
                        if ((cfg.StatusFlags & hard) != 0)
                            continue;
                    }
                }
                Apply(
                    recipient, b.BuffId, caster, skillId, now, b.Chance,
                    durationOverride: b.OverrideDuration);
            }
        }

        /// <summary>每帧：先打周期伤，再摘到期 Buff。周期伤按层数叠乘；卡帧时用 while 追赶。</summary>
        public void Tick(float now, float dt)
        {
            if (_byEntity.Count == 0) return;

            // 先拷贝 Keys，避免边遍历边删改字典
            var entityIds = new List<long>(_byEntity.Keys);
            for (int e = 0; e < entityIds.Count; e++)
            {
                long id = entityIds[e];
                if (!_byEntity.TryGetValue(id, out var list) || list == null || list.Count == 0)
                    continue;

                var scene = WorldManager.Instance?.GetDefaultScene();
                var entity = scene?.GetEntity(id);
                if (entity == null || entity.Hp <= 0f)
                {
                    _byEntity.Remove(id);
                    continue;
                }

                bool dirty = false;
                // 倒序遍历，到期可安全 RemoveAt
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var buff = list[i];
                    if (!BuffCatalog.TryGet(buff.BuffId, out var cfg) || cfg == null)
                    {
                        list.RemoveAt(i);
                        dirty = true;
                        continue;
                    }

                    float tickInterval = buff.TickIntervalOverride > 0f
                        ? buff.TickIntervalOverride
                        : cfg.TickInterval;
                    float periodDmg = buff.PeriodDamageOverride >= 0f
                        ? buff.PeriodDamageOverride
                        : cfg.PeriodDamage;

                    // 周期伤害：在持续时间内按节奏结算，并广播命中以便飘字
                    if (tickInterval > 0f && periodDmg > 0f)
                    {
                        while (now + 0.0001f >= buff.NextTickAt
                               && buff.NextTickAt <= buff.ExpireAt + 0.0001f)
                        {
                            float dmg = periodDmg * Math.Max(1, buff.StackCount);
                            var caster = buff.SourceEntityId != 0
                                ? scene?.GetEntity(buff.SourceEntityId)
                                : null;
                            var hit = DamageSystem.Apply(
                                caster,
                                entity,
                                buff.SourceSkillId,
                                new DamagePayload
                                {
                                    BaseDamage = dmg,
                                    Coefficient = 1f,
                                    DamageType = (int)DamageType.True
                                },
                                AttackStyles.Unit);

                            if (caster != null && hit.TargetId != 0)
                                SkillHitResolver.BroadcastHit(
                                    caster, buff.SourceSkillId, DotHitIndex, new[] { hit });

                            dirty = true;
                            buff.NextTickAt += tickInterval;

                            if (entity.Hp <= 0f)
                            {
                                dirty = false;
                                list = null;
                                break;
                            }
                        }

                        if (list == null)
                            break;
                    }

                    // 未到期则保留
                    if (now + 0.0001f < buff.ExpireAt)
                        continue;

                    // 到期必须清掉状态标记，否则控制/减速会残留
                    if (buff.AppliedTags != EntityStateTag.None)
                        entity.State.ForceClearState(buff.AppliedTags);

                    list.RemoveAt(i);
                    dirty = true;
                }

                if (list == null || list.Count == 0)
                    _byEntity.Remove(id);

                if (dirty)
                    PushEntityData(entity);
            }
        }

        /// <summary>有改动的单位推一次血量/属性（含 Buff 列表）。</summary>
        static void PushEntityData(Entity entity)
        {
            if (entity == null) return;
            SkillResultService.BroadcastHpSync(entity);
        }

        /// <summary>把 Buff 状态标记限时写进状态机。</summary>
        static void ApplyTags(Entity target, EntityStateTag tags, float now, float duration, float slowMag)
        {
            if (target == null || tags == EntityStateTag.None) return;
            target.State.AddTimedState(tags, now, duration, slowMag);
        }

        /// <summary>从属性修正里取最大减速幅度（移速百分比减）。</summary>
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

        /// <summary>按身上防御修正缩放即将扣的血。百分比减免上限 85%，再减固定值。</summary>
        public float ScaleIncomingDamage(Entity target, float damage)
        {
            if (target == null || damage <= 0f) return damage;
            if (!_byEntity.TryGetValue(target.Id, out var list) || list == null || list.Count == 0)
                return damage;

            float add = 0f;
            float percent = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var buff = list[i];
                if (!BuffCatalog.TryGet(buff.BuffId, out var cfg) || cfg?.AttributeModifiers == null)
                    continue;
                int stacks = buff.StackCount > 0 ? buff.StackCount : 1;
                for (int m = 0; m < cfg.AttributeModifiers.Length; m++)
                {
                    var mod = cfg.AttributeModifiers[m];
                    if (mod == null || mod.Attribute != AttributeType.Defense) continue;
                    float v = mod.Value * stacks;
                    if (mod.Operation == ModifyType.PercentAdd)
                        percent += v;
                    else
                        add += v;
                }
            }

            if (percent > 0.85f) percent = 0.85f;
            else if (percent < 0f) percent = 0f;

            float scaled = damage * (1f - percent) - add;
            return scaled > 0f ? scaled : 0f;
        }
    }
}
