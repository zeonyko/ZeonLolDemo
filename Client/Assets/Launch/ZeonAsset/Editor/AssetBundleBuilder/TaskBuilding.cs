using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

#if ZEONASSET_SBP
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine.Build.Pipeline;
#endif

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 3. 调用 SBP ContentPipeline / 或经典 BuildPipeline 打包。
    /// UseSBP=true 时失败直接中断，不回退。
    /// </summary>
    public class TaskBuilding : IBuildTask
    {
        public string Name => "TaskBuilding";

        public void Run(BuildContext context)
        {
            var builds = new List<AssetBundleBuild>(context.BundleMap.Count);

            foreach (var pair in context.BundleMap.OrderBy(p => p.Key))
            {
                var info = pair.Value;
                if (info.AssetPaths == null || info.AssetPaths.Count == 0)
                {
                    context.LogWarning($"跳过空 Bundle: {info.BundleName}");
                    continue;
                }

                // AssetDatabase 路径校验（比 File.Exists 更稳）
                var validAssets = info.AssetPaths
                    .Where(p => !string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                    .Distinct()
                    .ToArray();

                if (validAssets.Length == 0)
                {
                    context.LogWarning($"Bundle 内资源均无效，跳过: {info.BundleName}");
                    continue;
                }

                // 输出文件名固定带 .bundle，方便后续 TaskCreateManifest / CDN 布局定位
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = info.BundleName + ".bundle",
                    assetNames = validAssets,
                });
            }

            if (builds.Count == 0)
                throw new BuildException("没有可构建的 AssetBundleBuild。");

            var buildArray = builds.ToArray();
            bool useSbp = context.Parameters.UseSBP;
            Func<string, string[]> getDependencies;

            var prevPacker = EditorSettings.spritePackerMode;
            bool restorePacker = prevPacker == SpritePackerMode.Disabled;
            if (restorePacker)
            {
                EditorSettings.spritePackerMode = SpritePackerMode.BuildTimeOnlyAtlas;
                context.Log("编辑器 Sprite Packer 为 Disabled，打包期间临时改为 BuildTimeOnlyAtlas");
            }

            try
            {
#if ZEONASSET_SBP
                if (useSbp)
                {
                    getDependencies = BuildWithSbp(context, buildArray);
                }
                else
#endif
                {
                    if (useSbp)
                        context.LogWarning("工程未解析到 SBP 包（ZEONASSET_SBP 未定义），改用经典 BuildPipeline。请检查 Packages/manifest.json。");

                    getDependencies = BuildWithClassic(context, buildArray);
                }
            }
            finally
            {
                if (restorePacker)
                    EditorSettings.spritePackerMode = prevPacker;
            }

            foreach (var pair in context.BundleMap)
            {
                var fileName = pair.Value.BundleName + ".bundle";
                var deps = getDependencies(fileName);
                pair.Value.DependBundles.Clear();
                if (deps == null)
                    continue;

                foreach (var dep in deps)
                {
                    var depName = Path.GetFileNameWithoutExtension(dep);
                    if (!string.IsNullOrEmpty(depName))
                        pair.Value.DependBundles.Add(depName);
                }
            }

            context.Log($"打包完成，输出目录: {context.OutputPath}");
        }

#if ZEONASSET_SBP
        private static Func<string, string[]> BuildWithSbp(BuildContext context, AssetBundleBuild[] buildArray)
        {
            LogDirtyScenes(context);

            var incremental = context.Parameters.IncrementalBuild;
            var options = context.Parameters.BuildOptions;
            if (!incremental)
                options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

            context.Log(
                $"[SBP] Incremental={incremental} BuildTarget={context.Parameters.BuildTarget} " +
                $"BuildOptions={options} OutputPath={context.OutputPath}");
            context.Log($"[SBP] AssetBundleBuild Count={buildArray.Length}");
            for (int i = 0; i < buildArray.Length; i++)
            {
                var b = buildArray[i];
                var sample = b.assetNames != null ? b.assetNames.Take(3).ToArray() : Array.Empty<string>();
                context.Log(
                    $"[SBP] ({i}) Bundle={b.assetBundleName} AssetsCount={b.assetNames?.Length ?? 0}" +
                    $" SampleAssets=[{string.Join(", ", sample)}]");
            }

            // 直接走 ContentPipeline，才能拿到真实 ReturnCode（Compatibility 失败时只返回 null）
            var group = BuildPipeline.GetBuildTargetGroup(context.Parameters.BuildTarget);
            var parameters = new BundleBuildParameters(
                context.Parameters.BuildTarget,
                group,
                context.OutputPath)
            {
                UseCache = incremental,
            };

#if UNITY_2018_3_OR_NEWER
            if ((options & BuildAssetBundleOptions.ChunkBasedCompression) != 0)
                parameters.BundleCompression = UnityEngine.BuildCompression.LZ4;
            else if ((options & BuildAssetBundleOptions.UncompressedAssetBundle) != 0)
                parameters.BundleCompression = UnityEngine.BuildCompression.Uncompressed;
            else
                parameters.BundleCompression = UnityEngine.BuildCompression.LZMA;
#endif

            var content = new BundleBuildContent(buildArray);
            context.Log("开始 ContentPipeline.BuildAssetBundles (SBP)");
            ReturnCode exitCode = ContentPipeline.BuildAssetBundles(parameters, content, out IBundleBuildResults results);

            context.Log($"[SBP] ReturnCode={exitCode}");
            if (exitCode < ReturnCode.Success)
            {
                var hint = DescribeReturnCode(exitCode);
                throw new BuildException(
                    $"SBP ContentPipeline 失败: ReturnCode={exitCode}\n{hint}\n" +
                    $"OutputPath={context.OutputPath}, BuildTarget={context.Parameters.BuildTarget}");
            }

            var manifest = ScriptableObject.CreateInstance<CompatibilityAssetBundleManifest>();
            manifest.SetResults(results.BundleInfos);
            var topManifest = Path.Combine(context.OutputPath, Path.GetFileName(context.OutputPath) + ".manifest");
            File.WriteAllText(topManifest, manifest.ToString());

            context.UnityManifest = null;
            context.Log($"SBP ContentPipeline 完成，Bundles={results.BundleInfos.Count}");
            return manifest.GetAllDependencies;
        }

        private static void LogDirtyScenes(BuildContext context)
        {
            var dirty = new List<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    dirty.Add(string.IsNullOrEmpty(scene.path) ? "(Untitled)" : scene.path);
            }

            if (dirty.Count == 0)
            {
                context.Log("[SBP] DirtyScenes=0");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[SBP] 检测到未保存 Scene x{dirty.Count}（SBP 会直接返回 UnsavedChanges）：");
            for (int i = 0; i < dirty.Count; i++)
                sb.AppendLine("  - " + dirty[i]);
            context.LogWarning(sb.ToString().TrimEnd());
        }

        private static string DescribeReturnCode(ReturnCode code)
        {
            switch (code)
            {
                case ReturnCode.UnsavedChanges:
                    return "原因：有未保存的 Scene。请先 Ctrl+S 保存所有场景，或关闭未保存场景后再构建。";
                case ReturnCode.Canceled:
                    return "原因：构建被取消。";
                case ReturnCode.Error:
                    return "原因：SBP 任务返回 Error（常见：SwitchToBuildPlatform 失败 / 脚本编译失败）。请检查 Build Settings 是否安装当前平台模块。";
                case ReturnCode.Exception:
                    return "原因：SBP 抛出异常。请向上翻 Console 查找 SBP/ContentPipeline 的 Exception 日志。";
                case ReturnCode.MissingRequiredObjects:
                    return "原因：缺少必要上下文对象。";
                default:
                    return "请向上翻 Console，查看 SBP 是否有更详细的 Exception / Error。";
            }
        }
#endif

        private static Func<string, string[]> BuildWithClassic(BuildContext context, AssetBundleBuild[] buildArray)
        {
            var incremental = context.Parameters.IncrementalBuild;
            var options = context.Parameters.BuildOptions;
            if (!incremental)
                options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

            context.Log($"开始 BuildPipeline.BuildAssetBundles，数量={buildArray.Length}");
            context.Log(
                $"[BP] Incremental={incremental} BuildTarget={context.Parameters.BuildTarget} " +
                $"BuildOptions={options} OutputPath={context.OutputPath}");

            var manifest = BuildPipeline.BuildAssetBundles(
                context.OutputPath,
                buildArray,
                options,
                context.Parameters.BuildTarget);

            if (manifest == null)
                throw new BuildException("BuildPipeline.BuildAssetBundles 返回 null，打包失败。");

            context.UnityManifest = manifest;
            context.Log("BuildPipeline 完成");
            return manifest.GetAllDependencies;
        }
    }
}
