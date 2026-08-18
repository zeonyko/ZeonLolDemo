using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>动画轨：没配就不播；有配置才切到对应动作。</summary>
    public sealed class AnimHandler : IPlaybackHandler
    {
        #region 播放处理器
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            var actor = ctx?.SourceEntity;
            if (actor == null) return;

            string animId = !string.IsNullOrEmpty(clip.Event.Param) ? clip.Event.Param : clip.Event.Key;
            if (string.IsNullOrEmpty(animId)) return;

            var anim = actor.GetComponent<AnimationComponent>();
            if (anim == null) return;

            if (IsDeath(animId))
            {
                actor.GetComponent<CombatViewComponent>()?.PlayDeath(null);
                return;
            }

            if (ctx.Kind == PlaybackKind.Cast)
            {
                PlayCastAnim(actor, anim, ctx, clip, animId);
                return;
            }

            PlayReactionAnim(actor, anim, ctx, animId);
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }

        public void OnExit(PlaybackContext ctx, PlaybackClip clip) { }
        #endregion

        #region Cast / Reaction 分支
        /// <summary>施法动作：切到技能对应状态，并按是否为连招首刀选择对齐方式。</summary>
        private static void PlayCastAnim(Entity actor, AnimationComponent anim, PlaybackContext ctx, PlaybackClip clip, string animId)
        {
            string stateName = ResolveCastStateName(animId, ctx.SkillId);
            actor.GetComponent<CombatViewComponent>()?.OnCombatViewEvent(new CombatViewEvent
            {
                Type = ECombatViewEvent.AttackSwing
            });

            // 连招后续刀：快速切动作，勿再按「最后一击」拉长整段动画
            float clipTime = clip.Event.Time;
            if (clipTime > 0.05f)
            {
                anim.CrossFadeByName(stateName, 0.05f);
            }
            else
            {
                float hitTime = ResolveFirstHitTime(ctx.SkillId);
                anim.CrossFadeCastAligned(stateName, hitTime, ctx.SkillId, 0.06f);
            }

            ctx.Lifetime?.Register(() =>
            {
                actor.GetComponent<CombatViewComponent>()?.CancelAttackPose();
            });
        }

        /// <summary>受击反馈动作：严格按配置的状态名播放；未配置有效状态则不播动作。</summary>
        private static void PlayReactionAnim(Entity actor, AnimationComponent anim, PlaybackContext ctx, string animId)
        {
            string normalized = AnimationComponent.NormalizeStateName(animId);
            if (string.IsNullOrEmpty(normalized)
                || normalized == AnimationComponent.StateIdle
                || normalized == AnimationComponent.StateWalk)
                return;

            anim.CrossFadeByName(normalized, 0.05f);

            // Hit 状态额外播闪白/轻抖（仍由配置决定是否进入 Hit）
            if (normalized == AnimationComponent.StateHit)
            {
                actor.GetComponent<CombatViewComponent>()?.PlayHit(ctx.WorldPos);
            }
        }
        #endregion

        #region 动作名解析辅助
        /// <summary>配置的状态名是否是死亡动作。</summary>
        private static bool IsDeath(string animId)
        {
            return AnimationComponent.NormalizeStateName(animId) == AnimationComponent.StateDeath;
        }

        /// <summary>解析施法应播放的状态名；若配置的是通用/非法状态则回退到技能默认动作。</summary>
        private static string ResolveCastStateName(string animId, int skillId)
        {
            string normalized = AnimationComponent.NormalizeStateName(animId);
            if (!string.IsNullOrEmpty(normalized)
                && normalized != AnimationComponent.StateIdle
                && normalized != AnimationComponent.StateWalk
                && normalized != AnimationComponent.StateHit
                && normalized != AnimationComponent.StateDeath)
                return normalized;

            return AnimationComponent.DefaultAnimForSkill(skillId);
        }

        /// <summary>取技能首个命中判定时间，用于施法首刀的动作对齐。</summary>
        private static float ResolveFirstHitTime(int skillId)
        {
            var def = SkillCatalog.Get(skillId);
            if (def != null && SkillClipUtil.TryGetFirstHitPayloadTime(def.Clips, out float first))
                return Mathf.Max(0.03f, first);
            return SkillRules.GetEffectiveHitDelay(def);
        }
        #endregion
    }
}
