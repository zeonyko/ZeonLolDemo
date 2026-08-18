using Shared;

namespace Client.Battle
{
    /// <summary>命中要不要播反馈：看单位和技能配置，这里不写死单位类型。</summary>
    public static class CombatFeedbackRules
    {
        /// <summary>查询实体的单位类型（EntityType），供以下门控方法查表使用。</summary>
        public static bool TryGetEntityType(long entityId, out int entityType)
        {
            entityType = 0;
            if (entityId == 0 || BattleCache.Instance == null) return false;
            if (!BattleCache.Instance.TryGet(entityId, out var data) || data == null)
                return false;
            entityType = data.EntityType;
            return true;
        }

        /// <summary>按 UnitProfile 抑制受击 Reaction（飘字 / Hit 光圈等）。</summary>
        public static bool SuppressHitFx(long casterId, long targetId)
        {
            if (!TryGetEntityType(targetId, out int targetType)) return false;
            TryGetEntityType(casterId, out int casterType);
            return UnitCatalog.SuppressHitFeedback(casterType, targetType);
        }

        /// <summary>施法侧不播 CastBurst、Vfx_Slash 等英雄反馈（单位配表）。</summary>
        public static bool SuppressCasterFx(long casterId)
        {
            if (casterId == 0) return false;
            if (!TryGetEntityType(casterId, out int type)) return false;
            return UnitCatalog.SuppressCasterFeedback(type);
        }

        /// <summary>身体抖动 / 闪白门控（单位配表）。</summary>
        public static bool SuppressHitBodyFeedback(long entityId)
        {
            if (!TryGetEntityType(entityId, out int type)) return false;
            return UnitCatalog.SuppressHitBodyFeedback(type);
        }
    }
}
