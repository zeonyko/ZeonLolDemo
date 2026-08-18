using System;
using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>管地面持续区域：出手时放下，之后按间隔在圈里算效果。</summary>
    public sealed class AreaService
    {
        public static AreaService Instance { get; } = new AreaService();

        /// <summary>一块已经放下的地面区域（中心和朝向放下时就定了）。</summary>
        public sealed class AreaState
        {
            public long CasterId;
            public int SkillId;
            public int SceneId;
            public float CenterX;
            public float CenterY;
            public float CenterZ;
            public float DirX;
            public float DirZ;
            public float ExpireAt;
            public float NextTickAt;
            public float TickInterval;
            public SkillShapePayload Shape;
            public HitEffectPayload TickEffect;
            public string AttackType;
            /// <summary>已经跳了几次，也当命中段号用。</summary>
            public uint TickIndex;
        }

        private readonly List<AreaState> _areas = new List<AreaState>(8);
        private readonly List<TargetSelectorResolver.AnchorPose> _anchors =
            new List<TargetSelectorResolver.AnchorPose>(4);

        /// <summary>清空全部区域（对局重置）。</summary>
        public void Clear() => _areas.Clear();

        /// <summary>清掉某人某个技能留下的地面区域。</summary>
        public void CancelSkill(long casterId, int skillId)
        {
            for (int i = _areas.Count - 1; i >= 0; i--)
            {
                if (_areas[i].CasterId == casterId && _areas[i].SkillId == skillId)
                    _areas.RemoveAt(i);
            }
        }

        /// <summary>清掉某人留下的全部地面区域。断线时立刻调，别等过期。</summary>
        public void CancelCaster(long casterId)
        {
            for (int i = _areas.Count - 1; i >= 0; i--)
            {
                if (_areas[i].CasterId == casterId)
                    _areas.RemoveAt(i);
            }
        }

        /// <summary>按落点规则放下地面区域。动态选人可在多人脚下各放一圈。</summary>
        public void Spawn(
            Entity caster,
            Scene scene,
            SkillConfig def,
            PendingCastService.PendingCastState cast,
            SkillAreaPayload area,
            string attackType)
        {
            if (caster == null || scene == null || def == null || area == null || !area.HasContent)
                return;

            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            if (area.ReplaceSame)
                CancelSkill(caster.Id, def.SkillId);

            TargetSelectorResolver.ResolveAnchors(caster, scene, cast, area.Anchor, _anchors);
            // 放下时把中心定死；动态选人可在多人脚下各放一圈
            for (int i = 0; i < _anchors.Count; i++)
            {
                var pose = _anchors[i];
                AddArea(caster, scene, def, area, pose.PosX, pose.PosY, pose.PosZ,
                    pose.DirX, pose.DirZ, attackType, now);
            }
        }

        /// <summary>在指定地点直接放下（弹道落地等）。</summary>
        public void SpawnAt(
            Entity caster,
            Scene scene,
            SkillConfig def,
            SkillAreaPayload area,
            float cx, float cy, float cz,
            float dirX, float dirZ,
            string attackType)
        {
            if (caster == null || scene == null || def == null || area == null || !area.HasContent)
                return;
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            if (area.ReplaceSame)
                CancelSkill(caster.Id, def.SkillId);
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
            AddArea(caster, scene, def, area, cx, cy, cz, dirX, dirZ, attackType, now);
        }

        /// <summary>登记一块区域。没填周期效果时，按 0.5 倍瞬时伤。</summary>
        void AddArea(
            Entity caster,
            Scene scene,
            SkillConfig def,
            SkillAreaPayload area,
            float cx, float cy, float cz,
            float dx, float dz,
            string attackType,
            float now)
        {
            string atk = attackType ?? "";
            // TickFeedback 存在时以其为准（空字符串 = 跳过受击反馈）
            if (area.TickFeedback != null)
                atk = area.TickFeedback.AttackType ?? "";

            var state = new AreaState
            {
                CasterId = caster.Id,
                SkillId = def.SkillId,
                SceneId = scene.SceneId,
                CenterX = cx,
                CenterY = cy,
                CenterZ = cz,
                DirX = dx,
                DirZ = dz,
                ExpireAt = now + area.Duration,
                NextTickAt = now,
                TickInterval = area.TickInterval,
                Shape = area.Shape,
                TickEffect = area.TickEffect ?? new HitEffectPayload
                {
                    Damage = DamagePayload.DefaultInstant(0.5f)
                },
                AttackType = atk,
                TickIndex = 0
            };
            _areas.Add(state);
            float r = area.Shape?.SizeX ?? 0f;
            Console.WriteLine(
                $"[Area] Spawn skill={def.SkillId} caster={caster.Id} " +
                $"dur={area.Duration:F1} tick={area.TickInterval:F2} r={r:F1} " +
                $"at=({cx:F1},{cz:F1})");
        }

        /// <summary>推进全部区域。掉帧时把错过的次数补上，但不超过结束时间。</summary>
        public void Tick(float now)
        {
            if (_areas.Count == 0) return;
            var world = BattleSystem.Instance?.World;
            if (world == null) return;

            for (int i = _areas.Count - 1; i >= 0; i--)
            {
                var a = _areas[i];
                if (now >= a.ExpireAt)
                {
                    _areas.RemoveAt(i);
                    continue;
                }

                var scene = world.GetScene(a.SceneId);
                var caster = scene?.GetEntity(a.CasterId);
                if (scene == null || caster == null || caster.Hp <= 0f)
                {
                    _areas.RemoveAt(i);
                    continue;
                }

                while (now + 0.0001f >= a.NextTickAt && a.NextTickAt <= a.ExpireAt + 0.0001f)
                {
                    ResolveTick(caster, scene, a);
                    a.TickIndex++;
                    a.NextTickAt += a.TickInterval;
                    if (a.NextTickAt > a.ExpireAt + 0.0001f)
                        break;
                }
            }
        }

        /// <summary>跳一次：圈里敌人算伤害，圈里友军补 Buff。</summary>
        static void ResolveTick(Entity caster, Scene scene, AreaState a)
        {
            var shape = a.Shape;
            if (shape == null) return;
            var shapes = new[] { shape };
            var candidates = ShapeHitService.Collect(
                caster, scene, shapes, a.CenterX, a.CenterY, a.CenterZ, a.DirX, a.DirZ, a.SkillId);

            var hits = new List<SkillHitEntry>(candidates.Count);
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            var effect = a.TickEffect ?? new HitEffectPayload
            {
                Damage = DamagePayload.DefaultInstant(0.5f)
            };
            var damage = effect.ResolveDamage();
            var buffs = effect.TargetBuffs ?? Array.Empty<BuffApplySpec>();
            var seen = new HashSet<long>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i].Target;
                if (target == null || !seen.Add(target.Id)) continue;
                if (damage != null && (damage.Coefficient > 0.05f || damage.BaseDamage > 0.05f))
                    hits.Add(DamageSystem.Apply(
                        caster, target, a.SkillId, damage, a.AttackType));
                BuffService.Instance.ApplyPayloads(caster, target, buffs, a.SkillId, now);
            }

            // 形状只扫敌人；友军/自己要再按圈补 Buff
            if (buffs.Length > 0)
            {
                foreach (var e in scene.GetAllEntities())
                {
                    if (e == null || e.Hp <= 0f || !seen.Add(e.Id)) continue;
                    if (!ShapeOverlap.ContainsWorldPoint(
                            shape, a.CenterX, a.CenterY, a.CenterZ, a.DirX, a.DirZ, e.PosX, e.PosZ))
                        continue;
                    BuffService.Instance.ApplyPayloads(caster, e, buffs, a.SkillId, now);
                }
            }

            SkillHitResolver.BroadcastHit(
                caster, a.SkillId, a.TickIndex, hits.ToArray());
        }
    }
}
