using System.Text;
using Client.Network;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗 GM / 一键快照用的只读拼装。壳通过 DevicePerf 钩子拿文本，不引用本类型。</summary>
    public static class BattleGmSnapshot
    {
        public static string DumpLines()
        {
            var sb = new StringBuilder(768);
            sb.AppendLine(BuildStatePlain());
            sb.AppendLine(BuildConfigPlain());
            sb.AppendLine(BuildVfxPlain());
            sb.AppendLine(NetTrace.Format());
            var nm = NetworkManager.Instance;
            if (nm != null)
                sb.Append("流量  ↑").Append(FormatBytes(nm.TotalSentBytes))
                    .Append(" ↓").Append(FormatBytes(nm.TotalReceivedBytes));
            return sb.ToString().TrimEnd();
        }

        public static string PerfLines()
        {
            return BuildConfigRich() + "\n" + BuildVfxPlain();
        }

        public static string StatePageText()
        {
            return BuildStatePlain() + "\n" + BuildConfigPlain() + "\n" + BuildVfxPlain();
        }

        static string BuildStatePlain()
        {
            var battle = BattleSystem.Instance;
            long id = battle != null ? battle.LocalPlayerId : 0;
            var cache = BattleCache.Instance;
            float hp = cache != null && id != 0 ? cache.GetHp(id) : 0f;
            float maxHp = cache != null && id != 0 ? cache.GetMaxHp(id) : 0f;

            Vector3 pos = Vector3.zero;
            var entity = id != 0 ? EntityManager.Instance?.GetEntity(id) : null;
            var tf = entity?.GetComponent<TransformComponent>();
            if (tf != null)
                pos = tf.Position;
            else if (cache != null && cache.TryGet(id, out var data) && data != null)
                pos = new Vector3(data.PosX, data.PosY, data.PosZ);

            var skill = entity?.GetComponent<SkillCastComponent>();
            int skillId = skill != null ? skill.PendingSkillId : 0;
            string skillName = "-";
            float cd = 0f;
            if (skillId != 0)
            {
                var cfg = SkillCatalog.Get(skillId);
                skillName = cfg != null ? cfg.Name : skillId.ToString();
                cd = skill.GetCooldownRemaining(skillId);
            }

            string deny = skill != null ? skill.LastDenyReason : "-";
            long lockId = SkillTargeting.AttackTargetId;
            string lockInfo = lockId == 0 ? "无" : lockId.ToString();
            if (lockId != 0 && cache != null && cache.TryGet(lockId, out var lockData) && lockData != null)
                lockInfo = $"{lockId} {lockData.Name} hp={lockData.Hp:F0}/{lockData.MaxHp:F0}";

            CountEntities(out int heroes, out int minions, out int towers, out int others, out int total);

            return
                $"本地  id={id}  hp={hp:F0}/{maxHp:F0}  pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})\n" +
                $"{FormatFsm(entity?.GetComponent<StateComponent>())}\n" +
                $"技能  {(skill != null && skill.IsCasting ? "施法中" : "空闲")}  {skillName}({skillId})  cd={cd:F1}s\n" +
                $"拒绝  {deny}\n" +
                $"锁定  {lockInfo}\n" +
                $"实体  共{total}  英雄={heroes}  兵={minions}  塔={towers}  其它={others}";
        }

        static string FormatFsm(StateComponent state)
        {
            if (state == null)
                return "状态机  -";

            string fsm = state.CurrentType switch
            {
                EEntityFsmState.Idle => "待机",
                EEntityFsmState.Move => "移动",
                EEntityFsmState.Skill => "技能",
                EEntityFsmState.Stun => "眩晕",
                _ => state.CurrentType.ToString()
            };

            string tags = FormatStateTags(state.CurrentStateMask);
            string move = state.CanMove ? "可移" : "禁移";
            string atk = state.CanAttack ? "可攻" : "禁攻";
            string slow = state.HasState(EntityStateTag.Slow)
                ? $"  移速×{state.MoveSpeedMultiplier:F2}"
                : "";
            return $"状态机  {fsm}  标记={tags}  {move}  {atk}{slow}";
        }

        static string FormatStateTags(EntityStateTag mask)
        {
            if (mask == EntityStateTag.None)
                return "无";

            var sb = new StringBuilder();
            AppendTag(sb, mask, EntityStateTag.Root, "定身");
            AppendTag(sb, mask, EntityStateTag.Stun, "眩晕");
            AppendTag(sb, mask, EntityStateTag.Silence, "沉默");
            AppendTag(sb, mask, EntityStateTag.Disarm, "缴械");
            AppendTag(sb, mask, EntityStateTag.Slow, "减速");
            AppendTag(sb, mask, EntityStateTag.Invincible, "无敌");
            AppendTag(sb, mask, EntityStateTag.Unstoppable, "霸体");
            AppendTag(sb, mask, EntityStateTag.Untargetable, "不可选");
            AppendTag(sb, mask, EntityStateTag.Casting, "施法");
            AppendTag(sb, mask, EntityStateTag.Blind, "致盲");
            AppendTag(sb, mask, EntityStateTag.Airborne, "击飞");
            return sb.Length > 0 ? sb.ToString() : mask.ToString();
        }

        static void AppendTag(StringBuilder sb, EntityStateTag mask, EntityStateTag tag, string name)
        {
            if ((mask & tag) == 0)
                return;
            if (sb.Length > 0)
                sb.Append(',');
            sb.Append(name);
        }

        static string BuildConfigPlain()
        {
            int skills = SkillCatalog.All.Count;
            int units = UnitCatalog.All.Count;
            int views = UnitViewCatalog.Count;
            int buffs = BuffCatalog.All.Count;
            int proj = ProjectileCatalog.All.Count;
            bool ready = ConfigService.IsReady;
            string miss = ready && skills > 0 && units > 0 ? "" : "  缺表";
            return $"配置  skills={skills} units={units} views={views} buffs={buffs} proj={proj} ready={(ready ? "yes" : "NO")}{miss}";
        }

        static string BuildConfigRich()
        {
            return
                "配置         " + Mark("skills", SkillCatalog.All.Count) + "  " +
                Mark("units", UnitCatalog.All.Count) + "  " +
                Mark("views", UnitViewCatalog.Count) + "  " +
                Mark("buffs", BuffCatalog.All.Count) + "  " +
                Mark("proj", ProjectileCatalog.All.Count) + "  " +
                (ConfigService.IsReady ? "ready=yes" : "<color=#ed6666>ready=NO 缺表</color>");
        }

        static string Mark(string name, int n)
        {
            return n > 0 ? name + "=" + n : "<color=#ed6666>" + name + "=" + n + " 缺表</color>";
        }

        static string BuildVfxPlain()
        {
            var vfx = BattleScene.Vfx;
            int children = vfx != null ? vfx.childCount : 0;
            return $"特效  Vfx子物体={children}  一次性={VfxLod.OneShotAlive}";
        }

        static void CountEntities(out int heroes, out int minions, out int towers, out int others, out int total)
        {
            heroes = minions = towers = others = total = 0;
            var cache = BattleCache.Instance;
            if (cache == null)
                return;

            foreach (var kv in cache.GetAll())
            {
                var data = kv.Value;
                if (data == null)
                    continue;
                total++;
                switch (data.EntityType)
                {
                    case EEntityType.Player:
                        heroes++;
                        break;
                    case EEntityType.MinionMelee:
                    case EEntityType.MinionRanged:
                    case EEntityType.MinionSiege:
                    case EEntityType.MinionSuper:
                    case EEntityType.Monster:
                    case EEntityType.Boss:
                        minions++;
                        break;
                    case EEntityType.Tower:
                    case EEntityType.Crystal:
                    case EEntityType.Barracks:
                        towers++;
                        break;
                    default:
                        others++;
                        break;
                }
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("F1") + "KB";
            return (bytes / (1024f * 1024f)).ToString("F2") + "MB";
        }
    }
}
