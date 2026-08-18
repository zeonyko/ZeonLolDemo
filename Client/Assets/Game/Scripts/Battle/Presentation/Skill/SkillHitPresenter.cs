using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>命中表现入口：不改血量；反馈只在客户端按攻击类型和命中标记查表。</summary>
    public static class SkillHitPresenter
    {
        #region 网络命中入口
        /// <summary>服务器命中包：先给本地施法者记账，再逐个播目标反馈，最后播施法者反馈。</summary>
        public static void ProcessNetwork(S2C_Battle_SkillHitPacket msg)
        {
            if (msg == null) return;

            var caster = EntityManager.Instance.GetEntity(msg.CasterId);
            if (msg.CasterId == BattleSystem.Instance.LocalPlayerId)
                caster?.GetComponent<SkillCastComponent>()?.OnHitAcknowledged(msg.SkillId, msg.HitIndex);

            Vector3 casterPos = caster?.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            string casterReaction = ResolveCasterReaction(msg.SkillId, msg.HitIndex);

            bool suppressCasterFx = CombatFeedbackRules.SuppressCasterFx(msg.CasterId);

            if (msg.Hits == null || msg.Hits.Length == 0)
            {
                if (!suppressCasterFx)
                    PlayReaction(casterReaction, caster, caster, casterPos, 0f, default);
                return;
            }

            for (int i = 0; i < msg.Hits.Length; i++)
            {
                var hit = msg.Hits[i];
                if (hit == null) continue;

                Vector3 kb = Vector3.zero;
                if (hit.KnockbackDir != null)
                    kb = new Vector3(hit.KnockbackDir.X, hit.KnockbackDir.Y, hit.KnockbackDir.Z);

                string attackType = hit.AttackType;
                if (string.IsNullOrEmpty(attackType)
                    && SkillClipUtil.TryGetHitByIndex(msg.SkillId, msg.HitIndex, out var payload)
                    && payload != null)
                {
                    attackType = payload.AttackType;
                }

                string targetReaction = ResolveTargetReaction(attackType, hit.HitFlags);
                var damageType = CombatTextUtil.InferDamageTypeFromSkill(msg.SkillId);

                ProcessTarget(new SkillHitContext
                {
                    CasterId = msg.CasterId,
                    TargetId = hit.TargetId,
                    SkillId = msg.SkillId,
                    HitIndex = msg.HitIndex,
                    Accepted = true,
                    Damage = hit.Damage,
                    CasterPos = casterPos,
                    HitFlags = hit.HitFlags,
                    KnockbackDir = kb,
                    AttackType = attackType ?? "",
                    TargetReactionId = targetReaction,
                    CasterReactionId = casterReaction,
                    DamageType = damageType
                });
            }

            if (!suppressCasterFx)
                PlayReaction(casterReaction, caster, caster, casterPos, 0f, default);
        }
        #endregion

        #region 单次结算入口（非网络路径）
        /// <summary>非网络路径的单次命中结算（如本地剧情/测试触发）：按需补全 Reaction Id 后走统一播放。</summary>
        public static void Process(SkillHitContext result)
        {
            if (string.IsNullOrEmpty(result.TargetReactionId)
                && !string.IsNullOrEmpty(result.AttackType))
            {
                result.TargetReactionId = ResolveTargetReaction(result.AttackType, result.HitFlags);
            }

            if (string.IsNullOrEmpty(result.CasterReactionId))
                result.CasterReactionId = ResolveCasterReaction(result.SkillId, result.HitIndex);

            ProcessTarget(result);

            if (CombatFeedbackRules.SuppressCasterFx(result.CasterId))
                return;

            var caster = EntityManager.Instance.GetEntity(result.CasterId);
            Vector3 casterPos = result.CasterPos;
            if (casterPos.sqrMagnitude < 0.0001f)
                casterPos = caster?.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;

            PlayReaction(result.CasterReactionId, caster, caster, casterPos, 0f, result);
        }
        #endregion

        #region 公开 API：单点播放 Reaction
        /// <summary>对外暴露的便捷重载：source 与 target 为同一实体，带完整结算上下文。</summary>
        public static void PlayReaction(
            string reactionId,
            Entity actor,
            Vector3 worldPos,
            float damage,
            in SkillHitContext result)
        {
            PlayReaction(reactionId, actor, actor, worldPos, damage, result);
        }

        /// <summary>对外暴露的便捷重载：无结算上下文时使用（如纯表现触发）。</summary>
        public static void PlayReaction(
            string reactionId,
            Entity actor,
            Vector3 worldPos,
            float damage = 0f)
        {
            PlayReaction(reactionId, actor, actor, worldPos, damage, default);
        }
        #endregion

        #region Reaction Id 解析
        /// <summary>施法者反馈 Id：优先取该段 Hit 配置的 CasterFeedbackType，否则回退技能主命中配置。</summary>
        static string ResolveCasterReaction(int skillId, uint hitIndex)
        {
            if (SkillClipUtil.TryGetHitByIndex(skillId, hitIndex, out var hitPayload)
                && hitPayload != null
                && !string.IsNullOrEmpty(hitPayload.CasterFeedbackType))
            {
                return hitPayload.CasterFeedbackType;
            }

            var def = SkillCatalog.Get(skillId);
            return def?.PrimaryHit?.CasterFeedbackType ?? "";
        }

        /// <summary>目标反馈 Id：NoFeedback/Dodge/Lethal 优先判定，否则按 AttackType 查 Reaction 表。</summary>
        static string ResolveTargetReaction(string attackType, uint hitFlags)
        {
            if ((hitFlags & (uint)EBattle_HitFlags.NoFeedback) != 0)
                return "";
            if ((hitFlags & (uint)EBattle_HitFlags.Dodge) != 0)
                return TargetReactionIds.Dodge;
            if ((hitFlags & (uint)EBattle_HitFlags.Lethal) != 0)
                return TargetReactionIds.Death;

            bool invincible = (hitFlags & (uint)EBattle_HitFlags.Invincible) != 0;
            return SkillReactionResolver.ResolveTargetReaction(
                attackType ?? AttackStyles.LightSlash,
                unstoppable: false,
                invincible: invincible);
        }
        #endregion

        #region 目标命中处理
        /// <summary>单个目标的命中反馈：Dodge/Lethal 优先，随后播 Reaction 与地面/暴击等附加表现。</summary>
        private static void ProcessTarget(SkillHitContext result)
        {
            if (!result.Accepted || result.TargetId == 0)
                return;

            var caster = EntityManager.Instance.GetEntity(result.CasterId);
            var target = EntityManager.Instance.GetEntity(result.TargetId);
            Vector3 hitFrom = result.CasterPos;
            if (hitFrom.sqrMagnitude < 0.0001f)
                hitFrom = caster?.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;

            if (result.Damage < 0f)
            {
                Vector3 head = target?.GetComponent<TransformComponent>()?.Position ?? hitFrom;
                VfxManager.Instance?.PlayDamageFloat(head + Vector3.up * 1.4f, result.Damage);
                return;
            }

            bool noFeedback = (result.HitFlags & (uint)EBattle_HitFlags.NoFeedback) != 0
                || CombatFeedbackRules.SuppressHitFx(result.CasterId, result.TargetId);
            if (noFeedback)
                return;

            if ((result.HitFlags & (uint)EBattle_HitFlags.Dodge) != 0)
            {
                PlayReaction(TargetReactionIds.Dodge, target, target, hitFrom, 0f, result);
                return;
            }

            bool lethal = (result.HitFlags & (uint)EBattle_HitFlags.Lethal) != 0;
            string reactionId = lethal ? TargetReactionIds.Death : result.TargetReactionId;

            if (!string.IsNullOrEmpty(reactionId))
                PlayReaction(reactionId, target, target, hitFrom, result.Damage, result);

            // 击飞等特殊表现已收进 Reaction JSON（Airborne / StatusPopup）

            // 脚底溅射 / 配表 HitImpactModule（纯表现）
            PlayPresentationExtras(result, target);
        }

        /// <summary>地面溅射 / 暴击附加层，仅在有伤害或暴击时触发。</summary>
        static void PlayPresentationExtras(in SkillHitContext result, Entity target)
        {
            if (result.Damage <= 0f && (result.HitFlags & (uint)EBattle_HitFlags.Crit) == 0)
                return;

            Vector3 feet = target?.GetComponent<TransformComponent>()?.Position
                           ?? Vector3.zero;
            feet.y = GameConstants.GroundY;

            // 命中落地特效由技能表现配置驱动
            SkillPresentationModules.PlayHitImpact(result.SkillId, feet);

            // 暴击额外命中层（分级反馈）
            if ((result.HitFlags & (uint)EBattle_HitFlags.Crit) != 0)
            {
                Vector3 chest = feet + Vector3.up * 0.95f;
                VfxLibrary.SpawnTransient(
                    "Vfx_HitCrit", chest, Vector3.forward,
                    durationOverride: 0.32f,
                    worldVelocity: Vector3.up * 0.5f);
            }
        }

        #endregion

        #region 内部播放实现
        /// <summary>统一播反馈：查表 → 推断伤害类型 → 交给播放器。</summary>
        private static void PlayReaction(
            string reactionId,
            Entity source,
            Entity target,
            Vector3 worldPos,
            float damage,
            in SkillHitContext result)
        {
            if (string.IsNullOrEmpty(reactionId) || source == null)
                return;
            if (Playback.Instance == null)
                return;

            var cfg = SkillReactionConfigLoader.Get(reactionId);
            if (cfg == null)
            {
                Debug.LogError($"[SkillHitPresenter] missing reaction id={reactionId}");
                return;
            }

            var damageType = result.DamageType;
            if (damageType == DamageType.Physical && result.SkillId > 0)
                damageType = CombatTextUtil.InferDamageTypeFromSkill(result.SkillId);

            Playback.Instance.Play(new PlaybackData
            {
                Tag = $"reaction:{reactionId}",
                Events = cfg.Events,
                Context = new PlaybackContext
                {
                    Kind = PlaybackKind.Reaction,
                    SourceEntity = source,
                    TargetEntity = target,
                    WorldPos = worldPos,
                    SkillId = result.SkillId,
                    Damage = damage,
                    KnockbackDir = result.KnockbackDir,
                    HitFlags = result.HitFlags,
                    DamageType = damageType
                }
            });
        }
        #endregion
    }
}
