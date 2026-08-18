using System.Globalization;
using System.Text;

namespace Shared
{
    /// <summary>把技能配置写成好看的 JSON。空的命中/位移不写。给编辑器导出用。</summary>
    public static class SkillConfigWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        #region 导出 JSON

        /// <summary>只写出逻辑段（服务器用）。</summary>
        public static string ToPrettyJsonLogicOnly(SkillConfig logic)
        {
            if (logic == null) return "{\n    \"Logic\": {}\n}";
            SkillClipUtil.Normalize(logic);
            var sb = new StringBuilder(2048);
            sb.AppendLine("{");
            sb.AppendLine("    \"Logic\": {");
            AppendSkillBody(sb, logic, indent: 8);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>写出逻辑段和表现段（客户端用）。</summary>
        public static string ToPrettyJsonClient(SkillConfig logic, SkillPresentationConfig presentation)
        {
            if (logic == null) return ToPrettyJsonLogicOnly(null);
            SkillClipUtil.Normalize(logic);
            if (presentation != null)
                LogicPresentationSplit.NormalizePresentationClips(presentation.Clips);

            var sb = new StringBuilder(4096);
            sb.AppendLine("{");
            sb.AppendLine("    \"Logic\": {");
            AppendSkillBody(sb, logic, indent: 8);
            sb.AppendLine("    },");
            sb.AppendLine("    \"Presentation\": {");
            int skillId = presentation != null && presentation.SkillId > 0 ? presentation.SkillId : logic.SkillId;
            AppendInt(sb, "SkillId", skillId, trailingComma: true, indent: 8);
            if (presentation != null)
            {
                if (!string.IsNullOrEmpty(presentation.CastVfxModule))
                    AppendString(sb, "CastVfxModule", presentation.CastVfxModule, trailingComma: true, indent: 8);
                if (!string.IsNullOrEmpty(presentation.HitImpactModule))
                    AppendString(sb, "HitImpactModule", presentation.HitImpactModule, trailingComma: true, indent: 8);
                if (!string.IsNullOrEmpty(presentation.DefaultAnimKey))
                    AppendString(sb, "DefaultAnimKey", presentation.DefaultAnimKey, trailingComma: true, indent: 8);
                if (presentation.SuppressCastBurst)
                    AppendBool(sb, "SuppressCastBurst", true, trailingComma: true, indent: 8);
            }
            AppendClips(sb, presentation?.Clips, indent: 8);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>拆成逻辑和表现后再写出客户端格式。</summary>
        public static string ToPrettyJson(SkillConfig skill)
        {
            if (skill == null) return "{}";
            var split = LogicPresentationSplit.SplitSkill(skill);
            return ToPrettyJsonClient(split.Logic, split.Presentation);
        }

        #endregion

        #region 写出技能正文

        static void AppendSkillBody(StringBuilder sb, SkillConfig skill, int indent)
        {
            int fi = indent;
            AppendInt(sb, "SkillId", skill.SkillId, trailingComma: true, indent: fi);
            AppendString(sb, "Name", skill.Name ?? "", trailingComma: true, indent: fi);
            AppendInt(sb, "Type", skill.Type, trailingComma: true, indent: fi);
            AppendInt(sb, "AllowedTargets", skill.AllowedTargets, trailingComma: true, indent: fi);
            AppendFloat(sb, "CastRange", skill.CastRange, trailingComma: true, indent: fi);
            AppendFloat(sb, "Cooldown", skill.Cooldown, trailingComma: true, indent: fi);
            AppendInt(sb, "CostMana", skill.CostMana, trailingComma: true, indent: fi);
            AppendBool(sb, "CanCastInStun", skill.CanCastInStun, trailingComma: true, indent: fi);
            AppendBool(sb, "OrientToTargetOnCast", skill.OrientToTargetOnCast, trailingComma: true, indent: fi);
            AppendBool(sb, "IsAutoAttack", skill.IsAutoAttack, trailingComma: true, indent: fi);
            AppendInt(sb, "Damage", skill.Damage, trailingComma: true, indent: fi);
            AppendClips(sb, skill.Clips, indent: fi);
        }

        /// <summary>空时间轴写成 []。</summary>
        static void AppendClips(StringBuilder sb, SkillClip[] clips, int indent = 4)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).Append("\"Clips\": ");
            if (clips == null || clips.Length == 0)
            {
                sb.AppendLine("[]");
                return;
            }

            sb.AppendLine("[");
            for (int i = 0; i < clips.Length; i++)
                AppendClip(sb, clips[i], isLast: i == clips.Length - 1, baseIndent: indent);
            sb.Append(pad).AppendLine("]");
        }

        /// <summary>空的命中/位移不写出去。</summary>
        static void AppendClip(StringBuilder sb, SkillClip clip, bool isLast, int baseIndent = 4)
        {
            int clipIndent = baseIndent + 4;
            string clipPad = new string(' ', clipIndent);
            if (clip == null)
            {
                sb.Append(clipPad).Append("null");
                sb.AppendLine(isLast ? "" : ",");
                return;
            }

            // 空的命中/位移必须省略，不然表会又大又吵。
            bool hasHit = clip.Hit != null && clip.Hit.HasContent;
            bool hasMotion = clip.Motion != null && clip.Motion.HasContent;

            sb.Append(clipPad).AppendLine("{");
            AppendFloat(sb, "Time", clip.Time, trailingComma: true, indent: clipIndent + 4);
            AppendFloat(sb, "Duration", clip.Duration, trailingComma: true, indent: clipIndent + 4);
            AppendInt(sb, "Track", clip.Track, trailingComma: true, indent: clipIndent + 4);
            AppendString(sb, "Key", clip.Key ?? "", trailingComma: true, indent: clipIndent + 4);
            AppendString(sb, "Param", clip.Param ?? "", trailingComma: hasHit || hasMotion, indent: clipIndent + 4);

            if (hasHit)
                AppendHit(sb, clip.Hit, trailingComma: hasMotion, indent: clipIndent + 4);

            if (hasMotion)
                AppendMotion(sb, clip.Motion, trailingComma: false, indent: clipIndent + 4);

            sb.Append(clipPad).Append("}");
            sb.AppendLine(isLast ? "" : ",");
        }

        #endregion

        #region 写出命中 / 位移 / 区域 / 形状

        /// <summary>空的形状/弹道/区域/Buff 不写。</summary>
        static void AppendHit(StringBuilder sb, SkillHitPayload hit, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            bool hasShape = hit.ShapeHits != null && hit.ShapeHits.Length > 0;
            bool hasProjs = hit.ProjectileSpawns != null && hit.ProjectileSpawns.Length > 0;
            bool hasAreas = hit.HasAreaSpawns;
            bool hasCaster = hit.CasterBuffs != null && hit.CasterBuffs.Length > 0;
            bool hasHeal = hit.Heal > 0.05f;
            bool hasLists = hasShape || hasProjs || hasAreas || hasCaster || hasHeal;

            sb.Append(pad).AppendLine("\"Hit\": {");
            AppendString(sb, "AttackType", hit.AttackType ?? "", trailingComma: true, indent: indent + 4);
            AppendString(sb, "CasterFeedbackType", hit.CasterFeedbackType ?? "",
                trailingComma: hasLists, indent: indent + 4);

            if (hasShape)
                AppendShapeHits(sb, hit.ShapeHits, trailingComma: hasProjs || hasAreas || hasCaster || hasHeal, indent: indent + 4);
            if (hasProjs)
                AppendProjectiles(sb, hit.ProjectileSpawns, trailingComma: hasAreas || hasCaster || hasHeal, indent: indent + 4);
            if (hasAreas)
                AppendAreaSpawns(sb, hit.AreaSpawns, trailingComma: hasCaster || hasHeal, indent: indent + 4);
            if (hasCaster)
                AppendBuffs(sb, "CasterBuffs", hit.CasterBuffs, trailingComma: hasHeal, indent: indent + 4);
            if (hasHeal)
                AppendFloat(sb, "Heal", hit.Heal, trailingComma: false, indent: indent + 4);

            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>空区域跳过不写。</summary>
        static void AppendAreaSpawns(StringBuilder sb, SkillAreaPayload[] areas, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"AreaSpawns\": [");
            bool first = true;
            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i];
                if (area == null || !area.HasContent) continue;
                if (!first) sb.AppendLine(",");
                first = false;
                AppendAreaObject(sb, area, indent + 4);
            }
            sb.AppendLine();
            sb.Append(pad).Append(']');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendAreaObject(StringBuilder sb, SkillAreaPayload area, int indent)
        {
            string pad = new string(' ', indent);
            bool hasEffect = area.TickEffect != null;
            bool hasFb = area.TickFeedback != null;
            sb.Append(pad).AppendLine("{");
            AppendString(sb, "Name", area.Name ?? "", trailingComma: true, indent: indent + 4);
            AppendAnchor(sb, area.Anchor ?? TargetSelectorData.FixedWorld(), trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "Duration", area.Duration, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "TickInterval", area.TickInterval, trailingComma: true, indent: indent + 4);
            AppendBool(sb, "ReplaceSame", area.ReplaceSame, trailingComma: true, indent: indent + 4);
            AppendShapeObject(sb, area.Shape, trailingComma: hasEffect || hasFb, indent: indent + 4);
            if (hasEffect)
                AppendHitEffect(sb, area.TickEffect, trailingComma: hasFb, indent: indent + 4, fieldName: "TickEffect");
            if (hasFb)
                AppendFeedback(sb, area.TickFeedback, trailingComma: false, indent: indent + 4, fieldName: "TickFeedback");
            sb.Append(pad).Append('}');
        }

        static void AppendMotion(StringBuilder sb, SkillMotionPayload motion, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"Motion\": {");
            AppendInt(sb, "MotionType", motion.MotionType, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "Distance", motion.Distance, trailingComma: true, indent: indent + 4);
            AppendInt(sb, "TargetType", motion.TargetType, trailingComma: true, indent: indent + 4);
            AppendInt(sb, "CollisionPolicy", motion.CollisionPolicy, trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendShapeHits(StringBuilder sb, SkillShapeHitPayload[] shapeHits, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"ShapeHits\": [");
            for (int i = 0; i < shapeHits.Length; i++)
            {
                var mh = shapeHits[i];
                if (mh == null) continue;
                bool hasEffect = mh.Effect != null;
                bool hasFb = mh.Feedback != null;
                sb.Append(pad).AppendLine("    {");
                AppendString(sb, "Name", mh.Name ?? "", trailingComma: true, indent: indent + 8);
                AppendAnchor(sb, mh.Anchor ?? TargetSelectorData.CasterSelf(), trailingComma: true, indent: indent + 8);
                AppendShapeObject(sb, mh.Shape, trailingComma: hasEffect || hasFb, indent: indent + 8);
                if (hasEffect)
                    AppendHitEffect(sb, mh.Effect, trailingComma: hasFb, indent: indent + 8, fieldName: "Effect");
                if (hasFb)
                    AppendFeedback(sb, mh.Feedback, trailingComma: false, indent: indent + 8, fieldName: "Feedback");
                sb.Append(pad).Append("    }");
                sb.AppendLine(i == shapeHits.Length - 1 ? "" : ",");
            }
            sb.Append(pad).Append(']');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendHitEffect(StringBuilder sb, HitEffectPayload effect, bool trailingComma, int indent, string fieldName)
        {
            string pad = new string(' ', indent);
            var buffs = effect.TargetBuffs;
            bool hasBuffs = buffs != null && buffs.Length > 0;
            bool hasKb = effect.KnockbackDistance > 0.01f;
            sb.Append(pad).Append('"').Append(fieldName).AppendLine("\": {");
            AppendDamageObject(sb, effect.ResolveDamage(), trailingComma: hasBuffs || hasKb, indent: indent + 4);
            if (hasBuffs)
                AppendBuffs(sb, "TargetBuffs", buffs, trailingComma: hasKb, indent: indent + 4);
            if (hasKb)
                AppendFloat(sb, "KnockbackDistance", effect.KnockbackDistance, trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>没有反馈就不写这个字段。</summary>
        static void AppendFeedback(StringBuilder sb, HitFeedbackSignal fb, bool trailingComma, int indent, string fieldName)
        {
            if (fb == null) return;
            string pad = new string(' ', indent);
            sb.Append(pad).Append('"').Append(fieldName).AppendLine("\": {");
            AppendString(sb, "AttackType", fb.AttackType ?? "", trailingComma: true, indent: indent + 4);
            AppendString(sb, "CasterFeedbackType", fb.CasterFeedbackType ?? "", trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendAnchor(StringBuilder sb, TargetSelectorData a, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"Anchor\": {");
            AppendInt(sb, "SelectorType", a.SelectorType, trailingComma: true, indent: indent + 4);
            AppendInt(sb, "Relation", a.Relation, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "SearchRadius", a.SearchRadius, trailingComma: true, indent: indent + 4);
            AppendInt(sb, "MaxTargetCount", a.MaxTargetCount > 0 ? a.MaxTargetCount : 1, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "PositionOffsetX", a.PositionOffsetX, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "PositionOffsetY", a.PositionOffsetY, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "PositionOffsetZ", a.PositionOffsetZ, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "RotationOffsetX", a.RotationOffsetX, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "RotationOffsetY", a.RotationOffsetY, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "RotationOffsetZ", a.RotationOffsetZ, trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>没有伤害数据时写成默认瞬时伤。</summary>
        static void AppendDamageObject(StringBuilder sb, DamagePayload d, bool trailingComma, int indent)
        {
            if (d == null) d = DamagePayload.DefaultInstant();
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"Damage\": {");
            AppendFloat(sb, "BaseDamage", d.BaseDamage, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "Coefficient", d.Coefficient > 0f ? d.Coefficient : 1f, trailingComma: true, indent: indent + 4);
            AppendInt(sb, "DamageType", d.DamageType, trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>没有形状时写成 null。</summary>
        static void AppendShapeObject(StringBuilder sb, SkillShapePayload s, bool trailingComma, int indent)
        {
            if (s == null)
            {
                sb.Append(' ', indent).Append("\"Shape\": null");
                sb.AppendLine(trailingComma ? "," : "");
                return;
            }
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"Shape\": {");
            AppendInt(sb, "ShapeType", s.ShapeType, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "SizeX", s.SizeX, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "SizeY", s.SizeY, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "SizeZ", s.SizeZ, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "OffsetX", s.OffsetX, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "OffsetY", s.OffsetY, trailingComma: true, indent: indent + 4);
            AppendFloat(sb, "OffsetZ", s.OffsetZ, trailingComma: false, indent: indent + 4);
            sb.Append(pad).Append('}');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendProjectiles(StringBuilder sb, SkillProjectilePayload[] projs, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).AppendLine("\"ProjectileSpawns\": [");
            for (int i = 0; i < projs.Length; i++)
            {
                var p = projs[i];
                if (p == null) continue;
                sb.Append(pad).AppendLine("    {");
                AppendInt(sb, "ProjectileId", p.ProjectileId, trailingComma: true, indent: indent + 8);
                AppendAnchor(sb, p.Anchor ?? TargetSelectorData.CasterSelf(), trailingComma: true, indent: indent + 8);
                AppendFloat(sb, "AngleOffsetX", p.AngleOffsetX, trailingComma: true, indent: indent + 8);
                AppendFloat(sb, "AngleOffsetY", p.AngleOffsetY, trailingComma: true, indent: indent + 8);
                AppendFloat(sb, "AngleOffsetZ", p.AngleOffsetZ, trailingComma: false, indent: indent + 8);
                sb.Append(pad).Append("    }");
                sb.AppendLine(i == projs.Length - 1 ? "" : ",");
            }
            sb.Append(pad).Append(']');
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendBuffs(StringBuilder sb, string fieldName, BuffApplySpec[] buffs, bool trailingComma, int indent)
        {
            string pad = new string(' ', indent);
            sb.Append(pad).Append('"').Append(fieldName).AppendLine("\": [");
            for (int i = 0; i < buffs.Length; i++)
            {
                var b = buffs[i];
                if (b == null) continue;
                sb.Append(pad).AppendLine("    {");
                AppendInt(sb, "BuffId", b.BuffId, trailingComma: true, indent: indent + 8);
                AppendFloat(sb, "Chance", b.Chance, trailingComma: true, indent: indent + 8);
                AppendInt(sb, "Relation", b.Relation, trailingComma: true, indent: indent + 8);
                AppendInt(sb, "RequireStatusFlags", b.RequireStatusFlags, trailingComma: true, indent: indent + 8);
                AppendFloat(sb, "OverrideDuration", b.OverrideDuration, trailingComma: false, indent: indent + 8);
                sb.Append(pad).Append("    }");
                sb.AppendLine(i == buffs.Length - 1 ? "" : ",");
            }
            sb.Append(pad).Append(']');
            sb.AppendLine(trailingComma ? "," : "");
        }

        #endregion

        #region 写出单个字段

        static void AppendInt(StringBuilder sb, string name, int value, bool trailingComma, int indent = 4)
        {
            sb.Append(' ', indent).Append('"').Append(name).Append("\": ").Append(value);
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>最多保留 3 位小数。</summary>
        static void AppendFloat(StringBuilder sb, string name, float value, bool trailingComma, int indent = 4)
        {
            sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
                .Append(value.ToString("0.###", Inv));
            sb.AppendLine(trailingComma ? "," : "");
        }

        static void AppendBool(StringBuilder sb, string name, bool value, bool trailingComma, int indent = 4)
        {
            sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
                .Append(value ? "true" : "false");
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>自动转义引号。</summary>
        static void AppendString(StringBuilder sb, string name, string value, bool trailingComma, int indent = 4)
        {
            sb.Append(' ', indent).Append('"').Append(name).Append("\": \"")
                .Append(Escape(value)).Append('"');
            sb.AppendLine(trailingComma ? "," : "");
        }

        /// <summary>转义反斜杠和双引号。</summary>
        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion
    }
}
