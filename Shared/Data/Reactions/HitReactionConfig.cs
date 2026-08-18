using System;

namespace Shared
{
    /// <summary>攻击风格键，用来查受击反馈表。</summary>
    public static class AttackStyles
    {
        public const string LightSlash = "LightSlash";
        public const string HeavySlash = "HeavySlash";
        public const string Pierce = "Pierce";
        public const string HeavyBlow = "HeavyBlow";
        public const string KnockUp = "KnockUp";
        /// <summary>单位普攻等：只飘字，不播受击动作。</summary>
        public const string Unit = "Unit";
    }

    /// <summary>施法者侧反馈编号。</summary>
    public static class CasterReactionIds
    {
        public const string LightSlash = "Caster_Feedback_LightSlash";
        public const string HeavyImpact = "Caster_Feedback_HeavyImpact";
        public const string Pierce = "Caster_Feedback_Pierce";
    }

    /// <summary>目标侧默认受击反馈编号。</summary>
    public static class TargetReactionIds
    {
        public const string Light = "Hit_Default_Light";
        public const string HeavyKnockback = "Hit_Default_HeavyKnockback";
        public const string KnockUp = "Hit_Default_KnockUp";
        public const string ArmorOnly = "Hit_Default_ArmorOnly";
        public const string Pierce = "Hit_Default_Pierce";
        public const string DamageOnly = "Hit_Default_DamageOnly";
        public const string Dodge = "Hit_Default_Dodge";
        public const string Death = "Hit_Default_Death";
        public const string Interrupt = "Hit_Default_Light";
    }

    /// <summary>受击反馈里的一个动作（特效/动画）。</summary>
    [Serializable]
    public class SkillReactionAction
    {
        public float Time;
        public string Type = "Vfx";
        public string Param = "";
        public float Duration;
    }

    /// <summary>一次受击反馈：播多久、有哪些动作。JSON 类名保持 SkillReactionConfig。</summary>
    [Serializable]
    public class SkillReactionConfig
    {
        public string ReactionId = "";
        public float Duration = 0.3f;
        public float HitStop;
        public SkillReactionAction[] Actions = Array.Empty<SkillReactionAction>();
    }

    /// <summary>攻击类型 → 反馈编号。</summary>
    [Serializable]
    public class SkillReactionEntry
    {
        public string AttackType = "";
        public string ReactionId = "";
    }

    /// <summary>按体型分组的攻击类型 → 反馈表。</summary>
    [Serializable]
    public class SkillReactionProfile
    {
        public string ProfileId = "Default";
        public string BodyType = "Medium";
        public SkillReactionEntry[] Matrix = Array.Empty<SkillReactionEntry>();
    }

    /// <summary>按攻击类型和目标状态，选出该播哪条受击反馈。硬控打在霸体上只播护甲。</summary>
    public static class SkillReactionResolver
    {
        private static readonly SkillReactionProfile DefaultProfile = BuildDefaultProfile();

        public static string ResolveTargetReaction(string attackType, bool unstoppable, bool invincible)
        {
            if (string.IsNullOrEmpty(attackType))
                return "";

            if (invincible)
                return TargetReactionIds.ArmorOnly;

            if (unstoppable && IsHardControlAttack(attackType))
                return TargetReactionIds.ArmorOnly;

            var matrix = DefaultProfile.Matrix;
            for (int i = 0; i < matrix.Length; i++)
            {
                if (matrix[i] != null && matrix[i].AttackType == attackType)
                    return matrix[i].ReactionId;
            }

            return TargetReactionIds.Light;
        }

        static bool IsHardControlAttack(string attackType) =>
            attackType == AttackStyles.HeavySlash
            || attackType == AttackStyles.HeavyBlow
            || attackType == AttackStyles.KnockUp
            || attackType == AttackStyles.Pierce;

        static SkillReactionProfile BuildDefaultProfile()
        {
            return new SkillReactionProfile
            {
                ProfileId = "Default",
                BodyType = "Medium",
                Matrix = new[]
                {
                    new SkillReactionEntry { AttackType = AttackStyles.LightSlash, ReactionId = TargetReactionIds.Light },
                    new SkillReactionEntry { AttackType = AttackStyles.HeavySlash, ReactionId = TargetReactionIds.HeavyKnockback },
                    new SkillReactionEntry { AttackType = AttackStyles.HeavyBlow, ReactionId = TargetReactionIds.HeavyKnockback },
                    new SkillReactionEntry { AttackType = AttackStyles.KnockUp, ReactionId = TargetReactionIds.KnockUp },
                    new SkillReactionEntry { AttackType = AttackStyles.Pierce, ReactionId = TargetReactionIds.Pierce },
                    new SkillReactionEntry { AttackType = AttackStyles.Unit, ReactionId = TargetReactionIds.DamageOnly },
                }
            };
        }
    }
}
