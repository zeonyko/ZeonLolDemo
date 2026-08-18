using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>冷却先本地扣；服务器拒绝再退还。</summary>
    public sealed class SkillCooldownService
    {
        readonly Dictionary<int, float> _lastCastTime = new Dictionary<int, float>();

        public float GetCooldownRemaining(int skillId)
        {
            float last = _lastCastTime.TryGetValue(skillId, out float t) ? t : -999f;
            return SkillRules.GetCooldownRemaining(Time.time, last, skillId);
        }

        public bool IsCooldownReady(int skillId)
        {
            float last = _lastCastTime.TryGetValue(skillId, out float t) ? t : -999f;
            return SkillRules.IsCooldownReady(Time.time, last, skillId);
        }

        public void MarkCast(int skillId)
        {
            _lastCastTime[skillId] = Time.time;
        }

        public void ResetCooldown(int skillId)
        {
            _lastCastTime[skillId] = -999f;
        }
    }
}
