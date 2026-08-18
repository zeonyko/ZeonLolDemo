using System;
using System.Text;
using Client.Battle;
using Shared;
using UnityEngine;

namespace Client.Network
{
    /// <summary>最近 50 条收发包，给战斗 GM / 一键快照用。收包线程也会写，所以加锁。</summary>
    public static class NetTrace
    {
        public const int Capacity = 50;

        public struct Entry
        {
            public bool Outbound;
            public EOpCode OpCode;
            public int Bytes;
            public bool Delayed;
            public int TickMs;
            public string Json;
        }

        static readonly Entry[] Buf = new Entry[Capacity];
        static readonly object Gate = new object();
        static int _next;
        static int _count;
        static volatile bool _enabled;

        /// <summary>战斗 GM 打开才记。关掉立刻清空，平时收发包不进这里。</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                if (!value)
                    Clear();
            }
        }

        public static void Record(bool outbound, EOpCode opCode, int bytes, bool delayed, string json = null)
        {
            if (!_enabled)
                return;

            lock (Gate)
            {
                if (!_enabled)
                    return;
                Buf[_next] = new Entry
                {
                    Outbound = outbound,
                    OpCode = opCode,
                    Bytes = bytes,
                    Delayed = delayed,
                    TickMs = Environment.TickCount,
                    Json = json ?? string.Empty
                };
                _next = (_next + 1) % Capacity;
                if (_count < Capacity)
                    _count++;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                _next = 0;
                _count = 0;
            }
        }

        public static string Format()
        {
            Entry[] copy;
            int count;
            int next;
            lock (Gate)
            {
                count = _count;
                next = _next;
                copy = new Entry[count];
                for (int i = 0; i < count; i++)
                {
                    int idx = next - 1 - i;
                    if (idx < 0)
                        idx += Capacity;
                    copy[i] = Buf[idx];
                }
            }

            if (count == 0)
                return "最近包  (空)";

            int now = Environment.TickCount;
            var sb = new StringBuilder(count * 96);
            sb.Append("最近包  新→旧");
            for (int i = 0; i < count; i++)
            {
                var e = copy[i];
                sb.Append('\n')
                    .Append(e.Outbound ? "↑发 " : "↓收 ")
                    .Append(e.OpCode)
                    .Append("  ")
                    .Append(e.Bytes)
                    .Append('B')
                    .Append("  ")
                    .Append(Age(now, e.TickMs));
                if (e.Delayed)
                    sb.Append("  delay");

                string detail = Detail(e.OpCode, e.Json);
                if (!string.IsNullOrEmpty(detail))
                    sb.Append("\n  ").Append(detail);
            }

            return sb.ToString();
        }

        static string Detail(EOpCode op, string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            try
            {
                switch (op)
                {
                    case EOpCode.S2C_Auth_LoginRsp:
                    {
                        var m = JsonUtility.FromJson<S2C_Auth_LoginRsp>(json);
                        return m == null ? "" : $"id={m.EntityId} {m.PlayerName}";
                    }
                    case EOpCode.S2C_Battle_SyncTimePacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_SyncTimePacket>(json);
                        return m == null ? "" : $"服务器帧{m.ServerTick}";
                    }
                    case EOpCode.S2C_Battle_SyncEntitiesPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_SyncEntitiesPacket>(json);
                        int n = m?.Entities != null ? m.Entities.Length : 0;
                        return "×" + n;
                    }
                    case EOpCode.S2C_Battle_EntityStatePacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_EntityStatePacket>(json);
                        if (m == null) return "";
                        string name = m.EntityData != null && !string.IsNullOrEmpty(m.EntityData.Name)
                            ? m.EntityData.Name
                            : EntityName(m.EntityId);
                        return $"{EntityStateName(m.StateType)} {name}";
                    }
                    case EOpCode.C2S_Battle_InputCmd:
                    {
                        var m = JsonUtility.FromJson<C2S_Battle_InputCmd>(json);
                        if (m == null) return "";
                        int n = m.HistoryInputs != null ? m.HistoryInputs.Length : 0;
                        string act = n > 0 ? InputAct(m.HistoryInputs[n - 1]) : "";
                        return $"本地帧{m.LatestClientTick} ×{n} {act}".Trim();
                    }
                    case EOpCode.S2C_Battle_ReconcilePacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_ReconcilePacket>(json);
                        if (m == null) return "";
                        return $"已确认本地帧{m.AckClientTick} {ReconcileReason(m.ReconcileReason)} 权威位{Pos(m.CorrectTf)}".Trim();
                    }
                    case EOpCode.S2C_Battle_RemoteSnapshotPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_RemoteSnapshotPacket>(json);
                        if (m == null) return "";
                        int n = m.EntitySnapshots != null ? m.EntitySnapshots.Length : 0;
                        return $"服务器帧{m.ServerTick} ×{n}";
                    }
                    case EOpCode.S2C_Battle_ForceRelocatePacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_ForceRelocatePacket>(json);
                        if (m == null) return "";
                        return $"{RelocateReason(m.RelocateReason)} {EntityName(m.EntityId)} {Pos(m.TargetTf)}".Trim();
                    }
                    case EOpCode.C2S_Battle_SkillCastCmd:
                    {
                        var m = JsonUtility.FromJson<C2S_Battle_SkillCastCmd>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} t{m.ClientTick} →{Target(m.TargetId)}";
                    }
                    case EOpCode.S2C_Battle_SkillCastPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_SkillCastPacket>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} {EntityName(m.CasterId)} →{Target(m.TargetId)}";
                    }
                    case EOpCode.C2S_Battle_SkillCancelCmd:
                    {
                        var m = JsonUtility.FromJson<C2S_Battle_SkillCancelCmd>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} {CancelCastReason(m.CancelReason)}";
                    }
                    case EOpCode.S2C_Battle_SkillCancelPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_SkillCancelPacket>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} {EntityName(m.CasterId)} {SkillCancelReason(m.CancelReason)}";
                    }
                    case EOpCode.S2C_Battle_SkillHitPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_SkillHitPacket>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} {HitSummary(m.Hits)}";
                    }
                    case EOpCode.S2C_Battle_ProjectileSpawnPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_ProjectileSpawnPacket>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} →{Target(m.TargetId)}";
                    }
                    case EOpCode.S2C_Battle_ProjectileDespawnPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_ProjectileDespawnPacket>(json);
                        if (m == null) return "";
                        return $"{SkillName(m.SkillId)} {DespawnReason(m.Reason)}";
                    }
                    case EOpCode.S2C_Battle_MatchResultPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_MatchResultPacket>(json);
                        if (m == null) return "";
                        return $"胜方={m.WinnerTeamId} {m.MatchDuration:F0}s";
                    }
                    case EOpCode.S2C_Battle_RematchReadyPacket:
                    {
                        var m = JsonUtility.FromJson<S2C_Battle_RematchReadyPacket>(json);
                        return m == null ? "" : $"{m.ReadyCount}/{m.TotalCount}";
                    }
                    case EOpCode.C2S_Battle_RematchReadyCmd:
                    {
                        var m = JsonUtility.FromJson<C2S_Battle_RematchReadyCmd>(json);
                        return m == null ? "" : (m.Ready != 0 ? "准备" : "取消");
                    }
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        static string Age(int nowMs, int thenMs)
        {
            int ms = unchecked(nowMs - thenMs);
            if (ms < 0)
                ms = 0;
            if (ms < 1000)
                return ms + "ms";
            return (ms / 1000f).ToString("F1") + "s";
        }

        static string Pos(TransformData tf)
        {
            if (tf?.Position == null)
                return "";
            var p = tf.Position;
            return $"({p.X:F1},{p.Y:F1},{p.Z:F1})";
        }

        static string SkillName(int skillId)
        {
            if (skillId == 0)
                return "-";
            var cfg = SkillCatalog.Get(skillId);
            return cfg != null && !string.IsNullOrEmpty(cfg.Name) ? cfg.Name : skillId.ToString();
        }

        static string EntityName(long id)
        {
            if (id == 0)
                return "-";
            var cache = BattleCache.Instance;
            if (cache != null && cache.TryGet(id, out var data) && data != null && !string.IsNullOrEmpty(data.Name))
                return data.Name;
            return id.ToString();
        }

        static string Target(long id)
        {
            return id == 0 ? "方向" : EntityName(id);
        }

        static string InputAct(SingleFrameInput frame)
        {
            if (frame == null)
                return "";
            if ((frame.InputFlags & SyncContract.InputFlags.MotionCommit) != 0)
                return "闪";
            if ((frame.InputFlags & SyncContract.InputFlags.Jump) != 0)
                return "跳";
            var move = frame.MoveIntent;
            if (move != null && (move.X * move.X + move.Z * move.Z) > 0.0001f)
                return "走";
            return "停";
        }

        static string HitSummary(SkillHitEntry[] hits)
        {
            if (hits == null || hits.Length == 0)
                return "空挥";
            int dmg = 0;
            bool crit = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h == null)
                    continue;
                dmg += h.Damage;
                if ((h.HitFlags & (uint)EBattle_HitFlags.Crit) != 0)
                    crit = true;
            }

            string who = hits.Length == 1 && hits[0] != null ? EntityName(hits[0].TargetId) : "×" + hits.Length;
            return who + " 伤" + dmg + (crit ? " 暴击" : "");
        }

        static string EntityStateName(int stateType)
        {
            switch ((EEntityStateType)stateType)
            {
                case EEntityStateType.Spawn: return "出生";
                case EEntityStateType.Destroy: return "销毁";
                case EEntityStateType.Dead: return "死亡";
                case EEntityStateType.AttrSync: return "属性";
                default: return stateType.ToString();
            }
        }

        static string ReconcileReason(uint reason)
        {
            switch ((EBattle_ReconcileReason)reason)
            {
                case EBattle_ReconcileReason.PredictionError: return "预测误差";
                case EBattle_ReconcileReason.Collision: return "撞墙";
                case EBattle_ReconcileReason.IllegalSkillMotion: return "技能位移非法";
                case EBattle_ReconcileReason.HardControl: return "硬控";
                default: return reason == 0 ? "" : reason.ToString();
            }
        }

        static string RelocateReason(uint reason)
        {
            switch ((EBattle_RelocateReason)reason)
            {
                case EBattle_RelocateReason.Respawn: return "复活";
                case EBattle_RelocateReason.SceneChange: return "切场景";
                case EBattle_RelocateReason.ForcePull: return "强拉";
                case EBattle_RelocateReason.Knockback: return "击退";
                default: return reason == 0 ? "" : reason.ToString();
            }
        }

        static string CancelCastReason(uint reason)
        {
            switch ((EBattle_CancelCastReason)reason)
            {
                case EBattle_CancelCastReason.Move: return "移动打断";
                case EBattle_CancelCastReason.Dodge: return "闪避打断";
                case EBattle_CancelCastReason.ChargeRelease: return "松键";
                default: return reason.ToString();
            }
        }

        static string SkillCancelReason(uint reason)
        {
            switch ((EBattle_SkillCancelNotifyReason)reason)
            {
                case EBattle_SkillCancelNotifyReason.ActiveCancel: return "主动取消";
                case EBattle_SkillCancelNotifyReason.ControlInterrupt: return "受控打断";
                case EBattle_SkillCancelNotifyReason.Illegal: return "非法作废";
                case EBattle_SkillCancelNotifyReason.Rejected: return "服务器拒绝";
                default: return reason.ToString();
            }
        }

        static string DespawnReason(uint reason)
        {
            switch ((EBattle_ProjectileDespawnReason)reason)
            {
                case EBattle_ProjectileDespawnReason.Expired: return "超距";
                case EBattle_ProjectileDespawnReason.HitCap: return "命中上限";
                case EBattle_ProjectileDespawnReason.Cancelled: return "取消";
                default: return reason.ToString();
            }
        }
    }
}
