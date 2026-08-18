using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>剧情角色占位。播放时对上真实角色 Id。</summary>
    public enum EReplayActor
    {
        A = 0,
        B = 1,
        C = 2,
    }

    #region 剧情上下文：角色映射 / 临时状态
    /// <summary>剧情/回放上下文：角色映射与临时状态（如躲避）。</summary>
    public class StoryContext
    {
        public long IdA { get; private set; }
        public long IdB { get; private set; }
        public long IdC { get; private set; }
        public Vector3 Origin { get; private set; }

        private readonly HashSet<long> _dodgingUntilClear = new HashSet<long>();

        public void Bind(long idA, long idB, long idC, Vector3 origin)
        {
            IdA = idA;
            IdB = idB;
            IdC = idC;
            Origin = origin;
            _dodgingUntilClear.Clear();
        }

        public long Resolve(EReplayActor actor)
        {
            switch (actor)
            {
                case EReplayActor.A: return IdA;
                case EReplayActor.B: return IdB;
                case EReplayActor.C: return IdC;
                default: return 0;
            }
        }

        public Entity GetEntity(EReplayActor actor)
        {
            long id = Resolve(actor);
            return id == 0 ? null : EntityManager.Instance?.GetEntity(id);
        }

        public void SetDodging(EReplayActor actor, bool dodging)
        {
            long id = Resolve(actor);
            if (id == 0) return;
            if (dodging) _dodgingUntilClear.Add(id);
            else _dodgingUntilClear.Remove(id);
        }

        public bool IsDodging(EReplayActor actor)
        {
            long id = Resolve(actor);
            return id != 0 && _dodgingUntilClear.Contains(id);
        }

        public Vector3 GetPos(EReplayActor actor)
        {
            return GetEntity(actor)?.GetComponent<TransformComponent>()?.Position ?? Origin;
        }

        public float GetHp(EReplayActor actor)
        {
            long id = Resolve(actor);
            return id != 0 && BattleCache.Instance != null ? BattleCache.Instance.GetHp(id) : 0f;
        }
    }
    #endregion

    #region 剧情指令基类
    /// <summary>剧情指令。AtTime 是第几秒触发。</summary>
    public abstract class StoryCommand
    {
        public float AtTime { get; }

        protected StoryCommand(float atTime) => AtTime = atTime;

        public abstract void Execute(StoryContext ctx);
    }
    #endregion

    #region 具体指令
    /// <summary>旁白（Console 彩色日志，方便对照时间轴）。</summary>
    public sealed class StoryNarrateCommand : StoryCommand
    {
        public string Text { get; }

        public StoryNarrateCommand(float atTime, string text) : base(atTime)
        {
            Text = text ?? "";
        }

        public override void Execute(StoryContext ctx)
        {
            Debug.Log($"<color=#FFD54F>[剧情] t={AtTime:F2} {Text}</color>");
        }
    }

    /// <summary>占位到指定时刻，让死亡/后摇动画播完再清场。</summary>
    public sealed class StoryHoldCommand : StoryCommand
    {
        public StoryHoldCommand(float atTime) : base(atTime) { }

        public override void Execute(StoryContext ctx) { }
    }

    /// <summary>让角色朝向另一角色（仅改 View 朝向）。</summary>
    public sealed class StoryFaceCommand : StoryCommand
    {
        public EReplayActor Actor { get; }
        public EReplayActor LookAt { get; }

        public StoryFaceCommand(float atTime, EReplayActor actor, EReplayActor lookAt) : base(atTime)
        {
            Actor = actor;
            LookAt = lookAt;
        }

        public override void Execute(StoryContext ctx)
        {
            StoryOps.Face(ctx, Actor, LookAt);
        }
    }

    /// <summary>按开场原点平移角色逻辑坐标，画面立刻贴齐。</summary>
    public sealed class StorySetPosCommand : StoryCommand
    {
        public EReplayActor Actor { get; }
        public float OffsetX { get; }
        public float OffsetZ { get; }

        public StorySetPosCommand(float atTime, EReplayActor actor, float offsetX, float offsetZ) : base(atTime)
        {
            Actor = actor;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
        }

        public override void Execute(StoryContext ctx)
        {
            var entity = ctx.GetEntity(Actor);
            var transform = entity?.GetComponent<TransformComponent>();
            if (transform == null) return;

            Vector3 pos = ctx.Origin + new Vector3(OffsetX, 0f, OffsetZ);
            pos.y = GameConstants.GroundY;
            transform.SnapPosition(pos);
            entity.GetComponent<ViewComponent>()?.SnapToLogic();
        }
    }

    /// <summary>按最大生命百分比改血。用于残血开场。</summary>
    public sealed class StorySetHpCommand : StoryCommand
    {
        public EReplayActor Actor { get; }
        public float Ratio { get; }

        public StorySetHpCommand(float atTime, EReplayActor actor, float ratio) : base(atTime)
        {
            Actor = actor;
            Ratio = Mathf.Clamp01(ratio);
        }

        public override void Execute(StoryContext ctx)
        {
            long id = ctx.Resolve(Actor);
            var cache = BattleCache.Instance;
            if (id == 0 || cache == null) return;
            float max = cache.GetMaxHp(id);
            cache.SetHp(id, max * Ratio);
        }
    }

    /// <summary>打断当前施法，回到待机/走位。</summary>
    public sealed class StoryAbortCastCommand : StoryCommand
    {
        public EReplayActor Actor { get; }

        public StoryAbortCastCommand(float atTime, EReplayActor actor) : base(atTime)
        {
            Actor = actor;
        }

        public override void Execute(StoryContext ctx)
        {
            var entity = ctx.GetEntity(Actor);
            entity?.GetComponent<SkillCastComponent>()?.AbortCast();
            entity?.GetComponent<StateComponent>()?.ResolveLocomotion();
            Debug.Log($"[剧情] t={AtTime:F2} AbortCast {Actor}");
        }
    }

    /// <summary>播放施法起手表现：走本地 Timeline。剧情允许逻辑位移（闪现/冲撞/三连斩）。</summary>
    public sealed class StoryCastStartCommand : StoryCommand
    {
        public EReplayActor Caster { get; }
        public EReplayActor LookAt { get; }
        public int SkillId { get; }
        public bool InvertDir { get; }
        public bool HasLookAt { get; }

        public StoryCastStartCommand(float atTime, EReplayActor caster, int skillId = GameConstants.SkillId_NormalAttack)
            : base(atTime)
        {
            Caster = caster;
            LookAt = caster;
            SkillId = skillId;
            InvertDir = false;
            HasLookAt = false;
        }

        public StoryCastStartCommand(
            float atTime,
            EReplayActor caster,
            EReplayActor lookAt,
            int skillId,
            bool invertDir = false)
            : base(atTime)
        {
            Caster = caster;
            LookAt = lookAt;
            SkillId = skillId;
            InvertDir = invertDir;
            HasLookAt = true;
        }

        public override void Execute(StoryContext ctx)
        {
            var caster = ctx.GetEntity(Caster);
            var skill = caster?.GetComponent<SkillCastComponent>();
            if (skill == null) return;

            Vector3 casterPos = ctx.GetPos(Caster);
            Vector3 dir = Vector3.forward;
            CastPresentationTarget target = CastPresentationTarget.None;

            if (HasLookAt)
            {
                Vector3 lookPos = ctx.GetPos(LookAt);
                dir = lookPos - casterPos;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                else dir.Normalize();
                if (InvertDir) dir = -dir;

                Vector3 aimPos = InvertDir
                    ? casterPos + dir * Mathf.Max(1f, SkillRules.GetRange(SkillId))
                    : lookPos;
                long aimId = InvertDir ? 0 : ctx.Resolve(LookAt);
                target = CastPresentationTarget.Point(aimPos, aimId);
            }

            skill.PlayLocalCastTimeline(SkillId, dir, target, allowLogical: true);
            Debug.Log($"[剧情] t={AtTime:F2} CastStart {Caster} skill={SkillId}");
        }
    }

    /// <summary>攻击命中：对方在躲就 Miss；致死则走死亡表现，播完后移除。</summary>
    public sealed class StoryAttackHitCommand : StoryCommand
    {
        public EReplayActor Caster { get; }
        public EReplayActor Target { get; }
        public int SkillId { get; }
        public uint HitIndex { get; }
        public bool Lethal { get; }
        public float? OverrideDamage { get; }

        public StoryAttackHitCommand(
            float atTime,
            EReplayActor caster,
            EReplayActor target,
            int skillId = GameConstants.SkillId_NormalAttack,
            uint hitIndex = 0,
            bool lethal = false,
            float? overrideDamage = null)
            : base(atTime)
        {
            Caster = caster;
            Target = target;
            SkillId = skillId;
            HitIndex = hitIndex;
            Lethal = lethal;
            OverrideDamage = overrideDamage;
        }

        public override void Execute(StoryContext ctx)
        {
            var targetEntity = ctx.GetEntity(Target);
            if (targetEntity == null) return;

            long casterId = ctx.Resolve(Caster);
            long targetId = ctx.Resolve(Target);
            Vector3 casterPos = ctx.GetPos(Caster);
            Vector3 targetPos = ctx.GetPos(Target);

            Vector3 kb = targetPos - casterPos;
            kb.y = 0f;
            if (kb.sqrMagnitude > 0.0001f) kb.Normalize();
            else kb = Vector3.forward;

            if (ctx.IsDodging(Target))
            {
                SkillHitPresenter.Process(new SkillHitContext
                {
                    CasterId = casterId,
                    TargetId = targetId,
                    SkillId = SkillId,
                    HitIndex = HitIndex,
                    Accepted = true,
                    Damage = 0f,
                    CasterPos = casterPos,
                    HitFlags = (uint)EBattle_HitFlags.Dodge,
                    KnockbackDir = kb,
                    AttackType = AttackStyles.LightSlash
                });
                Debug.Log($"[剧情] t={AtTime:F2} AttackHit {Caster}->{Target} MISS(dodge)");
                return;
            }

            float hp = ctx.GetHp(Target);
            float damage = OverrideDamage ?? SkillRules.GetAttackDamage(SkillId);
            float newHp;
            uint flags = 0;
            if (Lethal)
            {
                damage = Mathf.Max(damage, hp);
                newHp = 0f;
                flags = (uint)EBattle_HitFlags.Lethal;
            }
            else
            {
                newHp = Mathf.Max(1f, hp - damage);
            }

            BattleCache.Instance?.SetHp(targetId, newHp);

            var def = SkillCatalog.Get(SkillId);
            string attackType = AttackStyles.LightSlash;
            if (SkillClipUtil.TryGetHitByIndex(SkillId, HitIndex, out var hitPayload)
                && hitPayload != null
                && !string.IsNullOrEmpty(hitPayload.AttackType))
            {
                attackType = hitPayload.AttackType;
            }
            else if (def?.PrimaryHit != null)
            {
                attackType = def.PrimaryHit.AttackType ?? AttackStyles.LightSlash;
            }

            SkillHitPresenter.Process(new SkillHitContext
            {
                CasterId = casterId,
                TargetId = targetId,
                SkillId = SkillId,
                HitIndex = HitIndex,
                Accepted = true,
                Damage = damage,
                CasterPos = casterPos,
                HitFlags = flags,
                KnockbackDir = kb,
                AttackType = attackType
            });

            if (Lethal)
            {
                targetEntity.GetComponent<CombatViewComponent>()?.PlayDeath(() =>
                {
                    BattleCache.Instance?.Discard(targetId);
                    EntityManager.Instance?.RemoveEntity(targetId);
                });
            }

            Debug.Log($"[剧情] t={AtTime:F2} AttackHit {Caster}->{Target} dmg={damage} hp={newHp} lethal={Lethal}");
        }
    }

    /// <summary>开关躲避。后面打中时用来判 Miss。</summary>
    public sealed class StoryDodgeCommand : StoryCommand
    {
        public EReplayActor Actor { get; }
        public bool Enable { get; }

        public StoryDodgeCommand(float atTime, EReplayActor actor, bool enable = true) : base(atTime)
        {
            Actor = actor;
            Enable = enable;
        }

        public override void Execute(StoryContext ctx)
        {
            ctx.SetDodging(Actor, Enable);
            Debug.Log($"[剧情] t={AtTime:F2} Dodge {Actor}={(Enable ? "ON" : "OFF")}");
        }
    }
    #endregion

    #region 组合指令辅助
    /// <summary>剧本编写辅助：常用指令组合的快捷方法。</summary>
    public static class StoryOps
    {
        public static void Face(StoryContext ctx, EReplayActor actor, EReplayActor lookAt)
        {
            var self = ctx.GetEntity(actor);
            var view = self?.GetComponent<ViewComponent>();
            if (view == null) return;

            Vector3 from = ctx.GetPos(actor);
            Vector3 to = ctx.GetPos(lookAt);
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            view.SnapFace(dir);
        }

        /// <summary>配表第 N 段命中时刻；没有则回退默认前摇。</summary>
        public static float HitTime(int skillId, uint hitIndex = 0)
        {
            if (SkillCatalog.TryGet(skillId, out var def) && def?.Clips != null)
            {
                uint n = 0;
                for (int i = 0; i < def.Clips.Length; i++)
                {
                    var clip = def.Clips[i];
                    if (clip?.Hit == null || !clip.Hit.HasContent) continue;
                    if (n == hitIndex)
                        return clip.Time;
                    n++;
                }
            }

            return SkillRules.GetEffectiveHitDelay(skillId);
        }

        /// <summary>弹道：出弹时刻 + 飞行时间。</summary>
        public static float ProjectileImpactDelay(int skillId, float distance)
        {
            float spawn = HitTime(skillId, 0);
            int projId = 0;
            if (SkillCatalog.TryGet(skillId, out var def))
                projId = SkillClipUtil.ResolvePrimaryProjectileId(def?.Clips);
            float speed = 16f;
            if (projId != 0 && ProjectileCatalog.TryGet(projId, out var proj) && proj != null && proj.Speed > 0.1f)
                speed = proj.Speed;
            return spawn + Mathf.Max(0f, distance) / speed;
        }

        public static void AddSwingAndHit(
            List<StoryCommand> list,
            float castTime,
            EReplayActor caster,
            EReplayActor target,
            int skillId = GameConstants.SkillId_NormalAttack,
            uint hitIndex = 0,
            bool lethal = false)
        {
            list.Add(new StoryCastStartCommand(castTime, caster, target, skillId));
            list.Add(new StoryAttackHitCommand(
                castTime + HitTime(skillId, hitIndex),
                caster,
                target,
                skillId,
                hitIndex,
                lethal));
        }

        /// <summary>一次起手，按配表打出全部命中段。</summary>
        public static void AddAllHits(
            List<StoryCommand> list,
            float castTime,
            EReplayActor caster,
            EReplayActor target,
            int skillId,
            bool lastLethal = false)
        {
            list.Add(new StoryCastStartCommand(castTime, caster, target, skillId));
            if (!SkillCatalog.TryGet(skillId, out var def) || def?.Clips == null)
            {
                list.Add(new StoryAttackHitCommand(castTime + HitTime(skillId), caster, target, skillId));
                return;
            }

            uint idx = 0;
            uint lastIdx = 0;
            var hits = new List<(uint Index, float Time)>(4);
            for (int i = 0; i < def.Clips.Length; i++)
            {
                var clip = def.Clips[i];
                if (clip?.Hit == null || !clip.Hit.HasContent) continue;
                hits.Add((idx, clip.Time));
                lastIdx = idx;
                idx++;
            }

            if (hits.Count == 0)
            {
                list.Add(new StoryAttackHitCommand(castTime + HitTime(skillId), caster, target, skillId));
                return;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                bool lethal = lastLethal && hits[i].Index == lastIdx;
                list.Add(new StoryAttackHitCommand(
                    castTime + hits[i].Time,
                    caster,
                    target,
                    skillId,
                    hits[i].Index,
                    lethal));
            }
        }
    }
    #endregion
}
