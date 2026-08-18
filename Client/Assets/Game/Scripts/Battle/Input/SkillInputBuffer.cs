using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>提前按的技能排队，默认记住 0.3 秒。</summary>
    public sealed class SkillInputBuffer
    {
        public struct Entry
        {
            public int SkillId;
            public SkillAim Aim;
            public float PressTime;
            public float ExpireTime;
        }

        readonly List<Entry> _entries = new List<Entry>(4);
        public float DefaultTtl = GameConstants.SkillInputBufferTtl;

        public int Count => _entries.Count;

        public void Push(int skillId, SkillAim aim, float pressTime = -1f, float ttl = -1f)
        {
            float now = Time.time;
            float press = pressTime >= 0f ? pressTime : now;
            float life = ttl > 0f ? ttl : DefaultTtl;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].SkillId == skillId)
                    _entries.RemoveAt(i);
            }

            _entries.Add(new Entry
            {
                SkillId = skillId,
                Aim = aim,
                PressTime = press,
                ExpireTime = press + life
            });
        }

        public void Clear() => _entries.Clear();

        public void PurgeExpired()
        {
            float now = Time.time;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].ExpireTime <= now)
                    _entries.RemoveAt(i);
            }
        }

        public bool TryPeek(out Entry entry)
        {
            PurgeExpired();
            if (_entries.Count == 0)
            {
                entry = default;
                return false;
            }
            entry = _entries[0];
            return true;
        }

        public bool TryPop(out Entry entry)
        {
            if (!TryPeek(out entry)) return false;
            _entries.RemoveAt(0);
            return true;
        }
    }
}
