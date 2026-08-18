using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 真机功耗档：限帧、关 MSAA/阴影/HDR。资源 GM「性能」页可改档、看 FPS。
    /// Game 可通过 Extra* 钩子追加战斗数据，壳不引用战斗类型。
    /// </summary>
    public static class DevicePerf
    {
        public const int MobileTargetFps = 30;

        public static bool HudEnabled;

        /// <summary>性能页追加几行（配置条数、特效存活）。</summary>
        public static Func<string> ExtraPerfLines;

        /// <summary>一键快照追加战斗态 / 末包等。</summary>
        public static Func<string> ExtraDumpLines;

        public static float Fps { get; private set; }
        public static float FpsAvg { get; private set; }
        public static float FrameMs { get; private set; }

        static bool _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoApply()
        {
            ApplyAtBoot();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToAllCameras();

        /// <summary>启动尽早调用。真机 30fps + 关抗锯齿/阴影；编辑器只锁 VSync。</summary>
        public static void ApplyAtBoot()
        {
            if (Application.isMobilePlatform)
                ApplyMobileDefaults();
            else if (!_applied)
            {
                QualitySettings.vSyncCount = 1;
                if (Application.targetFrameRate < 0)
                    Application.targetFrameRate = 60;
            }

            _applied = true;
            ApplyToAllCameras();
        }

        public static void ApplyMobileDefaults()
        {
            Application.targetFrameRate = MobileTargetFps;
            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            ApplyToAllCameras();
        }

        public static void SetTargetFps(int fps)
        {
            fps = Mathf.Clamp(fps, 15, 120);
            Application.targetFrameRate = fps;
            QualitySettings.vSyncCount = 1;
        }

        public static void ApplyToCamera(Camera cam)
        {
            if (cam == null || !Application.isMobilePlatform)
                return;
            cam.allowHDR = false;
            cam.allowMSAA = false;
        }

        public static void ApplyToAllCameras()
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
                ApplyToCamera(cams[i]);
        }

        public static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt < 0.00005f)
                return;
            FrameMs = dt * 1000f;
            Fps = 1f / dt;
            FpsAvg = FpsAvg < 0.5f ? Fps : Mathf.Lerp(FpsAvg, Fps, 0.08f);
        }

        public static long MonoBytes => GC.GetTotalMemory(false);

        public static string SnapshotLine()
        {
            return
                $"[Perf] fps={Fps:F0} avg={FpsAvg:F0} dt={FrameMs:F1}ms " +
                $"target={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} " +
                $"aa={QualitySettings.antiAliasing} shadows={QualitySettings.shadows} " +
                $"hdr={(Camera.main != null && Camera.main.allowHDR)} " +
                $"mono={MonoBytes / (1024f * 1024f):F0}MB " +
                $"res={Screen.width}x{Screen.height}";
        }

        /// <summary>一键快照：性能 + Game 追加 + 最近 Error。写进日志后可走资源 GM「上报」。</summary>
        public static string BuildDump()
        {
            ZeonAssetScreenLog.Ensure();
            var sb = new StringBuilder(1024);
            sb.Append("[Dump] ").AppendLine(DateTime.Now.ToString("HH:mm:ss"));
            sb.AppendLine(SnapshotLine());
            var extra = ExtraDumpLines?.Invoke();
            if (!string.IsNullOrEmpty(extra))
                sb.AppendLine(extra.TrimEnd());
            AppendLastErrors(sb);
            return sb.ToString().TrimEnd();
        }

        static void AppendLastErrors(StringBuilder sb)
        {
            var lines = ZeonAssetScreenLog.GetLines();
            if (lines == null || lines.Count == 0)
                return;

            int n = 0;
            for (int i = lines.Count - 1; i >= 0 && n < 8; i--)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line) || !LooksLikeError(line))
                    continue;
                if (n == 0)
                    sb.AppendLine("[Error]");
                sb.AppendLine(line);
                n++;
            }
        }

        static bool LooksLikeError(string line)
        {
            return line.IndexOf("[E] ", StringComparison.Ordinal) >= 0 ||
                   line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf(" FAIL ", StringComparison.Ordinal) >= 0;
        }
    }
}
