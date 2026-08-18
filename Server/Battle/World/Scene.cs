using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>一场对局：准备、开战、刷兵、AI。</summary>
    public class Scene
    {
        public int SceneId { get; }
        public string SceneName { get; }
        public EntityManager Entities { get; }

        public bool MatchEnded { get; private set; }
        /// <summary>是否已正式开战。false=大厅等准备。</summary>
        public bool MatchPlaying { get; private set; }
        public float MatchElapsed { get; private set; }
        public int WinnerTeamId { get; private set; }
        public bool BlueTowerAlive { get; private set; } = true;
        public bool RedTowerAlive { get; private set; } = true;
        public bool BlueBarracksAlive { get; private set; } = true;
        public bool RedBarracksAlive { get; private set; } = true;

        private readonly List<Entity> _scratch = new List<Entity>(32);
        private readonly List<PendingUnitHit> _pendingHits = new List<PendingUnitHit>(32);
        private readonly Queue<PendingMinionSpawn> _minionSpawnQueue = new Queue<PendingMinionSpawn>(32);
        private readonly HashSet<long> _rematchReady = new HashSet<long>();
        private readonly List<long> _onlinePlayerBuf = new List<long>(8);
        private float _minionWaveTimer = AramMap.MinionWaveInterval;
        /// <summary>本波已经过了多久；小于 0 表示没在出兵。</summary>
        private float _minionWaveSpawnElapsed = -1f;
        private int _minionWaveIndex = 1;

        private struct PendingUnitHit
        {
            public long AttackerId;
            public long TargetId;
            public int SkillId;
            public float ReadyTime;
        }

        public struct PendingMinionSpawn
        {
            public int TeamId;
            public int EntityType;
            public string Name;
            public float X;
            public float Z;
            public float LaneAcross;
            public bool BroadcastSpawn;
            /// <summary>相对本波开始等多久再出（秒）。</summary>
            public float Delay;
        }

        public Scene(int sceneId, string sceneName)
        {
            SceneId = sceneId;
            SceneName = sceneName;
            Entities = new EntityManager(this);
        }

        public void AddEntity(Entity entity)
        {
            Entities.Add(entity);
            Console.WriteLine($"[Scene {SceneId}:{SceneName}] Enter Id={entity.Id} Name={entity.Name}");
        }

        public void RemoveEntity(long entityId)
        {
            if (Entities.Remove(entityId))
                Console.WriteLine($"[Scene {SceneId}:{SceneName}] Leave Id={entityId}");
        }

        public Entity GetEntity(long entityId) => Entities.Get(entityId);

        public IEnumerable<Entity> GetAllEntities() => Entities.GetAll();

        public List<EntityData> GetAllEntitiesData() => Entities.GetAllData();

        public bool IsTowerAlive(int teamId)
        {
            // 外塔或内塔任一存活时，水晶保持无敌。
            if (teamId == ETeamId.Blue) return BlueTowerAlive || BlueBarracksAlive;
            if (teamId == ETeamId.Red) return RedTowerAlive || RedBarracksAlive;
            return false;
        }

        /// <summary>敌方兵营毁了 → 本队出超级兵。</summary>
        public bool ShouldSpawnSuperMinions(int teamId)
        {
            int enemy = ETeamId.EnemyOf(teamId);
            if (enemy == ETeamId.Blue) return !BlueBarracksAlive;
            if (enemy == ETeamId.Red) return !RedBarracksAlive;
            return false;
        }

        public void EnqueueMinionSpawn(PendingMinionSpawn spawn)
        {
            _minionSpawnQueue.Enqueue(spawn);
            if (_minionWaveSpawnElapsed < 0f)
                _minionWaveSpawnElapsed = 0f;
        }

        public void EnqueueMinionWave(int teamId, int waveIndex, bool broadcastSpawn)
        {
            WorldManager.Instance?.EnqueueMinionWave(this, teamId, waveIndex, broadcastSpawn);
        }

        public void OnMonsterDead(Entity dead)
        {
            if (dead == null) return;
            if (dead.IsStructure)
                OnStructureDestroyed(dead);
        }

        public void OnStructureDestroyed(Entity dead)
        {
            if (dead == null || !dead.IsStructure) return;

            Console.WriteLine($"[Scene] 建筑摧毁 {dead.Name} team={dead.TeamId}");

            if (dead.IsTower)
            {
                if (dead.TeamId == ETeamId.Blue) BlueTowerAlive = false;
                else if (dead.TeamId == ETeamId.Red) RedTowerAlive = false;
                Console.WriteLine($"[Scene] 队 {dead.TeamId} 外塔已毁" +
                    (IsTowerAlive(dead.TeamId) ? "，内塔仍保护水晶" : "，水晶可被攻击"));
                return;
            }

            if (dead.IsBarracks)
            {
                if (dead.TeamId == ETeamId.Blue) BlueBarracksAlive = false;
                else if (dead.TeamId == ETeamId.Red) RedBarracksAlive = false;
                Console.WriteLine($"[Scene] 队 {dead.TeamId} 内塔已毁，敌方将出超级兵" +
                    (IsTowerAlive(dead.TeamId) ? "" : "；水晶可被攻击"));
                return;
            }

            if (dead.IsCrystal && MatchPlaying && !MatchEnded)
                EndMatch(ETeamId.EnemyOf(dead.TeamId), dead.Id);
        }

        public void EndMatch(int winnerTeamId, long destroyedCrystalId)
        {
            if (MatchEnded) return;

            MatchEnded = true;
            MatchPlaying = false;
            WinnerTeamId = winnerTeamId;
            _minionSpawnQueue.Clear();
            _pendingHits.Clear();
            _rematchReady.Clear();

            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_MatchResultPacket, new S2C_Battle_MatchResultPacket
            {
                WinnerTeamId = winnerTeamId,
                MatchDuration = MatchElapsed,
                DestroyedCrystalId = destroyedCrystalId
            });

            BroadcastRematchReadyPacket();

            Console.WriteLine(
                $"[Scene] MatchEnd winner={winnerTeamId} duration={MatchElapsed:F1}s crystal={destroyedCrystalId}");
        }

        /// <summary>清空对局，回到大厅。</summary>
        public void PrepareNewMatch()
        {
            MatchEnded = false;
            MatchPlaying = false;
            WinnerTeamId = 0;
            MatchElapsed = 0f;
            BlueTowerAlive = true;
            RedTowerAlive = true;
            BlueBarracksAlive = true;
            RedBarracksAlive = true;
            _minionSpawnQueue.Clear();
            _pendingHits.Clear();
            _rematchReady.Clear();
            _minionWaveTimer = AramMap.MinionWaveInterval;
            _minionWaveSpawnElapsed = -1f;
            _minionWaveIndex = 1;
        }

        public void BeginPlaying()
        {
            MatchEnded = false;
            MatchPlaying = true;
            MatchElapsed = 0f;
            _rematchReady.Clear();
            _minionWaveTimer = AramMap.MinionWaveInterval;
            _minionWaveSpawnElapsed = -1f;
            _minionWaveIndex = 1;
        }

        public void CollectNonPlayerIds(List<long> buffer)
        {
            buffer.Clear();
            foreach (var e in Entities.GetAll())
            {
                if (e != null && !e.IsPlayer)
                    buffer.Add(e.Id);
            }
        }

        public void CollectTrainingDummyIds(List<long> buffer)
        {
            buffer.Clear();
            foreach (var e in Entities.GetAll())
            {
                if (e != null && e.IsTrainingDummy)
                    buffer.Add(e.Id);
            }
        }

        /// <summary>大厅或对局结束阶段都可以点准备。</summary>
        public void SetRematchReady(long entityId, bool ready)
        {
            if (MatchPlaying || entityId == 0) return;

            var player = GetEntity(entityId);
            if (player == null || !player.IsPlayer) return;

            if (ready) _rematchReady.Add(entityId);
            else _rematchReady.Remove(entityId);

            BroadcastRematchReadyPacket();
            TryAdvanceIfAllReady();
        }

        public void OnRematchPlayerLeft(long entityId)
        {
            if (entityId == 0) return;
            _rematchReady.Remove(entityId);
            if (MatchPlaying) return;

            BroadcastRematchReadyPacket();
            TryAdvanceIfAllReady();
        }

        public S2C_Battle_RematchReadyPacket BuildRematchReadyPacket()
        {
            CollectOnlinePlayerIds(_onlinePlayerBuf);
            var readyIds = new List<long>(_onlinePlayerBuf.Count);
            for (int i = 0; i < _onlinePlayerBuf.Count; i++)
            {
                long id = _onlinePlayerBuf[i];
                if (_rematchReady.Contains(id))
                    readyIds.Add(id);
            }

            return new S2C_Battle_RematchReadyPacket
            {
                ReadyCount = readyIds.Count,
                TotalCount = _onlinePlayerBuf.Count,
                ReadyEntityIds = readyIds.ToArray()
            };
        }

        public void BroadcastRematchReadyPacket()
        {
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_RematchReadyPacket, BuildRematchReadyPacket());
        }

        private void TryAdvanceIfAllReady()
        {
            if (MatchPlaying) return;

            CollectOnlinePlayerIds(_onlinePlayerBuf);
            if (_onlinePlayerBuf.Count <= 0) return;

            for (int i = 0; i < _onlinePlayerBuf.Count; i++)
            {
                if (!_rematchReady.Contains(_onlinePlayerBuf[i]))
                    return;
            }

            if (MatchEnded)
            {
                // 对局结束全员确认 → 回大厅练木桩
                Console.WriteLine($"[Scene] All {_onlinePlayerBuf.Count} players ready → RestartMatch (lobby)");
                WorldManager.Instance?.RestartMatch(this);
            }
            else
            {
                // 大厅全员准备 → 正式开战
                Console.WriteLine($"[Scene] All {_onlinePlayerBuf.Count} players ready → StartMatch");
                WorldManager.Instance?.StartMatch(this);
            }
        }

        private void CollectOnlinePlayerIds(List<long> buffer)
        {
            buffer.Clear();
            BattleNetPublisher.Instance?.ForEachSession(session =>
            {
                if (session == null || !session.IsAlive || session.PlayerEntityId == 0)
                    return;
                var e = GetEntity(session.PlayerEntityId);
                if (e != null && e.IsPlayer)
                    buffer.Add(e.Id);
            });
        }

        public void Update(float dt)
        {
            float serverTime = BattleSystem.Instance.ServerTime;

            if (MatchPlaying)
                MatchElapsed += dt;

            foreach (var entity in Entities.GetAll())
                entity.TickState(serverTime);

            if (MatchEnded)
                return;

            // 大厅也能放技能：推进出手、弹道；木桩回血
            TickPendingHits(serverTime);
            ProjectileService.Instance.Tick(dt);
            TickTrainingDummyRegen(dt);

            if (!MatchPlaying)
                return;

            TickPlayerRespawn(serverTime);

            Entities.CollectAiUnitsAlive(_scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var e = _scratch[i];
                if (e.IsMinion)
                    MinionAi.Tick(e, this, dt, serverTime);
                else if (e.IsAttackingStructure)
                    TowerAi.Tick(e, this, dt, serverTime);
                else if (e.IsMonster)
                    MonsterAi.Tick(e, this, dt, serverTime);
            }

            TickMinionWaves(dt);
            TickMinionSpawnQueue(dt);
        }

        private void TickTrainingDummyRegen(float dt)
        {
            float regen = GameConstants.TrainingDummyRegenPerSec * dt;
            if (regen <= 0f) return;

            foreach (var e in Entities.GetAll())
            {
                if (e == null || !e.IsTrainingDummy) continue;
                if (e.Hp >= e.MaxHp) continue;

                float before = e.Hp;
                e.Hp = Math.Min(e.MaxHp, e.Hp + regen);
                // 回血多一点或回满时再同步血量
                if (e.Hp >= e.MaxHp || e.Hp - before >= 40f)
                    SkillResultService.BroadcastHpSync(e);
            }
        }

        public void ScheduleUnitHit(long attackerId, long targetId, int skillId, float readyTime)
        {
            if (MatchEnded) return;
            _pendingHits.Add(new PendingUnitHit
            {
                AttackerId = attackerId,
                TargetId = targetId,
                SkillId = skillId,
                ReadyTime = readyTime
            });
        }

        private void TickPendingHits(float serverTime)
        {
            for (int i = _pendingHits.Count - 1; i >= 0; i--)
            {
                var hit = _pendingHits[i];
                if (hit.ReadyTime > serverTime) continue;
                _pendingHits.RemoveAt(i);

                if (MatchEnded) continue;

                var attacker = GetEntity(hit.AttackerId);
                var target = GetEntity(hit.TargetId);
                if (attacker == null || target == null) continue;
                if (attacker.Hp <= 0f || target.Hp <= 0f) continue;
                if (!Entity.AreEnemies(attacker, target)) continue;

                var def = SkillCatalog.Get(hit.SkillId);
                if (def != null && def.IsProjectile && def.ProjectileId != 0)
                {
                    if (!SkillClipUtil.TryGetHitByIndex(def.Clips, 0, out var hitPayload) || hitPayload == null)
                    {
                        hitPayload = new SkillHitPayload
                        {
                            AttackType = AttackStyles.Pierce,
                            CasterFeedbackType = CasterReactionIds.Pierce
                        };
                    }

                    float spawnY = attacker.PosY;
                    if (attacker.IsAttackingStructure)
                        spawnY += 2.0f;

                    long projId = ProjectileService.Instance.SpawnLockedFromHit(
                        attacker, this, def, target.Id, def.ProjectileId,
                        hitPayload, 0,
                        attacker.PosX, spawnY, attacker.PosZ);

                    // 弹道没放出来就立刻算伤害，避免远程空挥
                    if (projId == 0)
                        SkillResultService.ApplyDamageAndNotify(attacker, target, hit.SkillId);
                    continue;
                }

                SkillResultService.ApplyDamageAndNotify(attacker, target, hit.SkillId);
            }
        }

        private void TickMinionWaves(float dt)
        {
            _minionWaveTimer -= dt;
            if (_minionWaveTimer > 0f) return;

            _minionWaveTimer = AramMap.MinionWaveInterval;
            _minionWaveIndex++;

            bool siege = AramMap.WaveHasSiege(_minionWaveIndex);
            WorldManager.Instance?.EnqueueInterleavedMinionWaves(this, _minionWaveIndex, broadcastSpawn: true);
            Console.WriteLine(
                $"[Scene] 小兵波#{_minionWaveIndex} 交错入队 interval={AramMap.MinionWaveInterval:F0}s spawnGap={AramMap.MinionSpawnInterval:F1}s siege={(siege ? "Y" : "N")}");
        }

        private void TickMinionSpawnQueue(float dt)
        {
            if (_minionSpawnQueue.Count == 0)
            {
                _minionWaveSpawnElapsed = -1f;
                return;
            }

            if (_minionWaveSpawnElapsed < 0f)
                _minionWaveSpawnElapsed = 0f;
            _minionWaveSpawnElapsed += dt;

            while (_minionSpawnQueue.Count > 0
                   && _minionSpawnQueue.Peek().Delay <= _minionWaveSpawnElapsed + 1e-4f)
            {
                var pending = _minionSpawnQueue.Dequeue();
                WorldManager.Instance?.SpawnQueuedMinion(this, pending);
            }
        }

        private void TickPlayerRespawn(float serverTime)
        {
            foreach (var e in Entities.GetAll())
            {
                if (!e.IsPlayer || e.RespawnAt < 0f) continue;
                if (serverTime < e.RespawnAt) continue;
                RespawnPlayer(e);
            }
        }

        private void RespawnPlayer(Entity player)
        {
            if (player == null || !player.IsPlayer) return;

            player.Hp = player.MaxHp;
            player.RespawnAt = -1f;
            player.State.ClearAll();
            player.AggroTargetId = 0;

            AramMap.GetPlayerSpawn(player.TeamId, out float x, out float z);
            player.PosX = x;
            player.PosY = GameConstants.GroundY;
            player.PosZ = z;
            player.MoveState.IsGrounded = true;
            player.MoveState.VelY = 0f;

            SkillResultService.BroadcastHpSync(player);
            SkillResultService.BroadcastForceRelocate(player, EBattle_RelocateReason.Respawn);
            Console.WriteLine($"[Scene] 玩家复活 {player.Name} id={player.Id}");
        }

    }
}
