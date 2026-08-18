using System.Text;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 一次启动/初始化的案件报告。失败时把配置和清单状态再打一遍，避免翻上面的 Info。
    /// </summary>
    public static class ZeonAssetBootReport
    {
        public static string Last { get; private set; } = string.Empty;

        public static string CaptureStart(ZeonAssetConfig config)
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("BOOT");
            AppendConfig(sb, config);
            Last = sb.ToString().TrimEnd();
            return Last;
        }

        public static string CaptureEnd(AsyncOperationBase op)
        {
            var sb = new StringBuilder(768);
            if (op != null && op.IsSucceed)
            {
                sb.AppendLine("BOOT OK");
                AppendRuntimeState(sb);
            }
            else
            {
                sb.AppendLine("BOOT FAIL");
                if (op != null)
                {
                    ZeonAssetLog.AppendKv(sb, "op", op.GetDiagnosticLabel());
                    ZeonAssetLog.AppendKv(sb, "err", op.Error);
                }

                AppendRuntimeState(sb);
                sb.AppendLine("--- context ---");
                AppendConfig(sb, AssetManager.Config);
            }

            Last = sb.ToString().TrimEnd();
            return Last;
        }

        private static void AppendConfig(StringBuilder sb, ZeonAssetConfig config)
        {
            if (config == null)
            {
                sb.AppendLine("  config=(null)");
                return;
            }

            ZeonAssetLog.AppendKv(sb, "mode", config.PlayMode.ToString());
            ZeonAssetLog.AppendKv(sb, "policy", config.ResolveDownloadPolicy().ToString());
            ZeonAssetLog.AppendKv(sb, "playWhileDownload", config.ShouldPlayWhileDownload() ? "true" : "false");
            ZeonAssetLog.AppendKv(
                sb, "hook",
                config.CodeHotUpdateHook != null ? config.CodeHotUpdateHook.GetType().Name : "none");

            var urls = config.CollectVersionCheckUrls();
            ZeonAssetLog.AppendKv(sb, "vc", urls.Length > 0 ? string.Join(" | ", urls) : "(empty)");
            ZeonAssetLog.AppendKv(sb, "cdn", config.RemoteUrl);

            ZeonAssetLog.AppendKv(sb, "app", config.ResolveAppVersion());
            ZeonAssetLog.AppendKv(sb, "channel", config.Channel);
            ZeonAssetLog.AppendKv(sb, "sandbox", string.IsNullOrEmpty(config.BundleRoot)
                ? DiskCacheManager.GetSandboxRoot()
                : config.BundleRoot);
            ZeonAssetLog.AppendKv(sb, "streaming", DiskCacheManager.GetStreamingRoot());
        }

        private static void AppendRuntimeState(StringBuilder sb)
        {
            var active = AssetManager.ActiveManifest;
            var builtin = AssetManager.BuiltinManifest;
            var vc = AssetManager.ActiveVersionCheck;

            ZeonAssetLog.AppendKv(sb, "active", FormatManifest(active));
            ZeonAssetLog.AppendKv(sb, "builtin", FormatManifest(builtin));
            if (vc != null)
            {
                ZeonAssetLog.AppendKv(sb, "vc.has_update", vc.has_update ? "true" : "false");
                ZeonAssetLog.AppendKv(sb, "vc.manifest", vc.manifest_name);
                ZeonAssetLog.AppendKv(sb, "vc.hash", vc.manifest_hash);
                ZeonAssetLog.AppendKv(sb, "vc.cdn", vc.cdn_host);
            }
        }

        private static string FormatManifest(PackageManifest manifest)
        {
            if (manifest == null)
                return "(none)";
            var name = string.IsNullOrEmpty(manifest.ManifestFileName) ? "?" : manifest.ManifestFileName;
            var hash = string.IsNullOrEmpty(manifest.ManifestHash) ? "?" : manifest.ManifestHash;
            return name + " hash=" + hash;
        }
    }
}
