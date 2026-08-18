using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>调试报告：路径 / 依赖 / Tag 命中。</summary>
    public class ZeonAssetReportWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private Vector2 _scroll;
        private BuildTarget _buildTarget;
        private string _report = "点击下方按钮生成报告。";

        [MenuItem("ZeonAsset/报告", priority = 130)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetReportWindow>("报告");
            ZeonAssetEditorGuiUtility.ApplyWindowSize(w);
            w.Show();
        }

        private void OnEnable()
        {
            ZeonAssetEditorGuiUtility.ApplyWindowSize(this);
            _settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            _buildTarget = ZeonAssetEditorGuiUtility.GetActiveBuildTarget();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _settings = ZeonAssetEditorGuiUtility.DrawSettingsHeader(ref _settings);
            if (_settings == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("路径总览", GUILayout.Height(26)))
                    ShowPaths();
                if (GUILayout.Button("清单历史", GUILayout.Height(26)))
                    ShowManifestHistory();
                if (GUILayout.Button("收集+依赖", GUILayout.Height(26)))
                    Analyze();
                if (GUILayout.Button("Tag 命中", GUILayout.Height(26)))
                    ShowTagCoverage();
                if (GUILayout.Button("体积报告", GUILayout.Height(26)))
                    ShowBuildVolumeReport();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void ShowPaths()
        {
            var output = ZeonAssetSettingsUtility.GetBuildOutputPath(_settings, _buildTarget);
            var cdn = TaskPublishCdn.GetCdnRoot(_settings.CdnFolder, _buildTarget.ToString());
            var streaming = DiskCacheManager.GetStreamingRoot();
            var sb = new StringBuilder();
            sb.AppendLine($"Package: {_settings.PackageName}");
            sb.AppendLine($"Target: {_buildTarget}");
            sb.AppendLine($"Build Output: {output}  exists={Directory.Exists(output)}");
            sb.AppendLine($"CDN Root: {cdn}  exists={Directory.Exists(cdn)}");
            sb.AppendLine($"StreamingAssets: {streaming}  exists={Directory.Exists(streaming)}");
            sb.AppendLine($"Groups={_settings.Groups?.Count ?? 0}  TagRules={_settings.TagRules?.Count ?? 0}");

            var manifestsDir = Path.Combine(cdn, DiskCacheManager.ManifestsFolder);
            if (ManifestBuildInfoUtility.TryGetLatestManifest(manifestsDir, out var latest, out var latestPath))
            {
                sb.AppendLine();
                sb.AppendLine("=== 最近 CDN 清单 ===");
                sb.AppendLine(ManifestBuildInfoUtility.FormatSummary(latest));
                sb.AppendLine("path: " + latestPath);
            }

            _report = sb.ToString();
        }

        private void ShowManifestHistory()
        {
            var cdn = TaskPublishCdn.GetCdnRoot(_settings.CdnFolder, _buildTarget.ToString());
            var buildOut = ZeonAssetSettingsUtility.GetBuildOutputPath(_settings, _buildTarget);
            var sb = new StringBuilder();
            sb.AppendLine("=== CDN Manifests（新→旧）===");
            AppendManifestList(sb, Path.Combine(cdn, DiskCacheManager.ManifestsFolder));
            sb.AppendLine();
            sb.AppendLine("=== Build Output Manifests（新→旧）===");
            AppendManifestList(sb, Path.Combine(buildOut, DiskCacheManager.ManifestsFolder));
            _report = sb.ToString();
        }

        private static void AppendManifestList(StringBuilder sb, string dir)
        {
            if (!Directory.Exists(dir))
            {
                sb.AppendLine($"(目录不存在) {dir}");
                return;
            }

            var list = ManifestBuildInfoUtility.ListManifests(dir, 15);
            if (list.Count == 0)
            {
                sb.AppendLine("(无 .bytes 清单)");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var name = Path.GetFileName(item.path);
                sb.AppendLine(
                    $"{i + 1}. {name}  hash={item.manifest.ManifestHash}  " +
                    $"build={ManifestBuildInfoUtility.FormatBuildTime(item.manifest.BuildTime)}");
            }
        }

        private void ShowTagCoverage()
        {
            var cov = AssetTagRuleUtility.PreviewCoverage(_settings);
            var sb = new StringBuilder();
            sb.AppendLine("=== Tag Rules ===");
            for (int i = 0; i < cov.Count; i++)
            {
                var tags = cov[i].tags.Count > 0 ? string.Join(",", cov[i].tags) : "-";
                sb.AppendLine($"{cov[i].pathPrefix}  [{tags}]  assets={cov[i].assetCount}");
            }

            if (cov.Count == 0) sb.AppendLine("(无)");
            _report = sb.ToString();
        }

        private void ShowBuildVolumeReport()
        {
            var output = ZeonAssetSettingsUtility.GetBuildOutputPath(_settings, _buildTarget);
            var path = Path.Combine(output, TaskBuildReport.ReportFileName);
            if (!File.Exists(path))
            {
                _report = "未找到 BuildReport.json，请先执行 Resource/Build。\n" + path;
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var report = JsonUtility.FromJson<BuildVolumeReport>(json);
                var sb = new StringBuilder();
                sb.AppendLine("=== Build Volume Report ===");
                sb.AppendLine($"Package={report.PackageName} Platform={report.Platform}");
                sb.AppendLine($"Manifest={report.ManifestFileName} hash={report.ManifestHash}");
                sb.AppendLine(
                    $"Total={TaskBuildReport.FormatBytes(report.TotalBundleBytes)} " +
                    $"Bundles={report.BundleCount} Assets={report.AssetCount} AutoExtracted={report.AutoExtractedCount}");
                sb.AppendLine();
                sb.AppendLine("--- Top Bundles by Size ---");
                if (report.TopBundlesBySize != null)
                {
                    for (int i = 0; i < report.TopBundlesBySize.Count; i++)
                    {
                        var e = report.TopBundlesBySize[i];
                        sb.AppendLine(
                            $"{i + 1}. {TaskBuildReport.FormatBytes(e.FileSize)}  " +
                            $"{e.BundleName}  assets={e.AssetCount}" +
                            (e.IsAutoExtracted ? "  [AUTO]" : ""));
                    }
                }

                sb.AppendLine();
                sb.AppendLine("--- Top Shared Implicit Assets ---");
                if (report.TopSharedAssets != null)
                {
                    for (int i = 0; i < report.TopSharedAssets.Count; i++)
                    {
                        var e = report.TopSharedAssets[i];
                        sb.AppendLine($"{i + 1}. refs={e.ReferencedByBundleCount}  {e.AssetPath}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("raw: " + path);
                _report = sb.ToString();
            }
            catch (System.Exception e)
            {
                _report = "读取 BuildReport 失败: " + e.Message;
            }
        }

        private void Analyze()
        {
            var context = new BuildContext
            {
                Parameters = new BuildParameters
                {
                    Settings = _settings,
                    ClearOutputFolder = false,
                    PublishToCdn = false,
                    BuildTarget = _buildTarget,
                }
            };

            try
            {
                new TaskPrepare().Run(context);
                new TaskAnalyzeDependency().Run(context);
            }
            catch (System.Exception e)
            {
                _report = "分析失败:\n" + e;
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Assets={context.CollectedAssets.Count} Bundles={context.BundleMap.Count}");
            sb.AppendLine("=== Bundles ===");
            foreach (var pair in context.BundleMap)
            {
                var b = pair.Value;
                var tags = b.Tags != null && b.Tags.Count > 0 ? string.Join(",", b.Tags) : "-";
                sb.AppendLine($"[{(b.IsAutoExtracted ? "AUTO" : "USER")}] {b.BundleName}");
                sb.AppendLine($"  assets={b.AssetPaths.Count} deps={b.DependBundles.Count} tags=[{tags}]");
            }

            _report = sb.ToString();
        }
    }
}
