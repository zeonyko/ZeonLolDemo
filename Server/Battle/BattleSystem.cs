using System;
using System.IO;
using Shared;

namespace Server.Battle
{
    /// <summary>战斗总入口：启动、每帧推进、关停，并记服务器时间。</summary>
    public class BattleSystem
    {
        public static BattleSystem Instance { get; private set; }

        /// <summary>世界（含默认场景）。</summary>
        public WorldManager World { get; private set; }

        /// <summary>服务器累计时间（秒）。</summary>
        public float ServerTime { get; private set; }

        /// <summary>服务器帧号，只增不减。</summary>
        public uint ServerTick { get; private set; }

        private bool _booted;

        /// <summary>创建全局一份战斗系统。</summary>
        public static BattleSystem Create()
        {
            Instance = new BattleSystem();
            return Instance;
        }

        /// <summary>启动。配置：输出目录副本 → Server/Config → 仓库 Config/。</summary>
        public void Boot()
        {
            if (_booted) return;
            _booted = true;

            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string configRoot = FileConfigSource.ResolveExistingDirectory(
                Path.Combine(AppContext.BaseDirectory, "Config"),
                Path.Combine(repoRoot, "Server", "Config"),
                Path.Combine(repoRoot, "Config"))
                ?? Path.Combine(AppContext.BaseDirectory, "Config");

            ConfigService.Initialize(
                new FileConfigSource(configRoot),
                new global::Server.SystemTextConfigJsonCodec());
            BattleCatalogLoader.LoadCore(loadPresentation: false);

            World = WorldManager.Create();
            World.InitDefaultWorld();

            Console.WriteLine($"[BattleSystem] Config={configRoot}");
            Console.WriteLine(
                $"[BattleSystem] WorldCollision blockers={WorldCollision.All.Count} uv={WorldCollision.UseUvMapBounds}");
            Console.WriteLine("[BattleSystem] Boot OK");
        }

        /// <summary>每帧推进战斗。顺序固定：先出手结算，再地面区域，再 Buff，最后单位。别改顺序。</summary>
        public void Tick(float dt)
        {
            ServerTime += dt;
            ServerTick++;
            PendingCastService.Instance.Tick(ServerTime);
            AreaService.Instance.Tick(ServerTime);
            BuffService.Instance.Tick(ServerTime, dt);
            World?.Tick(dt);
        }

        /// <summary>默认对局场景。</summary>
        public Scene DefaultScene => World?.GetDefaultScene();

        /// <summary>关停世界与配置；可重复调用。</summary>
        public void Shutdown()
        {
            if (!_booted) return;
            _booted = false;

            World?.Shutdown();
            World = null;
            SnapshotOutbox.Clear();
            ConfigService.Shutdown();

            if (Instance == this)
                Instance = null;
        }
    }
}
