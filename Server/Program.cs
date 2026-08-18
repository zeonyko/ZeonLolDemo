using System;
using System.Diagnostics;
using System.Threading;
using Server.Battle;
using Server.Network;
using Shared;

namespace Server
{
    /// <summary>服务器入口：启动、每帧推进、关停。</summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Game Server";

            if (ArgsHas(args, "--verify-sync"))
            {
                bool ok = MovementReplayVerify.RunAll(out string report);
                Console.WriteLine(ok ? $"[SyncVerify] PASS {report}" : $"[SyncVerify] FAIL {report}");
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            var battle = BattleSystem.Create();
            battle.Boot();

            int port = ResolveListenPort(args);
            var network = NetworkServer.Create();
            network.Start(port);

            Console.WriteLine($"服务器主循环启动 Tick={GameConstants.ServerTickRate}Hz，按 Ctrl+C 退出");

            var stopwatch = Stopwatch.StartNew();
            double lastTime = 0;
            double accumulator = 0;
            const float tickDt = GameConstants.ServerTickDt;
            bool running = true;

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                running = false;
            };

            double statusTimer = 0;

            while (running)
            {
                double now = stopwatch.Elapsed.TotalSeconds;
                double frameDt = now - lastTime;
                lastTime = now;

                if (frameDt > 0.25)
                    frameDt = 0.25;

                accumulator += frameDt;
                statusTimer += frameDt;

                network.Update();

                while (accumulator >= tickDt)
                {
                    battle.Tick(tickDt);
                    network.TickBattle(tickDt);
                    SnapshotOutbox.Flush();
                    accumulator -= tickDt;
                }

                if (statusTimer >= 5.0)
                {
                    statusTimer = 0;
                    Console.WriteLine($"[Server] heartbeat onlineSessions={network.OnlineSessionCount}");
                }

                Thread.Sleep(1);
            }

            network.Stop();
            battle.Shutdown();
            Console.WriteLine("服务器已关闭");
        }

        private static int ResolveListenPort(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (!string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (int.TryParse(args[i + 1], out var fromArg) && fromArg > 0)
                        return fromArg;
                }
            }

            var env = Environment.GetEnvironmentVariable("GAME_SERVER_PORT");
            if (int.TryParse(env, out var fromEnv) && fromEnv > 0)
                return fromEnv;

            return 8888;
        }

        private static bool ArgsHas(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
