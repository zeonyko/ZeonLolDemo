using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 1. 准备与环境清理：校验参数、创建输出目录、收集资源并初始化 BundleMap。
    /// </summary>
    public class TaskPrepare : IBuildTask
    {
        public string Name => "TaskPrepare";

        public void Run(BuildContext context)
        {
            var settings = context.Parameters.Settings;
            var outputFolder = string.IsNullOrEmpty(context.Parameters.OutputFolder)
                ? settings.OutputFolder
                : context.Parameters.OutputFolder;

            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = "Bundles";

            var platformFolder = context.Parameters.BuildTarget.ToString();
            context.OutputPath = ZeonAssetPathLayout.GetEditorBuildOutputRoot(
                    settings.OutputFolder,
                    platformFolder)
                .Replace('\\', '/');

            if (context.Parameters.ClearOutputFolder && Directory.Exists(context.OutputPath))
            {
                context.Log($"清理输出目录: {context.OutputPath}");
                Directory.Delete(context.OutputPath, true);
            }

            Directory.CreateDirectory(context.OutputPath);

            context.CollectedAssets = AssetBundleCollector.Collect(settings);
            if (context.CollectedAssets.Count == 0)
                throw new BuildException("没有收集到任何可打包资源，请检查 AssetCollectorSettings。");

            AssetTagRuleUtility.ApplyPathRules(settings, context.CollectedAssets);

            foreach (var asset in context.CollectedAssets)
            {
                context.OwnedAssetPaths.Add(asset.AssetPath);
                var bundle = context.GetOrCreateBundle(asset.BundleName);
                if (!bundle.AssetPaths.Contains(asset.AssetPath))
                    bundle.AssetPaths.Add(asset.AssetPath);

                foreach (var tag in asset.Tags)
                {
                    if (!bundle.Tags.Contains(tag))
                        bundle.Tags.Add(tag);
                }
            }

            context.Log($"收集完成: Assets={context.CollectedAssets.Count}, Bundles={context.BundleMap.Count}, Output={context.OutputPath}");
        }
    }
}
