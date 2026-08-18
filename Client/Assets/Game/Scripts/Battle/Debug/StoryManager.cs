using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗剧情：假 PK / 多人切片共用一条时间轴。从 GM 面板点播放。</summary>
    public class StoryManager
    {
        public static StoryManager Instance { get; private set; }

        public const long StoryActorIdA = -9001;
        public const long StoryActorIdB = -9002;
        public const long StoryActorIdC = -9003;

        public bool IsPlaying { get; private set; }
        public string CurrentClipName { get; private set; } = "";

        #region 播放状态（当前剧本 / 播放进度）
        /// <summary>一次剧情播放的进度记账：当前剧本、下一条待执行指令索引、已播放时长。</summary>
        private sealed class PlaybackState
        {
            public StoryClip Clip;
            public int NextIndex;
            public float Time;

            /// <summary>重置播放进度；传 null 表示回到"未播放"状态。</summary>
            public void Reset(StoryClip clip = null)
            {
                Clip = clip;
                NextIndex = 0;
                Time = 0f;
            }
        }

        private readonly StoryContext _ctx = new StoryContext();
        private readonly PlaybackState _playback = new PlaybackState();
        #endregion

        #region 生命周期
        public static StoryManager Create()
        {
            Instance = new StoryManager();
            return Instance;
        }

        public void Shutdown()
        {
            Stop();
            if (Instance == this)
                Instance = null;
        }
        #endregion

        #region 每帧驱动
        public void Tick(float dt)
        {
            if (!IsPlaying || _playback.Clip == null) return;

            _playback.Time += dt;
            while (_playback.NextIndex < _playback.Clip.Commands.Count
                   && _playback.Clip.Commands[_playback.NextIndex].AtTime <= _playback.Time)
            {
                _playback.Clip.Commands[_playback.NextIndex].Execute(_ctx);
                _playback.NextIndex++;
            }

            if (_playback.NextIndex >= _playback.Clip.Commands.Count)
            {
                Debug.Log($"<color=cyan>[剧情] 结束: {_playback.Clip.Name}</color>");
                Stop();
            }
        }
        #endregion

        #region 播放控制：开始 / 停止
        public bool TryPlay(StoryClip clip)
        {
            if (clip == null || clip.Commands.Count == 0)
            {
                Debug.LogWarning("[剧情] Clip 为空");
                return false;
            }

            if (EntityManager.Instance == null)
            {
                Debug.LogWarning("[剧情] EntityManager 未就绪（请先进入 Play）");
                return false;
            }

            Stop();

            int actorCount = Mathf.Clamp(clip.ActorCount, 2, 3);
            EnsureStoryActors(actorCount, out long idA, out long idB, out long idC, out Vector3 origin);
            FrameCamera(idA, idB, idC);

            _ctx.Bind(idA, idB, idC, origin);
            _playback.Reset(clip);
            IsPlaying = true;
            CurrentClipName = clip.Name;
            Debug.Log($"<color=cyan>[剧情] 开始播放: {clip.Name}  人数={actorCount} — 本地演绎，不发包</color>");
            return true;
        }

        public void Stop()
        {
            bool wasPlaying = IsPlaying || _playback.Clip != null;
            IsPlaying = false;
            _playback.Reset();
            CurrentClipName = "";

            CleanupStoryActors();
            if (wasPlaying)
                Debug.Log("[剧情] 已清空剧情演员");
        }
        #endregion

        #region 剧情演员：生成 / 朝向 / 相机 / 清理
        /// <summary>生成本地剧情演员。2 人对站；3 人则 A 左、B 中、C 右。</summary>
        private static void EnsureStoryActors(
            int actorCount,
            out long idA,
            out long idB,
            out long idC,
            out Vector3 origin)
        {
            idA = StoryActorIdA;
            idB = StoryActorIdB;
            idC = actorCount >= 3 ? StoryActorIdC : 0;

            CleanupStoryActors();

            float y = GameConstants.GroundY;
            Vector3 spawnOrigin = Vector3.zero;
            long localId = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;
            if (localId != 0)
            {
                var localPos = EntityManager.Instance?.GetEntity(localId)
                    ?.GetComponent<TransformComponent>()?.Position;
                if (localPos.HasValue)
                    spawnOrigin = localPos.Value + new Vector3(0f, 0f, 4f);
            }

            spawnOrigin.y = y;
            origin = spawnOrigin;

            float maxHp = GameConstants.DefaultMaxHp;
            if (actorCount >= 3)
            {
                SpawnOne(idA, "玩家A", origin + new Vector3(-1.8f, 0f, 0f), maxHp, new Color(0.25f, 0.85f, 0.35f));
                SpawnOne(idB, "玩家B", origin, maxHp, new Color(0.3f, 0.75f, 0.95f));
                SpawnOne(idC, "玩家C", origin + new Vector3(1.8f, 0f, 0f), maxHp, new Color(1f, 0.55f, 0.2f));
                FaceEntities(idA, idB);
                FaceEntities(idC, idB);
                FaceEntities(idB, idA);
                return;
            }

            SpawnOne(idA, "玩家A", origin + new Vector3(-1.6f, 0f, 0f), maxHp, new Color(0.25f, 0.85f, 0.35f));
            SpawnOne(idB, "玩家B", origin + new Vector3(1.6f, 0f, 0f), maxHp, new Color(0.3f, 0.75f, 0.95f));
            FaceEntities(idA, idB);
            FaceEntities(idB, idA);
        }

        private static void SpawnOne(long id, string name, Vector3 pos, float maxHp, Color color)
        {
            pos.y = GameConstants.GroundY;
            var data = new EntityData
            {
                Id = id,
                Name = name,
                PosX = pos.x,
                PosY = pos.y,
                PosZ = pos.z,
                Hp = maxHp,
                MaxHp = maxHp,
                EntityType = EEntityType.Player
            };
            EntityViewSync.SpawnStoryActor(data, color);
        }

        private static void FaceEntities(long selfId, long otherId)
        {
            var self = EntityManager.Instance?.GetEntity(selfId);
            var other = EntityManager.Instance?.GetEntity(otherId);
            var view = self?.GetComponent<ViewComponent>();
            if (view == null || other == null) return;

            Vector3 from = self.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            Vector3 to = other.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            view.SnapFace(dir);
        }

        private static void FrameCamera(long idA, long idB, long idC)
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (BattleSystem.Instance != null && BattleSystem.Instance.LocalPlayerId != 0)
                return;

            Vector3 a = EntityManager.Instance.GetEntity(idA)?.GetComponent<TransformComponent>()?.Position
                        ?? Vector3.zero;
            Vector3 b = EntityManager.Instance.GetEntity(idB)?.GetComponent<TransformComponent>()?.Position
                        ?? Vector3.zero;
            Vector3 c = idC != 0
                ? EntityManager.Instance.GetEntity(idC)?.GetComponent<TransformComponent>()?.Position ?? b
                : b;
            Vector3 mid = (a + b + c) / (idC != 0 ? 3f : 2f) + Vector3.up * 1.2f;
            cam.transform.position = mid + new Vector3(0f, 3.5f, -7.5f);
            cam.transform.LookAt(mid);
        }

        private static void CleanupStoryActors()
        {
            AbortAndRemove(StoryActorIdA);
            AbortAndRemove(StoryActorIdB);
            AbortAndRemove(StoryActorIdC);

            var cache = BattleCache.Instance;
            if (cache != null)
            {
                cache.RemoveEntity(StoryActorIdA);
                cache.RemoveEntity(StoryActorIdB);
                cache.RemoveEntity(StoryActorIdC);
            }
            EntityViewSync.Flush();
        }

        private static void AbortAndRemove(long id)
        {
            var entity = EntityManager.Instance?.GetEntity(id);
            entity?.GetComponent<SkillCastComponent>()?.AbortCast();
        }
        #endregion
    }
}
