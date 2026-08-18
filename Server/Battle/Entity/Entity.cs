using System;
using Shared;

namespace Server.Battle
{
    public enum EMonsterAiState
    {
        Patrol = 0,
        Chase = 1,
        Return = 2,
    }

    /// <summary>服务器上的单位（不算表现）。</summary>
    public class Entity
    {
        private const int PosHistoryCapacity = 40; // 大约 2 秒，位移技用来往回找位置

        private struct PosSample
        {
            public float Time;
            public uint ClientTick;
            public float X;
            public float Y;
            public float Z;
        }

        public long Id;
        public string Name;
        /// <summary>单位类型。</summary>
        public int EntityType;
        /// <summary>阵营。</summary>
        public int TeamId;
        /// <summary>默认普攻技能。</summary>
        public int AttackSkillId;

        public MovementState MoveState;
        public float Hp = GameConstants.DefaultMaxHp;
        public float MaxHp = GameConstants.DefaultMaxHp;
        /// <summary>已经模拟完的最后一条客户端帧号。</summary>
        public uint LastProcessedInputSeq;
        /// <summary>还能模拟多少秒的输入。不够就留着下次。</summary>
        public float InputSimBudget;
        private readonly System.Collections.Generic.Dictionary<int, float> _lastCastTimes =
            new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>还没模拟的移动输入，按帧号从小到大。</summary>
        public readonly System.Collections.Generic.List<PendingSimInput> InputQueue =
            new System.Collections.Generic.List<PendingSimInput>(SyncContract.MaxPendingInputs);

        public struct PendingSimInput
        {
            public uint Tick;
            public float MoveX;
            public float MoveZ;
            public bool Jump;
            public float DeltaTime;
            public bool IsMotionCommit;
            public float MotionDirX;
            public float MotionDirZ;
            public float MotionDistance;
        }

        public readonly EntityStateMachine State = new EntityStateMachine();

        private readonly PosSample[] _posHistory = new PosSample[PosHistoryCapacity];
        private int _posHistoryCount;
        private int _posHistoryNext;

        public Scene Scene;

        // --- 怪物 AI ---
        public float SpawnX;
        public float SpawnZ;
        public EMonsterAiState AiState = EMonsterAiState.Patrol;
        public float PatrolTargetX;
        public float PatrolTargetZ;
        public float PatrolRetargetTimer;
        public float SnapshotTimer;
        /// <summary>站着不动时，上次把位置发给别人的时间。</summary>
        public float LastIdleSnapshotTime = -999f;
        /// <summary>上次广播/对账时的位置；用来判断这帧有没有真的挪。</summary>
        public float LastBroadcastPosX;
        public float LastBroadcastPosY;
        public float LastBroadcastPosZ;
        public bool HasBroadcastPos;
        public long AggroTargetId;
        /// <summary>小兵沿路推进到第几个点。</summary>
        public int LanePathIndex;
        /// <summary>兵线横向位置；推线为 0，交战时散开。</summary>
        public float LaneAcross;

        /// <summary>最近打过谁（小兵选人用）。</summary>
        public long LastDealtDamageTargetId;
        public float LastDealtDamageAt = -999f;
        /// <summary>最近被谁打过。</summary>
        public long LastDamagedById;
        public float LastDamagedByAt = -999f;
        /// <summary>强制仇恨截止时间（服务器时间）。</summary>
        public float ForcedAggroUntil = -999f;
        /// <summary>交战时站位槽；int.MinValue=还没占。</summary>
        public int EngageSlotIndex = int.MinValue;
        /// <summary>这个槽绑的是哪个目标。</summary>
        public long EngageSlotTargetId;
        /// <summary>已经进射程站住了，避免大家跟着晃。</summary>
        public bool EngagePlanted;

        /// <summary>玩家复活时刻（服务器时间）；小于 0 表示活着。</summary>
        public float RespawnAt = -1f;
        /// <summary>最近打过敌方英雄的时间（塔来帮忙用）。</summary>
        public float LastDamagedEnemyPlayerAt = -999f;
        /// <summary>大厅木桩：能挨打、死不了、会回血、没有 AI。</summary>
        public bool IsTrainingDummy;

        public bool IsDead => Hp <= 0f;
        public bool IsWaitingRespawn => IsPlayer && RespawnAt >= 0f && Hp <= 0f;

        public bool IsMonster => EntityType == EEntityType.Monster || EntityType == EEntityType.Boss;
        public bool IsBoss => EntityType == EEntityType.Boss;
        public bool IsPlayer => EntityType == EEntityType.Player;
        public bool IsMinion =>
            EntityType == EEntityType.MinionMelee
            || EntityType == EEntityType.MinionRanged
            || EntityType == EEntityType.MinionSiege
            || EntityType == EEntityType.MinionSuper;
        public bool IsStructure =>
            EntityType == EEntityType.Tower
            || EntityType == EEntityType.Crystal
            || EntityType == EEntityType.Barracks;
        public bool IsTower => EntityType == EEntityType.Tower;
        public bool IsCrystal => EntityType == EEntityType.Crystal;
        public bool IsBarracks => EntityType == EEntityType.Barracks;
        /// <summary>外塔 / 内塔（兵营位）都会普攻。</summary>
        public bool IsAttackingStructure => IsTower || IsBarracks;
        public bool IsSuperMinion => EntityType == EEntityType.MinionSuper;
        public bool IsSiegeMinion => EntityType == EEntityType.MinionSiege;
        public bool NeedsAiTick => (IsMinion || IsAttackingStructure || IsMonster) && !IsTrainingDummy;

        public static bool AreEnemies(Entity a, Entity b)
        {
            if (a == null || b == null) return false;
            if (a.Id == b.Id) return false;
            if (a.TeamId == ETeamId.None || b.TeamId == ETeamId.None) return false;
            return a.TeamId != b.TeamId;
        }

        public void TickState(float serverTime) => State.Tick(serverTime);

        /// <summary>记下当前位置，位移技用来往回找。</summary>
        public void RecordPosHistory(float serverTime)
        {
            _posHistory[_posHistoryNext] = new PosSample
            {
                Time = serverTime,
                ClientTick = LastProcessedInputSeq,
                X = PosX,
                Y = PosY,
                Z = PosZ
            };
            _posHistoryNext = (_posHistoryNext + 1) % PosHistoryCapacity;
            if (_posHistoryCount < PosHistoryCapacity)
                _posHistoryCount++;
        }

        /// <summary>按客户端帧号找回当时的位置。</summary>
        public bool TrySamplePosAtClientTick(uint clientTick, out float x, out float y, out float z)
        {
            x = PosX;
            y = PosY;
            z = PosZ;
            if (_posHistoryCount <= 0 || clientTick == 0)
                return false;

            // 找不超过目标帧号的最新记录
            bool found = false;
            PosSample best = default;
            for (int i = 0; i < _posHistoryCount; i++)
            {
                PosSample s = GetHistory(i);
                if (s.ClientTick == 0) continue;
                if (s.ClientTick > clientTick) continue;
                if (!found || s.ClientTick >= best.ClientTick)
                {
                    best = s;
                    found = true;
                }
            }

            if (!found)
                return false;

            x = best.X;
            y = best.Y;
            z = best.Z;
            return true;
        }

        /// <summary>取某一时刻的位置；没有记录就用现在。</summary>
        public bool TrySamplePosAt(float atTime, out float x, out float y, out float z)
        {
            x = PosX;
            y = PosY;
            z = PosZ;
            if (_posHistoryCount <= 0)
                return false;

            PosSample oldest = GetHistory(0);
            PosSample newest = GetHistory(_posHistoryCount - 1);

            if (atTime <= oldest.Time)
            {
                x = oldest.X;
                y = oldest.Y;
                z = oldest.Z;
                return true;
            }

            if (atTime >= newest.Time)
            {
                x = newest.X;
                y = newest.Y;
                z = newest.Z;
                return true;
            }

            for (int i = 0; i < _posHistoryCount - 1; i++)
            {
                PosSample a = GetHistory(i);
                PosSample b = GetHistory(i + 1);
                if (atTime < a.Time || atTime > b.Time)
                    continue;

                float span = b.Time - a.Time;
                float t = span > 0.0001f ? (atTime - a.Time) / span : 1f;
                x = a.X + (b.X - a.X) * t;
                y = a.Y + (b.Y - a.Y) * t;
                z = a.Z + (b.Z - a.Z) * t;
                return true;
            }

            return true;
        }

        private PosSample GetHistory(int logicalIndex)
        {
            int start = (_posHistoryNext - _posHistoryCount + PosHistoryCapacity) % PosHistoryCapacity;
            int idx = (start + logicalIndex) % PosHistoryCapacity;
            return _posHistory[idx];
        }

        public float GetLastCastTime(int skillId)
        {
            return _lastCastTimes.TryGetValue(skillId, out float t) ? t : -999f;
        }

        public void SetLastCastTime(int skillId, float time)
        {
            _lastCastTimes[skillId] = time;
        }

        public void ClearCastCooldowns()
        {
            _lastCastTimes.Clear();
        }

        public float PosX
        {
            get => MoveState.PosX;
            set
            {
                var ms = MoveState;
                ms.PosX = value;
                MoveState = ms;
            }
        }

        public float PosY
        {
            get => MoveState.PosY;
            set
            {
                var ms = MoveState;
                ms.PosY = value;
                MoveState = ms;
            }
        }

        public float PosZ
        {
            get => MoveState.PosZ;
            set
            {
                var ms = MoveState;
                ms.PosZ = value;
                MoveState = ms;
            }
        }

        public float DistToSpawnSq()
        {
            float dx = PosX - SpawnX;
            float dz = PosZ - SpawnZ;
            return dx * dx + dz * dz;
        }

        public EntityData ToEntityData()
        {
            float now = BattleSystem.Instance?.ServerTime ?? 0f;
            return new EntityData
            {
                Id = Id,
                Name = Name,
                PosX = PosX,
                PosY = PosY,
                PosZ = PosZ,
                Hp = Hp,
                MaxHp = MaxHp,
                EntityType = EntityType,
                TeamId = TeamId,
                Buffs = BuffService.Instance.Snapshot(Id, now)
            };
        }
    }
}
