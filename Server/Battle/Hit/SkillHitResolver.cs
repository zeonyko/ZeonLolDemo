using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>出手后算打中谁。形状、弹道、地面区域、Buff 可同时有。以服务器为准。</summary>
    public static class SkillHitResolver
    {
        /// <summary>算落点时的临时列表。</summary>
        static readonly List<TargetSelectorResolver.AnchorPose> AnchorScratch =
            new List<TargetSelectorResolver.AnchorPose>(8);

        /// <summary>一次把整次技能算完。没打中人也要通知客户端。</summary>
        public static void ResolveCast(
            ClientSession session,
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast)
        {
            if (caster == null || def == null || cast == null) return;

            TryApplyMotion(session, caster, def, cast);

            uint hitIndex = 0;
            bool anyHit = false;
            var clips = def.Clips;
            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip?.Hit == null || !clip.Hit.HasContent) continue;
                    anyHit = true;
                    ExecuteHit(
                        session, caster, scene, def, cast, clip.Hit, hitIndex,
                        clip.Hit.AttackType);
                    hitIndex++;
                }
            }

            if (!anyHit)
                BroadcastHit(caster, def.SkillId, 0, Array.Empty<SkillHitEntry>());
        }

        /// <summary>算第几段出手。第一段位移技要先回到起手点再判定。</summary>
        public static bool ResolveHitSegment(
            ClientSession session,
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast,
            int hitIndex)
        {
            if (caster == null || def == null || cast == null) return false;

            if (hitIndex == 0 && def.HasCasterMotion)
                PlayerMovementSimulatorHost.ApplyMotionResolveOrigin(caster, cast);

            if (!TryGetHitClip(def, hitIndex, out var hitPayload, out float hitTime))
            {
                if (hitIndex == 0)
                {
                    if (TryGetMotionClip(def, 0, out var onlyMotion))
                        ApplyMotionPayload(session, caster, scene, cast, onlyMotion);
                    BroadcastHit(caster, def.SkillId, 0, Array.Empty<SkillHitEntry>());
                }
                return false;
            }

            bool motionFirst = TryGetMotionClip(def, hitIndex, out var motion, out float motionTime)
                               && motionTime + 0.001f < hitTime;
            if (motionFirst)
                ApplyMotionPayload(session, caster, scene, cast, motion);

            ExecuteHit(
                session, caster, scene, def, cast, hitPayload, (uint)hitIndex,
                hitPayload.AttackType);

            if (!motionFirst && TryGetMotionClip(def, hitIndex, out motion))
                ApplyMotionPayload(session, caster, scene, cast, motion);

            return TryGetHitClip(def, hitIndex + 1, out _);
        }

        /// <summary>取出第几段有内容的出手数据。</summary>
        public static bool TryGetHitClip(SkillConfig def, int hitIndex, out SkillHitPayload hit)
        {
            return TryGetHitClip(def, hitIndex, out hit, out _);
        }

        static bool TryGetHitClip(SkillConfig def, int hitIndex, out SkillHitPayload hit, out float time)
        {
            hit = null;
            time = 0f;
            if (def?.Clips == null || hitIndex < 0) return false;
            int n = 0;
            for (int i = 0; i < def.Clips.Length; i++)
            {
                var c = def.Clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                if (n == hitIndex)
                {
                    hit = c.Hit;
                    time = c.Time;
                    return true;
                }
                n++;
            }
            return false;
        }

        /// <summary>收集每段出手的相对时间。</summary>
        public static float[] CollectHitTimes(SkillConfig def)
        {
            if (def?.Clips == null) return Array.Empty<float>();
            var list = new List<float>(4);
            for (int i = 0; i < def.Clips.Length; i++)
            {
                var c = def.Clips[i];
                if (c?.Hit == null || !c.Hit.HasContent) continue;
                list.Add(c.Time);
            }
            return list.ToArray();
        }

        /// <summary>算一段出手：先形状，再弹道，再地面区域，再给自己上 Buff。顺序别改。</summary>
        public static void ExecuteHit(
            ClientSession session,
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast,
            SkillHitPayload hit,
            uint hitIndex,
            string attackType)
        {
            if (hit == null || caster == null || def == null) return;
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            var hits = new List<SkillHitEntry>(8);

            if (hit.HasShapeHits)
                ResolveShapeHits(caster, scene, def, cast, hit, attackType, hits);

            if (hit.HasProjectileSpawns)
                SpawnProjectiles(caster, scene, def, cast, hit, hitIndex);

            if (hit.HasAreaSpawns && hit.AreaSpawns != null)
            {
                for (int i = 0; i < hit.AreaSpawns.Length; i++)
                {
                    var area = hit.AreaSpawns[i];
                    if (area == null || !area.HasContent) continue;
                    AreaService.Instance.Spawn(caster, scene, def, cast, area, attackType);
                }
            }

            BuffService.Instance.ApplyPayloads(caster, caster, hit.CasterBuffs, def.SkillId, now);
            if (hit.Heal > 0.05f)
                hits.Add(SkillResultService.ApplyHeal(caster, hit.Heal));
            BroadcastHit(caster, def.SkillId, hitIndex, hits.ToArray());
        }

        /// <summary>弹道碰到人：先算接触伤害；有落地爆炸可马上炸或晚点炸。</summary>
        public static void ResolveProjectileContact(
            Entity caster,
            Scene scene,
            SkillConfig def,
            SkillHitPayload sourceHit,
            int projectileId,
            Entity contactTarget,
            float posX, float posY, float posZ,
            float dirX, float dirZ,
            uint hitIndex)
        {
            if (caster == null || def == null) return;

            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            ProjectileCatalog.TryGet(projectileId, out var projCfg);
            bool hasImpact = ProjectileCatalog.TryGetImpact(projectileId, out var impact) && impact != null;
            float impactDelay = projCfg != null ? projCfg.ImpactDelay : 0f;
            bool delayImpact = hasImpact && impactDelay > 0.05f;

            var hits = new List<SkillHitEntry>(4);
            string contactAtk = ResolveFeedbackAttack(
                projCfg?.HitFeedback, sourceHit?.AttackType ?? AttackStyles.Pierce);

            if (contactTarget != null)
            {
                // 接触伤害看配置；没填时，有落地爆炸按 0.35 倍
                var hitEffect = projCfg?.HitEffect;
                if (hitEffect == null)
                {
                    float coef = hasImpact ? 0.35f : 1f;
                    hitEffect = new HitEffectPayload { Damage = DamagePayload.DefaultInstant(coef) };
                }

                var entry = DamageSystem.Apply(
                    caster, contactTarget, def.SkillId, hitEffect.ResolveDamage(), contactAtk);
                hits.Add(entry);
                BuffService.Instance.ApplyPayloads(
                    caster, contactTarget, hitEffect.TargetBuffs, def.SkillId, now);

                if (delayImpact && impact?.ShapeHits != null)
                {
                    for (int i = 0; i < impact.ShapeHits.Length; i++)
                    {
                        var mh = impact.ShapeHits[i];
                        var buffs = mh?.ResolveTargetBuffs();
                        if (buffs == null || buffs.Length == 0) continue;
                        BuffService.Instance.ApplyPayloads(
                            caster, contactTarget, buffs, def.SkillId, now);
                    }
                }
            }

            if (projCfg != null && projCfg.SpawnAreaOnHit && projCfg.GroundArea != null
                && projCfg.GroundArea.HasContent)
            {
                AreaService.Instance.SpawnAt(
                    caster, scene, def, projCfg.GroundArea,
                    posX, posY, posZ, dirX, dirZ,
                    contactAtk);
            }

            if (!hasImpact)
            {
                if (hits.Count > 0)
                    BroadcastHit(caster, def.SkillId, hitIndex, hits.ToArray());
                return;
            }

            if (delayImpact)
            {
                if (hits.Count > 0)
                    BroadcastHit(caster, def.SkillId, hitIndex, hits.ToArray());
                ProjectileService.Instance.ScheduleDelayedImpact(
                    caster.Id, def.SkillId, projectileId, hitIndex,
                    posX, posY, posZ, dirX, dirZ, impactDelay);
                return;
            }

            var impactHit = impact.ToHitPayload();
            ResolveShapeAtFixedOrigin(
                caster, scene, def, impactHit,
                posX, posY, posZ, dirX, dirZ,
                hitIndex, impact.AttackType,
                applyTargetBuffs: true);
        }

        /// <summary>延迟落地爆炸到期，在落点按形状算伤害。</summary>
        public static void ResolveProjectileDelayedImpact(
            Entity caster,
            Scene scene,
            SkillConfig def,
            int projectileId,
            float posX, float posY, float posZ,
            float dirX, float dirZ,
            uint hitIndex)
        {
            if (caster == null || def == null) return;
            if (!ProjectileCatalog.TryGetImpact(projectileId, out var impact) || impact == null)
                return;

            var impactHit = impact.ToHitPayload();
            ResolveShapeAtFixedOrigin(
                caster, scene, def, impactHit,
                posX, posY, posZ, dirX, dirZ,
                hitIndex, impact.AttackType,
                applyTargetBuffs: true);
        }

        /// <summary>按形状找人算伤害和 Buff。动态/最低血锚点即使形状没扫到也给 Buff。</summary>
        static void ResolveShapeHits(
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast,
            SkillHitPayload hit,
            string segmentAttackType,
            List<SkillHitEntry> hits)
        {
            if (scene == null || hit?.ShapeHits == null) return;
            float now = BattleSystem.Instance?.ServerTime ?? 0f;

            for (int m = 0; m < hit.ShapeHits.Length; m++)
            {
                var mh = hit.ShapeHits[m];
                if (mh?.Shape == null) continue;

                string atk = ResolveFeedbackAttack(mh.Feedback, segmentAttackType);
                TargetSelectorResolver.ResolveAnchors(caster, scene, cast, mh.Anchor, AnchorScratch);
                var shapes = new[] { mh.Shape };
                var damage = mh.ResolveDamage();
                var buffs = mh.ResolveTargetBuffs();
                var seen = new HashSet<long>();

                for (int a = 0; a < AnchorScratch.Count; a++)
                {
                    var pose = AnchorScratch[a];
                    var candidates = ShapeHitService.Collect(
                        caster, scene, shapes,
                        pose.PosX, pose.PosY, pose.PosZ, pose.DirX, pose.DirZ, def.SkillId);
                    if (def.IsAutoAttack)
                        TrimAutoAttackCandidates(candidates, cast, caster);
                    float knockback = mh.ResolveKnockbackDistance();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        if (c.Target == null || !seen.Add(c.Target.Id)) continue;
                        hits.Add(DamageSystem.Apply(
                            caster, c.Target, def.SkillId, damage, atk, knockback));
                        BuffService.Instance.ApplyPayloads(
                            caster, c.Target, buffs, def.SkillId, now);
                    }

                    // 动态/最低血锚点：形状没扫到（比如队友）也给 Buff
                    var anchorEnt = pose.Entity;
                    if (anchorEnt != null && seen.Add(anchorEnt.Id))
                    {
                        BuffService.Instance.ApplyPayloads(
                            caster, anchorEnt, buffs, def.SkillId, now);
                        if (SkillResultService.IsValidSkillTarget(caster, anchorEnt, def.SkillId)
                            && damage != null
                            && (damage.Coefficient > 0.05f || damage.BaseDamage > 0.05f))
                        {
                            hits.Add(DamageSystem.Apply(
                                caster, anchorEnt, def.SkillId, damage, atk, knockback));
                        }
                    }
                }
            }
        }

        /// <summary>在固定落点按形状爆炸并通知客户端。</summary>
        static void ResolveShapeAtFixedOrigin(
            Entity caster,
            Scene scene,
            SkillConfig def,
            SkillHitPayload hit,
            float ox, float oy, float oz,
            float dx, float dz,
            uint hitIndex,
            string attackType,
            bool applyTargetBuffs)
        {
            if (scene == null || hit == null)
            {
                BroadcastHit(caster, def.SkillId, hitIndex, Array.Empty<SkillHitEntry>());
                return;
            }

            SkillRules.NormalizeHorizontal(ref dx, ref dz);
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            var hits = new List<SkillHitEntry>(8);

            var shapeHits = hit.ShapeHits;
            if (shapeHits != null)
            {
                for (int m = 0; m < shapeHits.Length; m++)
                {
                    var mh = shapeHits[m];
                    if (mh?.Shape == null) continue;
                    string atk = ResolveFeedbackAttack(mh.Feedback, attackType);

                    var shapes = new[] { mh.Shape };
                    var candidates = ShapeHitService.Collect(
                        caster, scene, shapes, ox, oy, oz, dx, dz, def.SkillId);
                    var damage = mh.ResolveDamage();
                    var buffs = mh.ResolveTargetBuffs();
                    float knockback = mh.ResolveKnockbackDistance();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var cand = candidates[i];
                        hits.Add(DamageSystem.Apply(
                            caster, cand.Target, def.SkillId, damage, atk, knockback));
                        if (applyTargetBuffs)
                            BuffService.Instance.ApplyPayloads(
                                caster, cand.Target, buffs, def.SkillId, now);
                    }
                }
            }

            Console.WriteLine(
                $"[SkillHit] Shape skill={def.SkillId} caster={caster.Id} hits={hits.Count} idx={hitIndex}");
            BroadcastHit(caster, def.SkillId, hitIndex, hits.ToArray());
        }

        /// <summary>放出弹道：锁敌就跟着飞，否则直线飞（可带偏角）。</summary>
        static void SpawnProjectiles(
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast,
            SkillHitPayload hit,
            uint hitIndex)
        {
            if (scene == null || hit.ProjectileSpawns == null || hit.ProjectileSpawns.Length == 0)
                return;

            float dirX = cast?.DirX ?? 0f;
            float dirZ = cast?.DirZ ?? 1f;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            bool locked = cast != null && cast.TargetId != 0;
            for (int i = 0; i < hit.ProjectileSpawns.Length; i++)
            {
                var p = hit.ProjectileSpawns[i];
                if (p == null || p.ProjectileId == 0) continue;

                float yaw = p.AngleOffsetY;
                float bx = dirX;
                float bz = dirZ;
                if (Math.Abs(yaw) >= 0.01f)
                    RotateYaw(dirX, dirZ, yaw, out bx, out bz);

                TargetSelectorResolver.ResolveAnchors(caster, scene, cast, p.Anchor, AnchorScratch);
                var pose = AnchorScratch.Count > 0
                    ? AnchorScratch[0]
                    : new TargetSelectorResolver.AnchorPose
                    {
                        PosX = caster.PosX,
                        PosY = caster.PosY,
                        PosZ = caster.PosZ,
                        DirX = bx,
                        DirZ = bz
                    };

                if (locked)
                {
                    ProjectileService.Instance.SpawnLockedFromHit(
                        caster, scene, def, cast.TargetId, p.ProjectileId, hit, hitIndex,
                        pose.PosX, pose.PosY, pose.PosZ);
                }
                else
                {
                    ProjectileService.Instance.SpawnLinearFromHit(
                        caster, scene, def, p.ProjectileId, hit, hitIndex,
                        pose.PosX, pose.PosY, pose.PosZ, bx, bz);
                }
            }
        }

        /// <summary>出手样式：配置优先，没有就用备用。</summary>
        static string ResolveFeedbackAttack(HitFeedbackSignal fb, string fallback) =>
            !string.IsNullOrEmpty(fb?.AttackType) ? fb.AttackType : (fallback ?? "");

        /// <summary>普攻形状判定只打一人：优先锁敌，否则打最近。</summary>
        static void TrimAutoAttackCandidates(
            List<ShapeHitService.HitCandidate> candidates,
            PendingCastService.PendingCastState cast,
            Entity caster)
        {
            if (candidates == null || candidates.Count <= 1 || caster == null)
                return;

            long lockId = cast != null ? cast.TargetId : 0;
            if (lockId != 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var t = candidates[i].Target;
                    if (t == null || t.Id != lockId)
                        continue;
                    var keep = candidates[i];
                    candidates.Clear();
                    candidates.Add(keep);
                    return;
                }
            }

            int best = -1;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var t = candidates[i].Target;
                if (t == null)
                    continue;
                float dx = t.PosX - caster.PosX;
                float dz = t.PosZ - caster.PosZ;
                float d = dx * dx + dz * dz;
                if (d >= bestDistSq)
                    continue;
                bestDistSq = d;
                best = i;
            }

            if (best < 0)
            {
                candidates.Clear();
                return;
            }

            var nearest = candidates[best];
            candidates.Clear();
            candidates.Add(nearest);
        }

        static bool TryApplyMotion(
            ClientSession session,
            Entity caster,
            SkillConfig def,
            PendingCastService.PendingCastState cast)
        {
            if (!TryGetMotionClip(def, 0, out var motion))
                return false;
            if (def.HasCasterMotion)
                PlayerMovementSimulatorHost.ApplyMotionResolveOrigin(caster, cast);
            return ApplyMotionPayload(session, caster, caster.Scene, cast, motion);
        }

        /// <summary>做跳跃或冲刺/闪现，再跟服务器对齐位置。跳跃必须在地上；距离为 0 就取消。</summary>
        static bool ApplyMotionPayload(
            ClientSession session,
            Entity caster,
            Scene scene,
            PendingCastService.PendingCastState cast,
            SkillMotionPayload motion)
        {
            if (motion == null || !motion.HasContent || caster == null || cast == null)
                return false;

            float dirX = cast.DirX;
            float dirZ = cast.DirZ;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            float dist = motion.Distance;

            if (motion.Type != ESkillMotionClipType.Jump
                && motion.TargetType == (int)ESkillMotionTargetType.LockTarget
                && cast.TargetId != 0
                && scene != null)
            {
                var lockTarget = scene.GetEntity(cast.TargetId);
                if (lockTarget != null && lockTarget.Hp > 0f)
                {
                    bool front = motion.CollisionPolicy == (int)ESkillMotionCollisionPolicy.StopOnHitEnemy;
                    float destX, destZ, faceX, faceZ;
                    if (front)
                    {
                        SkillRules.TryGetLockFrontDestination(
                            caster.PosX, caster.PosZ,
                            lockTarget.PosX, lockTarget.PosZ,
                            motion.Distance,
                            out destX, out destZ, out faceX, out faceZ);
                    }
                    else
                    {
                        SkillRules.TryGetLockBehindDestination(
                            caster.PosX, caster.PosZ,
                            lockTarget.PosX, lockTarget.PosZ,
                            motion.Distance,
                            out destX, out destZ, out faceX, out faceZ);
                    }
                    dirX = destX - caster.PosX;
                    dirZ = destZ - caster.PosZ;
                    dist = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (dist < 0.05f)
                    {
                        dirX = faceX;
                        dirZ = faceZ;
                        dist = motion.Distance;
                    }
                    SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
                    cast.DirX = faceX;
                    cast.DirZ = faceZ;
                }
            }

            switch (motion.Type)
            {
                case ESkillMotionClipType.Jump:
                    if (!caster.MoveState.IsGrounded)
                    {
                        SkillResultService.BroadcastSkillCancel(
                            caster, cast.SkillId, EBattle_SkillCancelNotifyReason.Illegal, cast.ClientTick);
                        return false;
                    }
                    caster.MoveState = MovementSimulator.ApplyJump(caster.MoveState, GameConstants.JumpForce);
                    if (session != null)
                    {
                        PlayerMovementSimulatorHost.SendReconcile(session, caster);
                        PlayerMovementSimulatorHost.BroadcastSnapshotExcept(session, caster);
                    }
                    return true;

                case ESkillMotionClipType.LinearDash:
                case ESkillMotionClipType.Blink:
                default:
                {
                    if (dist <= 0f)
                    {
                        SkillResultService.BroadcastSkillCancel(
                            caster, cast.SkillId, EBattle_SkillCancelNotifyReason.Illegal, cast.ClientTick);
                        return false;
                    }

                    caster.MoveState = MovementSimulator.ApplySkillMotion(caster.MoveState, dirX, dirZ, dist);
                    PlayerMovementSimulatorHost.ClearInputQueue(caster);

                    if (session != null)
                    {
                        PlayerMovementSimulatorHost.SendReconcile(session, caster);
                        PlayerMovementSimulatorHost.BroadcastSnapshotExcept(session, caster, hardSnap: true);
                    }
                    return true;
                }
            }
        }

        /// <summary>取出第几段有内容的位移数据。</summary>
        static bool TryGetMotionClip(SkillConfig def, int motionIndex, out SkillMotionPayload motion)
        {
            return TryGetMotionClip(def, motionIndex, out motion, out _);
        }

        static bool TryGetMotionClip(SkillConfig def, int motionIndex, out SkillMotionPayload motion, out float time)
        {
            motion = null;
            time = 0f;
            if (def?.Clips == null || motionIndex < 0) return false;
            int n = 0;
            for (int i = 0; i < def.Clips.Length; i++)
            {
                var m = def.Clips[i]?.Motion;
                if (m == null || !m.HasContent) continue;
                if (n == motionIndex)
                {
                    motion = m;
                    time = def.Clips[i].Time;
                    return true;
                }
                n++;
            }
            return false;
        }

        /// <summary>通知全员这次打中了谁、掉了多少血。</summary>
        public static void BroadcastHit(
            Entity caster,
            int skillId,
            uint hitIndex,
            SkillHitEntry[] hits)
        {
            if (caster == null) return;

            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_SkillHitPacket, new S2C_Battle_SkillHitPacket
            {
                CasterId = caster.Id,
                SkillId = skillId,
                HitIndex = hitIndex,
                Hits = hits ?? Array.Empty<SkillHitEntry>()
            });
        }

        /// <summary>水平方向转一个角度（单位：度）。</summary>
        static void RotateYaw(float dirX, float dirZ, float yawDegrees, out float outX, out float outZ)
        {
            double rad = yawDegrees * (Math.PI / 180.0);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            outX = dirX * cos - dirZ * sin;
            outZ = dirX * sin + dirZ * cos;
        }
    }
}
