using System;

namespace Shared
{
    /// <summary>单位当前状态（眩晕、沉默等）。和配表里的状态编号不是同一套，要用转换函数。</summary>
    [Flags]
    public enum EntityStateTag
    {
        None = 0,
        Root = 1 << 0,
        Stun = 1 << 1,
        Silence = 1 << 2,
        Disarm = 1 << 3,
        Slow = 1 << 4,
        Invincible = 1 << 5,
        Unstoppable = 1 << 6,
        Untargetable = 1 << 7,
        Casting = 1 << 8,
        Blind = 1 << 9,
        Airborne = 1 << 10,
    }

    /// <summary>身上有哪些控制/状态。多层叠着，全卸完才消失。霸体/无敌扛得住硬控。</summary>
    public sealed class EntityStateMachine
    {
        #region --- 内部数据 ---

        private struct TimedRemove
        {
            public EntityStateTag Tag;
            public float ExpireTime;
            public float Magnitude;
        }

        private readonly System.Collections.Generic.Dictionary<EntityStateTag, int> _refCounts =
            new System.Collections.Generic.Dictionary<EntityStateTag, int>(16);

        private readonly System.Collections.Generic.List<TimedRemove> _timed =
            new System.Collections.Generic.List<TimedRemove>(8);

        private float _slowMagnitude;

        #endregion

        #region --- 当前状态 / 能不能动 ---

        /// <summary>当前身上有哪些状态。</summary>
        public EntityStateTag CurrentStateMask { get; private set; } = EntityStateTag.None;

        /// <summary>某个状态刚加上或刚卸掉。</summary>
        public event Action<EntityStateTag, bool> OnStateTagChanged;

        /// <summary>能被清控、能跟服务器对齐的那些控制。</summary>
        public static readonly EntityStateTag ControlMask =
            EntityStateTag.Root | EntityStateTag.Stun | EntityStateTag.Silence
            | EntityStateTag.Disarm | EntityStateTag.Slow | EntityStateTag.Blind
            | EntityStateTag.Airborne;

        /// <summary>这些状态不能走路。</summary>
        public static readonly EntityStateTag MoveBlockMask =
            EntityStateTag.Root | EntityStateTag.Stun
            | EntityStateTag.Airborne;

        /// <summary>这些状态不能攻击。</summary>
        public static readonly EntityStateTag AttackBlockMask =
            EntityStateTag.Stun | EntityStateTag.Disarm | EntityStateTag.Airborne;

        public bool CanMove => (CurrentStateMask & MoveBlockMask) == 0;
        public bool CanAttack => (CurrentStateMask & AttackBlockMask) == 0;

        #endregion

        #region --- 查询 ---

        /// <summary>减速后还剩多少移速。没减速是 1，最低 0.05。</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                if ((CurrentStateMask & EntityStateTag.Slow) == 0) return 1f;
                float mul = 1f - _slowMagnitude;
                if (mul < 0.05f) mul = 0.05f;
                if (mul > 1f) mul = 1f;
                return mul;
            }
        }

        /// <summary>身上有没有这些状态（有一个就算）。</summary>
        public bool HasState(EntityStateTag tag)
        {
            return (CurrentStateMask & tag) != 0;
        }

        /// <summary>现在能不能放技能。眩晕/击飞不行；魔法技还看沉默；定身时闪现仍可放。</summary>
        public bool CanCastSkill(bool isMagicSkill, bool allowsWhileRooted = false)
        {
            EntityStateTag forbid = EntityStateTag.Stun | EntityStateTag.Airborne;
            if (isMagicSkill) forbid |= EntityStateTag.Silence;
            if (!allowsWhileRooted) forbid |= EntityStateTag.Root;
            return (CurrentStateMask & forbid) == 0;
        }

        #endregion

        #region --- 加上 / 卸掉 ---

        /// <summary>叠上一层状态。</summary>
        public bool AddState(EntityStateTag tag)
        {
            if (tag == EntityStateTag.None) return false;

            bool any = false;
            for (int bit = 1; bit != 0; bit <<= 1)
            {
                var single = (EntityStateTag)bit;
                if ((tag & single) == 0) continue;
                if (AddSingle(single))
                    any = true;
            }
            return any;
        }

        /// <summary>卸掉一层状态。</summary>
        public void RemoveState(EntityStateTag tag)
        {
            if (tag == EntityStateTag.None) return;

            for (int bit = 1; bit != 0; bit <<= 1)
            {
                var single = (EntityStateTag)bit;
                if ((tag & single) == 0) continue;
                RemoveSingle(single);
            }
        }

        /// <summary>加上状态，时间到了自动卸。双方要用同一套时钟。</summary>
        public bool AddTimedState(EntityStateTag tag, float now, float duration, float magnitude = 0f)
        {
            if (duration <= 0f || tag == EntityStateTag.None) return false;
            if (!AddState(tag)) return false;

            if ((tag & EntityStateTag.Slow) != 0 && magnitude > _slowMagnitude)
                _slowMagnitude = magnitude > 1f ? 1f : magnitude;

            _timed.Add(new TimedRemove
            {
                Tag = tag,
                ExpireTime = now + duration,
                Magnitude = magnitude
            });
            return true;
        }

        /// <summary>按配表状态编号挂上，时间到了自动卸。</summary>
        public bool ApplyStatusFlags(int statusFlags, float now, float duration, float slowMagnitude = 0f)
        {
            var tag = StatusFlagsToTag(statusFlags);
            if (tag == EntityStateTag.None || duration <= 0f) return false;
            return AddTimedState(tag, now, duration, slowMagnitude);
        }

        #endregion

        #region --- 每帧推进 / 清理 ---

        /// <summary>到点的状态自动卸掉。每帧调一次。</summary>
        public void Tick(float now)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (_timed[i].ExpireTime > now) continue;
                var entry = _timed[i];
                _timed.RemoveAt(i);
                // 先从队列拿掉，再卸状态，避免回调把列表改乱。
                RemoveState(entry.Tag);
            }
        }

        /// <summary>清掉身上所有硬控。</summary>
        public void ClearControl()
        {
            ClearMask(ControlMask);
        }

        /// <summary>把本地控制状态跟服务器对齐。</summary>
        public void ReconcileControlMask(EntityStateTag authMask)
        {
            EntityStateTag want = authMask & ControlMask;
            EntityStateTag have = CurrentStateMask & ControlMask;
            EntityStateTag toRemove = have & ~want;
            EntityStateTag toAdd = want & ~have;
            if (toRemove != EntityStateTag.None)
                ForceClearState(toRemove);
            if (toAdd != EntityStateTag.None)
                AddState(toAdd);
        }

        /// <summary>不管叠了几层，立刻卸干净。施法结束时防残留。</summary>
        public void ForceClearState(EntityStateTag tag)
        {
            if (tag == EntityStateTag.None) return;
            ClearMask(tag);
        }

        /// <summary>清空身上所有状态。</summary>
        public void ClearAll()
        {
            if (_refCounts.Count == 0 && CurrentStateMask == EntityStateTag.None) return;

            var keys = new System.Collections.Generic.List<EntityStateTag>(_refCounts.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var t = keys[i];
                if (HasState(t))
                    OnStateTagChanged?.Invoke(t, false);
            }

            _refCounts.Clear();
            _timed.Clear();
            _slowMagnitude = 0f;
            CurrentStateMask = EntityStateTag.None;
        }

        #endregion

        #region --- 配表转换 ---

        /// <summary>把配表状态编号转成运行时状态。两套编号不一样，别直接抄。</summary>
        public static EntityStateTag StatusFlagsToTag(int statusFlags)
        {
            EntityStateTag tag = EntityStateTag.None;
            if ((statusFlags & SkillEffectFlags.Root) != 0) tag |= EntityStateTag.Root;
            if ((statusFlags & SkillEffectFlags.Stun) != 0) tag |= EntityStateTag.Stun;
            if ((statusFlags & SkillEffectFlags.Silence) != 0) tag |= EntityStateTag.Silence;
            if ((statusFlags & SkillEffectFlags.Slow) != 0) tag |= EntityStateTag.Slow;
            if ((statusFlags & SkillEffectFlags.Disarm) != 0) tag |= EntityStateTag.Disarm;
            if ((statusFlags & SkillEffectFlags.Airborne) != 0) tag |= EntityStateTag.Airborne;
            if ((statusFlags & SkillEffectFlags.Unstoppable) != 0) tag |= EntityStateTag.Unstoppable;
            return tag;
        }

        #endregion

        #region --- 内部 ---

        // 叠一层；从无到有时通知外面。
        private bool AddSingle(EntityStateTag tag)
        {
            if (IsImmuneTo(tag)) return false;

            _refCounts.TryGetValue(tag, out int count);
            _refCounts[tag] = count + 1;

            if (count == 0)
            {
                CurrentStateMask |= tag;
                OnStateTagChanged?.Invoke(tag, true);
            }
            return true;
        }

        // 卸一层；叠数为 0 才真正卸掉。减速卸完时速度恢复。
        private void RemoveSingle(EntityStateTag tag)
        {
            if (!_refCounts.TryGetValue(tag, out int count) || count <= 0) return;

            count--;
            _refCounts[tag] = count;

            if (count == 0)
            {
                CurrentStateMask &= ~tag;
                _refCounts.Remove(tag);
                if (tag == EntityStateTag.Slow && (CurrentStateMask & EntityStateTag.Slow) == 0)
                    _slowMagnitude = 0f;
                OnStateTagChanged?.Invoke(tag, false);
            }
        }

        // 把这些状态连叠层一起清掉。
        private void ClearMask(EntityStateTag mask)
        {
            for (int bit = 1; bit != 0; bit <<= 1)
            {
                var single = (EntityStateTag)bit;
                if ((mask & single) == 0) continue;
                while (HasState(single))
                    RemoveSingle(single);
            }

            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if ((_timed[i].Tag & mask) != 0)
                    _timed.RemoveAt(i);
            }
        }

        /// <summary>霸体/无敌时硬控套不上。</summary>
        private bool IsImmuneTo(EntityStateTag newTag)
        {
            bool isControl = (newTag & (EntityStateTag.Stun | EntityStateTag.Root
                                        | EntityStateTag.Silence | EntityStateTag.Disarm
                                        | EntityStateTag.Airborne)) != 0;
            if (!isControl) return false;
            return HasState(EntityStateTag.Unstoppable | EntityStateTag.Invincible);
        }

        #endregion
    }
}
