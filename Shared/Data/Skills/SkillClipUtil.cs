using System;

namespace Shared
{
    /// <summary>从时间轴推出手时刻、怎么算伤害，加载后补默认值。两端都走这里。</summary>
    public static class SkillClipUtil
    {
        public static float ResolveHitTime(SkillClip[] clips)
        {
            return TryGetFirstHitPayloadTime(clips, out float t) ? t : 0f;
        }

        public static bool TryGetFirstHitPayloadTime(SkillClip[] clips, out float time)
        {
            time = 0f;
            if (clips == null) return false;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (c.Time < best)
                {
                    best = c.Time;
                    found = true;
                }
            }
            if (!found) return false;
            time = best;
            return true;
        }

        public static bool TryGetLastHitPayloadTime(SkillClip[] clips, out float time)
        {
            time = 0f;
            if (clips == null) return false;
            float last = 0f;
            bool found = false;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (!found || c.Time > last)
                    last = c.Time;
                found = true;
            }
            if (!found) return false;
            time = last;
            return true;
        }

        public static float ResolveMotionDistance(SkillClip[] clips)
        {
            if (clips == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Motion != null && c.Motion.HasContent)
                    sum += c.Motion.Distance;
            }
            return sum;
        }

        public static int ResolvePrimaryProjectileId(SkillClip[] clips)
        {
            if (clips == null) return 0;
            for (int i = 0; i < clips.Length; i++)
            {
                var hit = clips[i]?.Hit;
                if (hit?.ProjectileSpawns == null) continue;
                for (int j = 0; j < hit.ProjectileSpawns.Length; j++)
                {
                    if (hit.ProjectileSpawns[j] != null && hit.ProjectileSpawns[j].ProjectileId != 0)
                        return hit.ProjectileSpawns[j].ProjectileId;
                }
            }
            return 0;
        }

        public static bool HasMotion(SkillClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i]?.Motion != null && clips[i].Motion.HasContent)
                    return true;
            }
            return false;
        }

        /// <summary>最后一段位移结束时刻（Time + Duration）。没有位移轨返回 false。</summary>
        public static bool TryGetLastMotionEndTime(SkillClip[] clips, out float endTime)
        {
            endTime = 0f;
            if (clips == null) return false;
            bool found = false;
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                var motion = clip?.Motion;
                if (motion == null || !motion.HasContent) continue;
                float end = clip.Time + (clip.Duration > 0f ? clip.Duration : 0f);
                if (!found || end > endTime)
                    endTime = end;
                found = true;
            }
            return found;
        }

        public static bool HasDirectShape(SkillClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                var hit = clips[i]?.Hit;
                if (hit != null && hit.HasShapeHits) return true;
            }
            return false;
        }

        /// <summary>先看位移，再看弹道，再看近战按形状找人。顺序别改，本地先动和服务器都靠它。</summary>
        public static ESkillResolveMode ResolveMode(SkillClip[] clips, TargetType target)
        {
            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    var m = clips[i]?.Motion;
                    if (m == null || !m.HasContent) continue;
                    return m.Type switch
                    {
                        ESkillMotionClipType.LinearDash => ESkillResolveMode.Dash,
                        ESkillMotionClipType.Jump => ESkillResolveMode.Jump,
                        _ => ESkillResolveMode.Blink
                    };
                }
            }

            if (ResolvePrimaryProjectileId(clips) != 0)
            {
                return target == TargetType.SingleTarget
                    ? ESkillResolveMode.LockedBolt
                    : ESkillResolveMode.SkillshotLine;
            }

            if (HasDirectShape(clips))
                return ESkillResolveMode.MeleeShape;

            return ESkillResolveMode.None;
        }

        public static SkillTimelineEventNode[] ToTimelineEvents(SkillClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return Array.Empty<SkillTimelineEventNode>();

            var list = new System.Collections.Generic.List<SkillTimelineEventNode>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null || string.IsNullOrEmpty(c.Key)) continue;
                list.Add(new SkillTimelineEventNode
                {
                    Time = c.Time,
                    Duration = c.Duration > 0f ? c.Duration : SkillTrackId.DefaultClipDuration,
                    Track = c.Track,
                    Key = c.Key,
                    Param = c.Param ?? ""
                });
            }
            list.Sort((a, b) =>
            {
                int byTime = a.Time.CompareTo(b.Time);
                return byTime != 0 ? byTime : a.Track.CompareTo(b.Track);
            });
            return list.ToArray();
        }

        /// <summary>加载后补默认时长、丢掉空命中/位移。登记前必须调用。</summary>
        public static void Normalize(SkillConfig skill)
        {
            if (skill == null) return;
            if (skill.Clips == null)
                skill.Clips = Array.Empty<SkillClip>();
            for (int i = 0; i < skill.Clips.Length; i++)
            {
                var c = skill.Clips[i];
                if (c == null) continue;
                if (c.Duration <= 0f)
                    c.Duration = SkillTrackId.DefaultClipDuration;

                if (c.Hit != null)
                {
                    NormalizeHit(c.Hit);
                    if (!c.Hit.HasContent)
                        c.Hit = null;
                }

                if (c.Motion != null && !c.Motion.HasContent)
                    c.Motion = null;
            }
        }

        public static SkillHitPayload PrimaryHit(SkillClip[] clips)
        {
            if (clips == null) return null;
            SkillHitPayload best = null;
            float bestTime = float.MaxValue;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (c.Time < bestTime)
                {
                    bestTime = c.Time;
                    best = c.Hit;
                }
            }
            return best;
        }

        /// <summary>按序号取第 N 条有内容的命中（从 0 起），和网络包命中序号对齐。</summary>
        public static bool TryGetHitByIndex(SkillClip[] clips, uint hitIndex, out SkillHitPayload hit)
        {
            hit = null;
            if (clips == null) return false;
            uint n = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (n == hitIndex)
                {
                    hit = c.Hit;
                    return true;
                }
                n++;
            }
            return false;
        }

        public static bool TryGetHitByIndex(int skillId, uint hitIndex, out SkillHitPayload hit)
        {
            hit = null;
            return SkillCatalog.TryGet(skillId, out var def)
                   && TryGetHitByIndex(def.Clips, hitIndex, out hit);
        }

        /// <summary>最早一条形状/区域判定，给技能指示器用。</summary>
        public static bool TryGetPrimaryShape(SkillClip[] clips, out SkillShapePayload shape)
        {
            shape = null;
            if (clips == null) return false;
            float bestTime = float.MaxValue;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                var hit = c?.Hit;
                if (hit == null) continue;
                if (c.Time > bestTime) continue;

                if (hit.AreaSpawns != null)
                {
                    for (int a = 0; a < hit.AreaSpawns.Length; a++)
                    {
                        var area = hit.AreaSpawns[a];
                        if (area == null || !area.HasContent || area.Shape == null) continue;
                        bestTime = c.Time;
                        shape = area.Shape;
                        break;
                    }
                    if (shape != null) continue;
                }

                if (!hit.HasShapeHits) continue;
                for (int j = 0; j < hit.ShapeHits.Length; j++)
                {
                    var mh = hit.ShapeHits[j];
                    if (mh?.Shape == null) continue;
                    bestTime = c.Time;
                    shape = mh.Shape;
                    break;
                }
            }
            return shape != null;
        }

        public static bool TryGetPrimaryShapeHit(SkillClip[] clips, out SkillShapeHitPayload shapeHit)
        {
            shapeHit = null;
            if (clips == null) return false;
            float bestTime = float.MaxValue;

            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                var hit = c?.Hit;
                if (hit == null || !hit.HasShapeHits) continue;
                if (c.Time > bestTime) continue;

                for (int j = 0; j < hit.ShapeHits.Length; j++)
                {
                    var mh = hit.ShapeHits[j];
                    if (mh?.Shape == null) continue;
                    bestTime = c.Time;
                    shapeHit = mh;
                    break;
                }
            }

            return shapeHit != null;
        }

        /// <summary>按表现轨时刻取对应判定形状：优先即将到来的命中，否则最近一次。</summary>
        public static bool TryGetShapeForTime(SkillClip[] clips, float time, out SkillShapePayload shape)
        {
            shape = null;
            if (clips == null) return false;

            SkillShapePayload nextShape = null;
            float nextDt = float.MaxValue;
            SkillShapePayload prevShape = null;
            float prevDt = float.MaxValue;

            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (!TryExtractShape(c.Hit, out var s) || s == null) continue;

                float dt = c.Time - time;
                if (dt >= -0.0001f)
                {
                    if (dt < nextDt)
                    {
                        nextDt = dt;
                        nextShape = s;
                    }
                }
                else
                {
                    float back = -dt;
                    if (back < prevDt)
                    {
                        prevDt = back;
                        prevShape = s;
                    }
                }
            }

            shape = nextShape ?? prevShape;
            return shape != null;
        }

        static bool TryExtractShape(SkillHitPayload hit, out SkillShapePayload shape)
        {
            shape = null;
            if (hit == null) return false;
            if (hit.HasShapeHits)
            {
                for (int j = 0; j < hit.ShapeHits.Length; j++)
                {
                    var mh = hit.ShapeHits[j];
                    if (mh?.Shape == null) continue;
                    shape = mh.Shape;
                    return true;
                }
            }

            if (hit.AreaSpawns == null) return false;
            for (int a = 0; a < hit.AreaSpawns.Length; a++)
            {
                var area = hit.AreaSpawns[a];
                if (area?.Shape == null) continue;
                shape = area.Shape;
                return true;
            }

            return false;
        }

        static void NormalizeHit(SkillHitPayload hit)
        {
            if (hit == null) return;
            hit.ShapeHits ??= Array.Empty<SkillShapeHitPayload>();
            hit.ProjectileSpawns ??= Array.Empty<SkillProjectilePayload>();
            hit.AreaSpawns ??= Array.Empty<SkillAreaPayload>();
            hit.CasterBuffs ??= Array.Empty<BuffApplySpec>();

            for (int i = 0; i < hit.ShapeHits.Length; i++)
                NormalizeShapeHit(hit.ShapeHits[i], defaultCoef: 1f);

            for (int i = 0; i < hit.ProjectileSpawns.Length; i++)
            {
                var p = hit.ProjectileSpawns[i];
                if (p == null) continue;
                p.Anchor ??= TargetSelectorData.CasterSelf();
            }

            var areas = new System.Collections.Generic.List<SkillAreaPayload>(hit.AreaSpawns.Length);
            for (int i = 0; i < hit.AreaSpawns.Length; i++)
            {
                var area = hit.AreaSpawns[i];
                if (area == null) continue;
                if (area.TickInterval < 0.05f) area.TickInterval = 0.5f;
                if (area.Duration < 0.05f) area.Duration = 1f;
                area.Anchor ??= TargetSelectorData.FixedWorld();
                NormalizeHitEffect(area.TickEffect, defaultCoef: 0.5f);
                if (area.HasContent)
                    areas.Add(area);
            }
            hit.AreaSpawns = areas.Count == hit.AreaSpawns.Length
                ? hit.AreaSpawns
                : areas.ToArray();

            for (int i = 0; i < hit.CasterBuffs.Length; i++)
            {
                var b = hit.CasterBuffs[i];
                if (b == null) continue;
                if (b.Relation == 0 && b.BuffId != 0)
                    b.Relation = (int)TargetRelation.Self;
            }
        }

        static void NormalizeShapeHit(SkillShapeHitPayload mh, float defaultCoef)
        {
            if (mh == null) return;
            mh.Anchor ??= TargetSelectorData.CasterSelf();
            mh.Effect ??= new HitEffectPayload();
            NormalizeHitEffect(mh.Effect, defaultCoef);
        }

        static void NormalizeHitEffect(HitEffectPayload effect, float defaultCoef)
        {
            if (effect == null) return;
            effect.TargetBuffs ??= Array.Empty<BuffApplySpec>();
            if (effect.Damage == null)
                effect.Damage = DamagePayload.DefaultInstant(defaultCoef);
            if (effect.Damage.Coefficient <= 0f)
                effect.Damage.Coefficient = defaultCoef > 0f ? defaultCoef : 1f;
        }
    }
}
