using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>单位类型表：血量、普攻技能、受击要不要播反馈。来自 Units/UnitProfiles.json。</summary>
    [Serializable]
    public class UnitProfile
    {
        /// <summary>单位类型编号。</summary>
        public int EntityType;
        /// <summary>调试用名字。</summary>
        public string Id = "";
        /// <summary>分类（小兵/建筑）。同类互殴可不播受击。</summary>
        public string Class = "";
        /// <summary>只吃普攻伤害（技能伤害为 0，且不播受击反馈）。</summary>
        public bool OnlyAutoAttackDamage;
        /// <summary>永远不播受击反馈（飘字、光圈、动作）。</summary>
        public bool SuppressHitFeedback;
        /// <summary>不播身体抖动 / 闪白 / 受击甩头。</summary>
        public bool SuppressHitBodyFeedback;
        /// <summary>被同类打时不播受击反馈。</summary>
        public bool SuppressHitFeedbackSameClass;
        /// <summary>自己放技能时不播英雄级出手反馈。</summary>
        public bool SuppressCasterFeedback;
        /// <summary>最大生命。没填就用默认。以这张表为准。</summary>
        public float MaxHp;
        /// <summary>默认普攻技能编号；伤害读技能表。</summary>
        public int AttackSkillId;
        /// <summary>移速；没填不覆盖。</summary>
        public float MoveSpeed;
    }

    /// <summary>按单位类型取最大生命。</summary>
    public static class UnitProfileHp
    {
        public static float Resolve(int entityType, float fallback = 0f)
        {
            if (UnitCatalog.TryGet(entityType, out var p) && p != null && p.MaxHp > 0f)
                return p.MaxHp;
            return fallback > 0f ? fallback : GameConstants.DefaultMaxHp;
        }
    }

    /// <summary>UnitProfiles.json 根对象。</summary>
    [Serializable]
    public class UnitProfileConfig
    {
        public UnitProfile[] Profiles = Array.Empty<UnitProfile>();
    }

    /// <summary>运行时单位表。塔/水晶/小兵规则从这里查，不要在战斗代码里写死。</summary>
    public static class UnitCatalog
    {
        private static readonly Dictionary<int, UnitProfile> Map = new Dictionary<int, UnitProfile>();

        public static IReadOnlyDictionary<int, UnitProfile> All => Map;

        /// <summary>换上整张单位表。编号无效的跳过。</summary>
        public static void ClearAndLoad(UnitProfileConfig file)
        {
            Map.Clear();
            if (file?.Profiles == null) return;
            for (int i = 0; i < file.Profiles.Length; i++)
            {
                var p = file.Profiles[i];
                if (p == null || p.EntityType <= 0) continue;
                Map[p.EntityType] = p;
            }
        }

        public static void Clear() => Map.Clear();

        public static bool TryGet(int entityType, out UnitProfile profile) =>
            Map.TryGetValue(entityType, out profile);

        /// <summary>没有就返回空。</summary>
        public static UnitProfile Get(int entityType)
        {
            Map.TryGetValue(entityType, out var p);
            return p;
        }

        /// <summary>这个单位用哪招普攻。</summary>
        public static int ResolveAttackSkillId(int entityType)
        {
            if (TryGet(entityType, out var p) && p != null && p.AttackSkillId > 0)
                return p.AttackSkillId;
            return 0;
        }

        public static bool OnlyAutoAttackDamage(int entityType) =>
            TryGet(entityType, out var p) && p != null && p.OnlyAutoAttackDamage;

        /// <summary>塔/水晶/兵营等只吃普攻：非普攻技能不算合法目标、也不扣血。</summary>
        public static bool CanReceiveSkillDamage(int targetEntityType, SkillConfig skill)
        {
            if (!OnlyAutoAttackDamage(targetEntityType)) return true;
            return skill != null && skill.IsAutoAttack;
        }

        public static bool CanReceiveSkillDamage(int targetEntityType, int skillId)
        {
            if (!OnlyAutoAttackDamage(targetEntityType)) return true;
            return SkillCatalog.TryGet(skillId, out var skill) && skill != null && skill.IsAutoAttack;
        }

        public static bool SuppressHitBodyFeedback(int entityType) =>
            TryGet(entityType, out var p) && p != null && p.SuppressHitBodyFeedback;

        public static bool SuppressCasterFeedback(int entityType) =>
            TryGet(entityType, out var p) && p != null && p.SuppressCasterFeedback;

        /// <summary>被打时要不要藏受击反馈（含同类互殴）。</summary>
        public static bool SuppressHitFeedback(int attackerEntityType, int targetEntityType)
        {
            if (!TryGet(targetEntityType, out var target) || target == null)
                return false;

            if (target.SuppressHitFeedback)
                return true;

            if (target.SuppressHitFeedbackSameClass
                && !string.IsNullOrEmpty(target.Class)
                && TryGet(attackerEntityType, out var attacker)
                && attacker != null
                && string.Equals(attacker.Class, target.Class, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
