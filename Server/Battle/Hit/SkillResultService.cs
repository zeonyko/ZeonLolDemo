using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>算伤害、判断能不能打、往外发包。</summary>
    public static class SkillResultService
    {
        /// <summary>单位自动攻击：看冷却、距离、敌我，再出手或放出弹道。</summary>
        public static void TryUnitAttack(Entity attacker, Entity target, int skillId, float serverTime)
        {
            if (attacker == null || target == null) return;
            if (attacker.Hp <= 0f || target.Hp <= 0f) return;
            if (!Entity.AreEnemies(attacker, target)) return;
            if (!attacker.State.CanAttack) return;

            var scene = attacker.Scene ?? WorldManager.Instance?.GetDefaultScene();
            if (scene != null && (scene.MatchEnded || !scene.MatchPlaying)) return;

            if (skillId <= 0)
                skillId = UnitCatalog.ResolveAttackSkillId(attacker.EntityType);
            if (skillId <= 0) return;

            // 和玩家一样按技能看冷却
            if (!SkillRules.IsCooldownReady(serverTime, attacker.GetLastCastTime(skillId), skillId))
                return;

            if (!SkillRules.IsInSkillRange(
                    attacker.PosX, attacker.PosY, attacker.PosZ,
                    target.PosX, target.PosY, target.PosZ,
                    skillId))
                return;

            attacker.SetLastCastTime(skillId, serverTime);

            var targetPos = Vector3Data.Of(target.PosX, target.PosY, target.PosZ);
            BroadcastSkillCast(attacker, skillId, targetPos, target.Id);

            var def = SkillCatalog.Get(skillId);
            bool wantsBolt = def != null && def.IsProjectile && def.ProjectileId != 0;

            if (wantsBolt)
            {
                // 出手延迟为 0 就立刻出弹
                float windup = SkillRules.GetCastWindup(def);
                float ready = serverTime + windup;

                if (scene != null)
                {
                    scene.ScheduleUnitHit(attacker.Id, target.Id, skillId, ready);
                    return;
                }
            }

            ApplyDamageAndNotify(attacker, target, skillId);
        }

        /// <summary>走动按 MonsterSnapshotInterval 进队；站住按 IdleSnapshotInterval。真正发包在帧末 SnapshotOutbox.Flush。</summary>
        public static void TryBroadcastUnitSnapshot(Entity unit, float dt)
        {
            if (unit == null) return;

            float serverTime = BattleSystem.Instance?.ServerTime ?? 0f;
            bool moved = !unit.HasBroadcastPos
                         || (unit.PosX - unit.LastBroadcastPosX) * (unit.PosX - unit.LastBroadcastPosX)
                         + (unit.PosY - unit.LastBroadcastPosY) * (unit.PosY - unit.LastBroadcastPosY)
                         + (unit.PosZ - unit.LastBroadcastPosZ) * (unit.PosZ - unit.LastBroadcastPosZ) > 0.0001f;

            if (moved)
            {
                unit.SnapshotTimer -= dt;
                if (unit.SnapshotTimer > 0f) return;
                unit.SnapshotTimer = GameConstants.MonsterSnapshotInterval;
            }
            else if (serverTime - unit.LastIdleSnapshotTime < GameConstants.IdleSnapshotInterval)
            {
                return;
            }

            SnapshotOutbox.Enqueue(unit);
            unit.LastBroadcastPosX = unit.PosX;
            unit.LastBroadcastPosY = unit.PosY;
            unit.LastBroadcastPosZ = unit.PosZ;
            unit.HasBroadcastPos = true;
            unit.LastIdleSnapshotTime = serverTime;
            unit.SnapshotTimer = GameConstants.MonsterSnapshotInterval;
        }

        /// <summary>这个人能不能被该技能打。没开战时只有木桩能挨打；塔/水晶/基地只吃普攻。</summary>
        public static bool IsValidSkillTarget(Entity caster, Entity target, int skillId = 0)
        {
            if (caster == null || target == null) return false;
            if (target.Hp <= 0f) return false;
            if (caster.Id == target.Id) return false;
            if (!Entity.AreEnemies(caster, target)) return false;
            if (!UnitCatalog.CanReceiveSkillDamage(target.EntityType, skillId))
                return false;

            var scene = caster.Scene ?? target.Scene ?? WorldManager.Instance?.GetDefaultScene();
            if (scene != null && !scene.MatchPlaying)
                return target.IsTrainingDummy;

            return target.IsMinion || target.IsStructure || target.IsMonster || target.IsPlayer;
        }

        /// <summary>按默认倍率扣血并通知打中了谁。</summary>
        public static void ApplyDamageAndNotify(
            Entity caster,
            Entity target,
            int skillId,
            uint hitIndex = 0,
            float damageMultiplier = 1f,
            string attackType = null)
        {
            if (caster == null || target == null) return;
            var payload = DamagePayload.DefaultInstant(damageMultiplier > 0f ? damageMultiplier : 1f);
            var hit = DamageSystem.Apply(caster, target, skillId, payload, attackType);
            SkillHitResolver.BroadcastHit(caster, skillId, hitIndex, new[] { hit });
        }

        /// <summary>给自己加血，并通过命中包把负数伤害发给客户端做治疗飘字。</summary>
        public static SkillHitEntry ApplyHeal(Entity target, float amount)
        {
            if (target == null || amount <= 0.05f)
            {
                return new SkillHitEntry
                {
                    TargetId = target != null ? target.Id : 0,
                    Damage = 0,
                    HitFlags = 0,
                    KnockbackDir = Vector3Data.Zero,
                    AttackType = ""
                };
            }

            float before = target.Hp;
            float maxHp = target.MaxHp > 1f ? target.MaxHp : GameConstants.DefaultMaxHp;
            target.Hp = Math.Min(maxHp, target.Hp + amount);
            int healed = (int)Math.Round(target.Hp - before);
            if (healed > 0)
                BroadcastHpSync(target);

            return new SkillHitEntry
            {
                TargetId = target.Id,
                Damage = -healed,
                HitFlags = 0,
                KnockbackDir = Vector3Data.Zero,
                AttackType = ""
            };
        }

        /// <summary>扣血。木桩血量不会低于 1。水晶在本方塔还在时打不动。</summary>
        public static SkillHitEntry ApplyDamage(
            Entity caster,
            Entity target,
            int skillId,
            float damage,
            string attackType)
        {
            if (target == null)
            {
                return new SkillHitEntry
                {
                    TargetId = 0,
                    Damage = 0,
                    HitFlags = 0,
                    KnockbackDir = Vector3Data.Zero,
                    AttackType = ""
                };
            }

            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            if (damage < 0f) damage = 0f;

            uint hitFlags = (uint)EBattle_HitFlags.None;
            var scene = target.Scene ?? WorldManager.Instance?.GetDefaultScene();
            var atkDef = SkillCatalog.Get(skillId);

            // 水晶在本方防御塔还在时打不动
            bool structureInvuln = target.IsCrystal
                && scene != null
                && scene.IsTowerAlive(target.TeamId);

            // 塔/水晶/基地等只吃普攻
            bool profileDamageBlocked = damage > 0f
                && !UnitCatalog.CanReceiveSkillDamage(target.EntityType, atkDef);

            bool invincible = structureInvuln
                              || target.State.HasState(EntityStateTag.Invincible);

            if (invincible || profileDamageBlocked)
            {
                if (invincible)
                    hitFlags |= (uint)EBattle_HitFlags.Invincible;
                damage = 0f;
            }
            else
            {
                target.Hp = Math.Max(0f, target.Hp - damage);
                if (target.IsTrainingDummy && target.Hp < 1f)
                    target.Hp = 1f;
            }

            int casterType = caster?.EntityType ?? 0;
            if (profileDamageBlocked
                || UnitCatalog.SuppressHitFeedback(casterType, target.EntityType))
            {
                hitFlags |= (uint)EBattle_HitFlags.NoFeedback;
            }

            if (damage > 0f && caster != null)
            {
                caster.LastDealtDamageTargetId = target.Id;
                caster.LastDealtDamageAt = now;
                if (target.Hp > 0f)
                {
                    target.LastDamagedById = caster.Id;
                    target.LastDamagedByAt = now;
                }

                if (caster.IsPlayer && target.IsPlayer && Entity.AreEnemies(caster, target))
                    caster.LastDamagedEnemyPlayerAt = now;

                // 英雄打英雄 / 单位打小兵 → 周围敌方小兵转火
                if (scene != null && target.Hp > 0f)
                    MinionAi.OnUnitDamaged(caster, target, scene, now);
            }

            if (target.Hp <= 0f)
                hitFlags |= (uint)EBattle_HitFlags.Lethal;

            if (string.IsNullOrEmpty(attackType))
                attackType = atkDef?.PrimaryHit?.AttackType ?? AttackStyles.LightSlash;

            float kbX = 0f;
            float kbZ = 1f;
            if (caster != null)
            {
                kbX = target.PosX - caster.PosX;
                kbZ = target.PosZ - caster.PosZ;
                SkillRules.NormalizeHorizontal(ref kbX, ref kbZ);
            }

            var hit = new SkillHitEntry
            {
                TargetId = target.Id,
                Damage = (int)Math.Round(damage),
                HitFlags = hitFlags,
                KnockbackDir = Vector3Data.Of(kbX, 0f, kbZ),
                AttackType = attackType ?? ""
            };

            if (target.Hp > 0f)
            {
                BroadcastHpSync(target);
                return hit;
            }

            // 打死了：玩家进复活；别人清 Buff、从场上拿走
            if (target.IsPlayer)
            {
                float matchElapsed = scene?.MatchElapsed ?? 0f;
                target.RespawnAt = now + GameConstants.CalcPlayerRespawnDelay(matchElapsed);
                target.AggroTargetId = 0;
                BuffService.Instance.ClearEntity(target.Id);
                BroadcastHpSync(target);
                BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
                {
                    EntityId = target.Id,
                    StateType = (int)EEntityStateType.Dead,
                    EntityData = target.ToEntityData()
                });
                return hit;
            }

            BuffService.Instance.ClearEntity(target.Id);
            scene?.OnMonsterDead(target);
            scene?.RemoveEntity(target.Id);

            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
            {
                EntityId = target.Id,
                StateType = (int)EEntityStateType.Dead,
                EntityData = null
            });

            return hit;
        }

        /// <summary>把血量等属性发给大家。</summary>
        public static void BroadcastHpSync(Entity entity)
        {
            if (entity == null) return;
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
            {
                EntityId = entity.Id,
                StateType = (int)EEntityStateType.AttrSync,
                EntityData = entity.ToEntityData()
            });
        }

        /// <summary>打包当前状态（位置、速度、状态标记）。</summary>
        public static EntitySnapshot ToEntitySnapshot(Entity entity, bool hardSnap = false)
        {
            if (entity == null) return null;
            return new EntitySnapshot
            {
                EntityId = entity.Id,
                Transform = TransformData.Of(entity.PosX, entity.PosY, entity.PosZ),
                Velocity = Vector3Data.Of(0f, entity.MoveState.VelY, 0f),
                StateFlags = (uint)entity.State.CurrentStateMask,
                HardSnap = hardSnap ? 1 : 0
            };
        }

        /// <summary>把某个单位的当前状态记进本帧快照队列。</summary>
        public static void BroadcastEntitySnapshot(Entity entity, bool hardSnap = false)
        {
            SnapshotOutbox.Enqueue(entity, hardSnap);
        }

        /// <summary>通知全员有人开始放技能。</summary>
        public static void BroadcastSkillCast(
            Entity caster,
            int skillId,
            Vector3Data targetPos,
            long targetEntityId)
        {
            if (caster == null) return;
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_SkillCastPacket, new S2C_Battle_SkillCastPacket
            {
                CasterId = caster.Id,
                SkillId = skillId,
                ServerTick = BattleSystem.Instance?.ServerTick ?? 0,
                TargetPosition = targetPos ?? Vector3Data.Zero,
                TargetId = targetEntityId
            });
        }

        /// <summary>通知全员技能被取消或打断。</summary>
        public static void BroadcastSkillCancel(
            Entity caster,
            int skillId,
            EBattle_SkillCancelNotifyReason reason,
            uint clientTick = 0)
        {
            if (caster == null) return;
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_SkillCancelPacket, new S2C_Battle_SkillCancelPacket
            {
                CasterId = caster.Id,
                SkillId = skillId,
                CancelReason = (uint)reason,
                InterruptTick = BattleSystem.Instance?.ServerTick ?? 0,
                ClientTick = clientTick
            });
        }

        /// <summary>强制把人拉到服务器位置（出生、击退等）。</summary>
        public static void BroadcastForceRelocate(Entity entity, EBattle_RelocateReason reason)
        {
            if (entity == null) return;
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_ForceRelocatePacket, new S2C_Battle_ForceRelocatePacket
            {
                EntityId = entity.Id,
                TargetTf = TransformData.Of(entity.PosX, entity.PosY, entity.PosZ),
                RelocateReason = (uint)reason
            });
        }
    }
}
