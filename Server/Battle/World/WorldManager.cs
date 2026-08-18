using System;
using System.Collections.Generic;
using Shared;
using Server.Network;

namespace Server.Battle
{
    /// <summary>管场景和刷怪：水晶、塔、小兵波。</summary>
    public class WorldManager
    {
        public static WorldManager Instance { get; private set; }

        public const int DefaultSceneId = 1001;

        private readonly Dictionary<int, Scene> _scenes = new Dictionary<int, Scene>();
        private long _nextEntityId = 10000;

        public static WorldManager Create()
        {
            Instance = new WorldManager();
            return Instance;
        }

        public Scene GetDefaultScene() => GetScene(DefaultSceneId);

        public Scene GetScene(int sceneId)
        {
            _scenes.TryGetValue(sceneId, out var scene);
            return scene;
        }

        public long GenerateNextId() => ++_nextEntityId;

        public void InitDefaultWorld()
        {
            var scene = CreateScene(DefaultSceneId, "HowlingAbyss");
            SpawnTeamStructures(scene, ETeamId.Blue, broadcastSpawn: false);
            SpawnTeamStructures(scene, ETeamId.Red, broadcastSpawn: false);
            SpawnTrainingDummies(scene, broadcastSpawn: false);
            // 先不刷兵、不开战：等全员准备再开
            Console.WriteLine("[WorldManager] Howling Abyss lobby ready (waiting for players)");
        }

        /// <summary>对局结束后重建地图，回到大厅等再开。</summary>
        public void RestartMatch(Scene scene)
        {
            if (scene == null) return;

            PendingCastService.Instance.Clear();
            ProjectileService.Instance.Clear();
            AreaService.Instance.Clear();
            BuffService.Instance.Clear();

            scene.CollectNonPlayerIds(_restartRemoveBuf);
            for (int i = 0; i < _restartRemoveBuf.Count; i++)
            {
                long id = _restartRemoveBuf[i];
                scene.RemoveEntity(id);
                BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
                {
                    EntityId = id,
                    StateType = (int)EEntityStateType.Destroy,
                    EntityData = null
                });
            }

            scene.PrepareNewMatch();

            foreach (var entity in scene.GetAllEntities())
            {
                if (entity == null || !entity.IsPlayer) continue;
                ResetPlayerForNewMatch(entity);
            }

            SpawnTeamStructures(scene, ETeamId.Blue, broadcastSpawn: true);
            SpawnTeamStructures(scene, ETeamId.Red, broadcastSpawn: true);
            SpawnTrainingDummies(scene, broadcastSpawn: true);

            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_MatchRestartPacket, new S2C_Battle_MatchRestartPacket());

            var sync = new S2C_Battle_SyncEntitiesPacket
            {
                Entities = scene.GetAllEntitiesData().ToArray()
            };
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_SyncEntitiesPacket, sync);

            // 回大厅，不直接开战；要再点「准备开战」
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_MatchLobbyPacket, new S2C_Battle_MatchLobbyPacket());
            scene.BroadcastRematchReadyPacket();

            Console.WriteLine($"[WorldManager] Match restarted → lobby, entities={sync.Entities.Length}");
        }

        /// <summary>正式开战：清木桩、出第一波兵。</summary>
        public void StartMatch(Scene scene)
        {
            if (scene == null || scene.MatchPlaying) return;

            ClearTrainingDummies(scene, broadcastDestroy: true);
            scene.BeginPlaying();
            EnqueueInterleavedMinionWaves(scene, waveIndex: 1, broadcastSpawn: true);
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_MatchStartPacket, new S2C_Battle_MatchStartPacket());
            Console.WriteLine("[WorldManager] Match started");
        }

        public void SpawnTrainingDummies(Scene scene, bool broadcastSpawn)
        {
            if (scene == null) return;
            SpawnTrainingDummiesForTeam(scene, ETeamId.Blue, broadcastSpawn);
            SpawnTrainingDummiesForTeam(scene, ETeamId.Red, broadcastSpawn);
        }

        private void SpawnTrainingDummiesForTeam(Scene scene, int ownerTeamId, bool broadcastSpawn)
        {
            float dir = ownerTeamId == ETeamId.Blue ? 1f : -1f;
            float baseAlong = ownerTeamId == ETeamId.Blue
                ? AramMap.BlueCrystalAlong
                : AramMap.RedCrystalAlong;
            float along = baseAlong + dir * GameConstants.TrainingDummyAlongOffset;
            int count = GameConstants.TrainingDummyCountPerSide;
            float spacing = GameConstants.TrainingDummyAcrossSpacing;
            float startAcross = -spacing * (count - 1) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float across = startAcross + spacing * i;
                AramMap.LaneToWorld(along, across, out float x, out float z);
                var dummy = EntityFactory.CreateTrainingDummy(
                    GenerateNextId(),
                    ownerTeamId,
                    x,
                    z,
                    $"木头假人{i + 1}");
                scene.AddEntity(dummy);
                BroadcastSpawnIfNeeded(dummy, broadcastSpawn);
            }
        }

        public void ClearTrainingDummies(Scene scene, bool broadcastDestroy)
        {
            if (scene == null) return;
            scene.CollectTrainingDummyIds(_restartRemoveBuf);
            for (int i = 0; i < _restartRemoveBuf.Count; i++)
            {
                long id = _restartRemoveBuf[i];
                scene.RemoveEntity(id);
                if (!broadcastDestroy) continue;
                BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
                {
                    EntityId = id,
                    StateType = (int)EEntityStateType.Destroy,
                    EntityData = null
                });
            }
        }

        private static void ResetPlayerForNewMatch(Entity player)
        {
            player.MaxHp = UnitProfileHp.Resolve(player.EntityType, GameConstants.DefaultMaxHp);
            player.AttackSkillId = UnitCatalog.ResolveAttackSkillId(player.EntityType);
            player.Hp = player.MaxHp;
            player.RespawnAt = -1f;
            player.State.ClearAll();
            player.AggroTargetId = 0;
            player.ClearCastCooldowns();

            AramMap.GetPlayerSpawn(player.TeamId, out float x, out float z);
            player.PosX = x;
            player.PosY = GameConstants.GroundY;
            player.PosZ = z;
            player.MoveState.IsGrounded = true;
            player.MoveState.VelY = 0f;
            player.InputQueue.Clear();

            SkillResultService.BroadcastHpSync(player);
            SkillResultService.BroadcastForceRelocate(player, EBattle_RelocateReason.Respawn);
        }

        private readonly List<long> _restartRemoveBuf = new List<long>(64);

        public void Tick(float dt)
        {
            foreach (var scene in _scenes.Values)
                scene.Update(dt);
        }

        public Scene CreateScene(int sceneId, string sceneName)
        {
            if (_scenes.TryGetValue(sceneId, out var existing))
                return existing;

            var scene = new Scene(sceneId, sceneName);
            _scenes[sceneId] = scene;
            return scene;
        }

        public Entity CreatePlayer(string name, Scene targetScene = null)
        {
            long id = GenerateNextId();
            int team = EntityFactory.PickBalancedTeam(targetScene ?? GetDefaultScene());
            var player = EntityFactory.CreatePlayer(id, name, team);
            var scene = targetScene ?? GetDefaultScene();
            scene?.AddEntity(player);
            Console.WriteLine($"[WorldManager] Player {name} team={(team == ETeamId.Blue ? "Blue" : "Red")} spawn=({player.PosX:F1},{player.PosZ:F1})");
            return player;
        }

        public void SpawnTeamStructures(Scene scene, int teamId, bool broadcastSpawn)
        {
            if (scene == null) return;
            string side = teamId == ETeamId.Blue ? "Blue" : "Red";

            var crystal = EntityFactory.CreateStructure(
                GenerateNextId(), teamId, EEntityType.Crystal, $"{side}_Crystal");
            scene.AddEntity(crystal);
            BroadcastSpawnIfNeeded(crystal, broadcastSpawn);

            var barracks = EntityFactory.CreateStructure(
                GenerateNextId(), teamId, EEntityType.Barracks, $"{side}_InnerTower");
            scene.AddEntity(barracks);
            BroadcastSpawnIfNeeded(barracks, broadcastSpawn);

            var tower = EntityFactory.CreateStructure(
                GenerateNextId(), teamId, EEntityType.Tower, $"{side}_OuterTower");
            scene.AddEntity(tower);
            BroadcastSpawnIfNeeded(tower, broadcastSpawn);
        }

        /// <summary>蓝红各自排队出门，两边可以同时出。</summary>
        public void EnqueueInterleavedMinionWaves(Scene scene, int waveIndex, bool broadcastSpawn)
        {
            if (scene == null) return;
            var ordered = new List<Scene.PendingMinionSpawn>(16);
            AppendWaveWithDelays(ordered, scene, ETeamId.Blue, waveIndex, broadcastSpawn);
            AppendWaveWithDelays(ordered, scene, ETeamId.Red, waveIndex, broadcastSpawn);
            ordered.Sort((a, b) =>
            {
                int c = a.Delay.CompareTo(b.Delay);
                return c != 0 ? c : a.TeamId.CompareTo(b.TeamId);
            });
            for (int i = 0; i < ordered.Count; i++)
                scene.EnqueueMinionSpawn(ordered[i]);
        }

        /// <summary>一波兵入队。敌方兵营毁了出超级兵，否则 3 近战 + 3 远程（可加攻城）。</summary>
        public void EnqueueMinionWave(Scene scene, int teamId, int waveIndex, bool broadcastSpawn)
        {
            if (scene == null) return;
            var ordered = new List<Scene.PendingMinionSpawn>(8);
            AppendWaveWithDelays(ordered, scene, teamId, waveIndex, broadcastSpawn);
            for (int i = 0; i < ordered.Count; i++)
                scene.EnqueueMinionSpawn(ordered[i]);
        }

        private static void AppendWaveWithDelays(
            List<Scene.PendingMinionSpawn> into, Scene scene, int teamId, int waveIndex, bool broadcastSpawn)
        {
            var list = WaveSpawner.BuildWaveSpawns(scene, teamId, waveIndex, broadcastSpawn);
            float t = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                s.Delay = t;
                into.Add(s);
                t += AramMap.MinionSpawnInterval;
            }
        }

        public void SpawnQueuedMinion(Scene scene, Scene.PendingMinionSpawn pending)
        {
            if (scene == null || scene.MatchEnded || !scene.MatchPlaying) return;

            var m = EntityFactory.CreateMinion(
                GenerateNextId(), pending.TeamId, pending.EntityType,
                pending.Name, pending.X, pending.Z);
            m.LaneAcross = pending.LaneAcross;
            scene.AddEntity(m);
            BroadcastSpawnIfNeeded(m, pending.BroadcastSpawn);
        }

        private static void BroadcastSpawnIfNeeded(Entity entity, bool broadcastSpawn)
        {
            if (!broadcastSpawn || entity == null) return;
            BattleNetPublisher.Instance.BroadcastAll(EOpCode.S2C_Battle_EntityStatePacket, new S2C_Battle_EntityStatePacket
            {
                EntityId = entity.Id,
                StateType = (int)EEntityStateType.Spawn,
                EntityData = entity.ToEntityData()
            });
        }

        public void Shutdown()
        {
            _scenes.Clear();
            if (Instance == this)
                Instance = null;
        }
    }
}
