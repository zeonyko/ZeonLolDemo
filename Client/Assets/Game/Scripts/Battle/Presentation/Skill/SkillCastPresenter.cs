using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>客户端施法表现入口：拼播放数据，返回这次施法会话。</summary>
    public static class SkillCastPresenter
    {
        /// <summary>播一次施法表现。施法者或播放表缺失时返回 null。</summary>
        public static SkillAbility Start(
            Entity caster,
            int skillId,
            Vector3 castDir,
            bool allowLogical,
            CastPresentationTarget presentationTarget = default)
        {
            // 施法者、播放表、播放系统都要就绪
            if (caster == null)
                return null;

            var cfg = SkillTimelineLoader.GetPlayback(skillId);
            if (cfg == null)
            {
                Debug.LogWarning($"[SkillAbility] 无 Timeline skill={skillId}");
                return null;
            }

            if (Playback.Instance == null)
            {
                Debug.LogWarning("[SkillAbility] Playback 未初始化");
                return null;
            }

            // 提交施法时间轴播放
            Vector3 worldPos = caster.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            Vector3 aimPos = presentationTarget.HasTargetPoint ? presentationTarget.TargetPos : worldPos;

            var lifetime = new PlaybackLifetime();
            var handle = Playback.Instance.Play(new PlaybackData
            {
                Tag = $"cast:{skillId}",
                Events = cfg.Events,
                Context = new PlaybackContext
                {
                    Kind = PlaybackKind.Cast,
                    SourceEntity = caster,
                    TargetEntity = null,
                    WorldPos = worldPos,
                    SkillId = skillId,
                    CastDir = castDir,
                    AimPos = aimPos,
                    AllowLogicalMotion = allowLogical,
                    Lifetime = lifetime
                }
            });

            if (!handle.IsValid)
                return null;

            var cfgSkill = SkillCatalog.Get(skillId);
            if (presentationTarget.HasTargetPoint
                && cfgSkill != null
                && cfgSkill.ResolveMode != ESkillResolveMode.Blink)
                SkillAimAreaView.TrySpawnFromCast(skillId, aimPos);

            CastPresentation.PlayCastBurst(caster, skillId, castDir);
            SkillPresentationModules.PlayCastExtras(
                caster, skillId, presentationTarget.TargetId, aimPos);
            return new SkillAbility(cfg, handle, lifetime);
        }
    }
}
