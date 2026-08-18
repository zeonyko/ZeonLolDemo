using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>位移播放：施法位移只通知逻辑去改坐标；受击击退只平滑挪画面。这里不直接写逻辑位置。</summary>
    public sealed class MotionHandler : IPlaybackHandler
    {
        #region 常量
        private const float DefaultReactionKnockback = 1.2f; // 没配参数时的默认击退距离
        #endregion

        #region 播放处理器
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            if (ctx == null || ctx.SourceEntity == null) return;

            string key = clip.Event.Key;
            string param = clip.Event.Param;

            if (ctx.Kind == PlaybackKind.Reaction)
            {
                ApplyReactionDisplacement(ctx, param);
                return;
            }

            // 逻辑位移归 SkillMotionApply；画面冲刺由 View 按时长插值。
            if (!ctx.AllowLogicalMotion) return;
            if (!SkillMotionCodec.IsMotionCommitKey(key)) return;

            SkillMotionApply.Commit(ctx.SourceEntity, key, param, ctx.CastDir, ctx.AimPos);

            if (key != SkillTimelineKeys.CommitDash) return;

            var view = ctx.SourceEntity.GetComponent<ViewComponent>();
            var transformComp = ctx.SourceEntity.GetComponent<TransformComponent>();
            if (view == null || transformComp == null) return;

            view.BeginDashVisual(transformComp.Position, clip.Event.Duration);
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }

        public void OnExit(PlaybackContext ctx, PlaybackClip clip)
        {
            if (ctx?.Kind != PlaybackKind.Cast) return;
            if (clip.Event.Key != SkillTimelineKeys.CommitDash) return;
            ctx.SourceEntity?.GetComponent<ViewComponent>()?.EndDashVisual();
        }
        #endregion

        #region Reaction 击退位移
        /// <summary>受击击退：只播画面位移，不改逻辑位置。</summary>
        private static void ApplyReactionDisplacement(PlaybackContext ctx, string param)
        {
            Vector3 dir = ctx.KnockbackDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            float distance = ParseKnockbackDistance(param);
            if (distance <= 0f) return;

            ctx.SourceEntity?.GetComponent<CombatViewComponent>()
                ?.PlayKnockbackNudge(dir, distance);
        }

        /// <summary>Param 末段为击退距离；解析失败或非法则回退默认值。</summary>
        private static float ParseKnockbackDistance(string param)
        {
            if (string.IsNullOrEmpty(param))
                return DefaultReactionKnockback;

            string[] parts = param.Split(',');
            string token = parts[parts.Length - 1].Trim();
            if (float.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float d)
                && d > 0f)
                return d;

            return DefaultReactionKnockback;
        }
        #endregion
    }
}
