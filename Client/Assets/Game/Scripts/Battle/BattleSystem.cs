using System.Collections;
using Client.Network;
using Game.ZeonAsset;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>客户端战斗总入口：启动、每帧推进、关闭。</summary>
    public class BattleSystem
    {
        #region 全局一份与状态

        public static BattleSystem Instance { get; private set; }

        public BattleSession Session { get; private set; }
        public long LocalPlayerId => Session?.LocalPlayerId ?? 0;

        private EntityManager _entities;
        private VfxManager _vfx;
        private StoryManager _story;
        private bool _booted;

        public static BattleSystem Create()
        {
            Instance = new BattleSystem();
            return Instance;
        }

        #endregion

        #region 生命周期：Boot / Shutdown

        /// <summary>
        /// 进战场必经：配置表 → 子系统 → 必要暖场（HUD/占位等）。
        /// 由 <see cref="Client.GameEntry"/> 在加载 UI 下驱动，完成后再连服。
        /// </summary>
        public IEnumerator BootAsync()
        {
            if (_booted)
                yield break;

            yield return GameConfig.LoadAllAsync();

            if (!AuthoredMapRoot.TryFind(out _))
                Debug.LogWarning("[BattleSystem] 场景中未找到 AuthoredMapRoot");

            InitCoreSubsystems();
            NetworkManager.Instance?.AttachBattle(Session);

            yield return PrepareEssentialsAsync();

            _booted = true;
            Debug.Log("<color=cyan>[BattleSystem] Boot OK</color>");
        }

        /// <summary>开战前必须就绪的表现资源（与 <see cref="BattleBootKeys.Warmup"/> 一致）。</summary>
        static IEnumerator PrepareEssentialsAsync()
        {
            if (BattleBootKeys.Warmup != null && BattleBootKeys.Warmup.Length > 0)
                yield return BattleAssets.PreloadAsync(BattleBootKeys.Warmup);
            yield return BattlePanel.EnsureAsync();
            yield return CombatTextPool.EnsureReadyAsync();
        }

        public void Shutdown()
        {
            if (!_booted) return;
            _booted = false;

            DetachNetAndUi();
            DisposeCore();
            ClearRefs();
        }

        #endregion

        #region 开战时初始化

        private void InitCoreSubsystems()
        {
            Session = new BattleSession();
            BattleCache.Create();
            _entities = EntityManager.Create();
            _vfx = VfxManager.Create();
            CombatTextPool.Create();
            ProjectileViewService.Create();
            Playback.Create();
            _story = StoryManager.Create();

            DevicePerf.ExtraPerfLines = BattleGmSnapshot.PerfLines;
            DevicePerf.ExtraDumpLines = BattleGmSnapshot.DumpLines;
            BattleGmHost.Ensure();
            CollisionDebugView.Enabled = true;
        }

        #endregion

        #region 关闭时清理

        private void DetachNetAndUi()
        {
            CollisionDebugView.Shutdown();
            BattleGmHost.Shutdown();
            Playback.Instance?.Shutdown();
            SfxLibrary.Clear();
            NetworkManager.Instance?.DetachBattle();
            BattlePanel.Shutdown();
            MatchResultPanel.Shutdown();
            MatchEvents.Clear();
        }

        private void DisposeCore()
        {
            _story?.Shutdown();
            ProjectileViewService.Instance?.Shutdown();
            CombatTextPool.Instance?.Shutdown();
            SkillAimAreaView.Shutdown();
            _vfx?.Shutdown();
            _entities?.Shutdown();
            BattleCache.Instance?.Shutdown();
            ConfigService.Shutdown();
            BattleAssets.Shutdown();
        }

        private void ClearRefs()
        {
            _story = null;
            _vfx = null;
            _entities = null;
            Session = null;

            if (Instance == this)
                Instance = null;
        }

        #endregion

        #region 每帧推进

        public void Tick(float dt)
        {
            if (Session != null && Session.MatchPlaying && !Session.MatchEnded)
                Session.MatchElapsed += dt;

            Playback.Instance?.Tick(dt);
            ProjectileViewService.Instance?.Tick(dt);
            _entities?.Tick(dt);
            _story?.Tick(dt);
        }

        public void LateTick(float dt)
        {
            _entities?.LateTick(dt);
        }

        #endregion
    }
}
