using System;

namespace Shared
{
    /// <summary>把技能拆成逻辑轨和表现轨，或再合并。服务器只要逻辑；客户端两边都要。</summary>
    public static class LogicPresentationSplit
    {
        public static bool IsLogicClip(SkillClip clip)
        {
            if (clip == null) return false;
            if (clip.Hit != null && clip.Hit.HasContent) return true;
            if (clip.Motion != null && clip.Motion.HasContent) return true;
            return clip.Track >= SkillTrackId.Phase;
        }

        public static bool IsPresentationClip(SkillClip clip)
        {
            if (clip == null) return false;
            return clip.Track >= SkillTrackId.Anim && clip.Track <= SkillTrackId.HitStop;
        }

        public static void SplitSkillClips(
            SkillClip[] clips,
            out SkillClip[] logicClips,
            out SkillClip[] presentationClips)
        {
            if (clips == null || clips.Length == 0)
            {
                logicClips = Array.Empty<SkillClip>();
                presentationClips = Array.Empty<SkillClip>();
                return;
            }

            var logic = new System.Collections.Generic.List<SkillClip>(clips.Length);
            var pres = new System.Collections.Generic.List<SkillClip>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null) continue;
                if (IsLogicClip(c))
                    logic.Add(CloneLogicClip(c));
                else if (IsPresentationClip(c))
                    pres.Add(ClonePresentationClip(c));
            }

            logicClips = logic.ToArray();
            presentationClips = pres.ToArray();
        }

        public static SkillClip[] MergeSkillClips(SkillClip[] logicClips, SkillClip[] presentationClips)
        {
            int lc = logicClips?.Length ?? 0;
            int pc = presentationClips?.Length ?? 0;
            if (lc == 0 && pc == 0) return Array.Empty<SkillClip>();

            var merged = new SkillClip[lc + pc];
            int n = 0;
            if (logicClips != null)
            {
                for (int i = 0; i < logicClips.Length; i++)
                    merged[n++] = logicClips[i];
            }
            if (presentationClips != null)
            {
                for (int i = 0; i < presentationClips.Length; i++)
                    merged[n++] = presentationClips[i];
            }

            Array.Sort(merged, (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int byTime = a.Time.CompareTo(b.Time);
                return byTime != 0 ? byTime : a.Track.CompareTo(b.Track);
            });
            return merged;
        }

        public static SkillConfigFile SplitSkill(SkillConfig flat)
        {
            if (flat == null) return null;
            SplitSkillClips(flat.Clips, out var logicClips, out var presClips);
            return new SkillConfigFile
            {
                Logic = new SkillConfig
                {
                    SkillId = flat.SkillId,
                    Name = flat.Name ?? "",
                    Type = flat.Type,
                    AllowedTargets = flat.AllowedTargets,
                    CastRange = flat.CastRange,
                    Cooldown = flat.Cooldown,
                    CostMana = flat.CostMana,
                    CanCastInStun = flat.CanCastInStun,
                    OrientToTargetOnCast = flat.OrientToTargetOnCast,
                    IsAutoAttack = flat.IsAutoAttack,
                    Damage = flat.Damage,
                    Clips = logicClips
                },
                Presentation = new SkillPresentationConfig
                {
                    SkillId = flat.SkillId,
                    Clips = presClips
                }
            };
        }

        public static BuffConfigFile SplitBuff(BuffConfig flat)
        {
            if (flat == null) return null;
            return new BuffConfigFile
            {
                Logic = ExtractBuffLogic(flat),
                Presentation = new BuffPresentationConfig { BuffId = flat.BuffId }
            };
        }

        public static BuffConfig ExtractBuffLogic(BuffConfig flat)
        {
            if (flat == null) return null;
            return new BuffConfig
            {
                BuffId = flat.BuffId,
                Name = flat.Name ?? "",
                Type = flat.Type,
                DefaultDuration = flat.DefaultDuration,
                StackType = flat.StackType,
                MaxStacks = flat.MaxStacks,
                StatusFlags = flat.StatusFlags,
                AttributeModifiers = flat.AttributeModifiers ?? Array.Empty<BuffAttributeModifier>(),
                TickInterval = flat.TickInterval,
                PeriodDamage = flat.PeriodDamage,
                EffectKind = flat.EffectKind,
                ValueSource = flat.ValueSource
            };
        }

        public static ProjectileConfigFile SplitProjectile(ProjectileConfig flat)
        {
            if (flat == null) return null;
            return new ProjectileConfigFile
            {
                Logic = ExtractProjectileLogic(flat),
                Presentation = new ProjectilePresentationConfig
                {
                    ProjectileId = flat.ProjectileId,
                    VfxKey = flat.Name ?? ""
                }
            };
        }

        public static ProjectileConfig ExtractProjectileLogic(ProjectileConfig flat)
        {
            if (flat == null) return null;
            return new ProjectileConfig
            {
                ProjectileId = flat.ProjectileId,
                Name = flat.Name ?? "",
                Width = flat.Width,
                Speed = flat.Speed,
                MaxDistance = flat.MaxDistance,
                MaxTargets = flat.MaxTargets,
                PierceCount = flat.PierceCount,
                BoltCount = flat.BoltCount,
                SpreadDegrees = flat.SpreadDegrees,
                ImpactDelay = flat.ImpactDelay,
                CollisionShape = flat.CollisionShape,
                HitEffect = flat.HitEffect,
                HitFeedback = flat.HitFeedback,
                Impact = flat.Impact,
                SpawnAreaOnHit = flat.SpawnAreaOnHit,
                GroundArea = flat.GroundArea
            };
        }

        /// <summary>JSON 里有没有 Logic 段。</summary>
        public static bool IsDualKeyJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf("\"Logic\"", StringComparison.Ordinal) >= 0;
        }

        static SkillClip CloneLogicClip(SkillClip c) => new SkillClip
        {
            Time = c.Time,
            Duration = c.Duration,
            Track = c.Track,
            Key = c.Key ?? "",
            Param = c.Param ?? "",
            Hit = c.Hit,
            Motion = c.Motion
        };

        static SkillClip ClonePresentationClip(SkillClip c) => new SkillClip
        {
            Time = c.Time,
            Duration = c.Duration,
            Track = c.Track,
            Key = c.Key ?? "",
            Param = c.Param ?? ""
        };

        /// <summary>表现轨补默认时长，并清掉命中/位移（表现不负责判定）。</summary>
        public static void NormalizePresentationClips(SkillClip[] clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null) continue;
                if (c.Duration <= 0f)
                    c.Duration = SkillTrackId.DefaultClipDuration;
                c.Hit = null;
                c.Motion = null;
            }
        }
    }
}
