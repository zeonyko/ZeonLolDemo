namespace Shared
{
    /// <summary>位移事件的参数字符串：距离;目标类型;撞人策略。</summary>
    public static class SkillMotionCodec
    {
        public static string Encode(
            float distance,
            ESkillMotionTargetType targetType = ESkillMotionTargetType.InputDirection,
            ESkillMotionCollisionPolicy collision = ESkillMotionCollisionPolicy.PassThrough)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.###};{1};{2}",
                distance, (int)targetType, (int)collision);
        }

        /// <summary>距离大于 0 才算解析成功。</summary>
        public static bool TryParse(
            string param,
            out float distance,
            out ESkillMotionTargetType targetType,
            out ESkillMotionCollisionPolicy collision)
        {
            distance = 0f;
            targetType = ESkillMotionTargetType.InputDirection;
            collision = ESkillMotionCollisionPolicy.PassThrough;
            if (string.IsNullOrEmpty(param)) return false;

            string[] parts = param.Split(';');
            if (parts.Length > 0
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float d))
                distance = d;
            if (parts.Length > 1 && int.TryParse(parts[1], out int t))
                targetType = (ESkillMotionTargetType)t;
            if (parts.Length > 2 && int.TryParse(parts[2], out int c))
                collision = (ESkillMotionCollisionPolicy)c;
            return distance > 0f;
        }

        public static string KeyFromMotionType(ESkillMotionClipType type) => type switch
        {
            ESkillMotionClipType.LinearDash => SkillTimelineKeys.CommitDash,
            ESkillMotionClipType.Jump => SkillTimelineKeys.CommitJump,
            _ => SkillTimelineKeys.CommitBlink
        };

        public static ESkillMotionClipType MotionTypeFromKey(string key)
        {
            if (key == SkillTimelineKeys.CommitDash) return ESkillMotionClipType.LinearDash;
            if (key == SkillTimelineKeys.CommitJump) return ESkillMotionClipType.Jump;
            return ESkillMotionClipType.Blink;
        }

        public static bool IsMotionCommitKey(string key) =>
            key == SkillTimelineKeys.CommitBlink
            || key == SkillTimelineKeys.CommitDash
            || key == SkillTimelineKeys.CommitJump;
    }
}
