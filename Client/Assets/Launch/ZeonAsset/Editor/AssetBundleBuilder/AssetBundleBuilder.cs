using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 构建管线总驱动器：按顺序执行 IBuildTask 链。
    /// </summary>
    public static class AssetBundleBuilder
    {
        public static PackageManifest Build(BuildParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (parameters.Settings == null)
                throw new ArgumentException("BuildParameters.Settings is null.");
            if (parameters.BuildTarget == BuildTarget.NoTarget)
                parameters.BuildTarget = EditorUserBuildSettings.activeBuildTarget;

            var context = new BuildContext
            {
                Parameters = parameters,
            };

            var tasks = CreateDefaultTasks();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            context.Log($"==== Resource Build Start | Target={parameters.BuildTarget} | Package={parameters.Settings.PackageName} ====");

            try
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    var task = tasks[i];
                    var taskSw = System.Diagnostics.Stopwatch.StartNew();
                    context.Log($"---- [{i + 1}/{tasks.Count}] {task.Name} ----");
                    task.Run(context);
                    context.Log($"---- {task.Name} done in {taskSw.ElapsedMilliseconds} ms ----");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeonAsset] Build Failed: {e}");
                throw;
            }

            sw.Stop();
            context.Log($"==== Resource Build Success in {sw.ElapsedMilliseconds} ms | Bundles={context.BundleMap.Count} Assets={context.CollectedAssets.Count} ====");
            AssetDatabase.Refresh();
            return context.Manifest;
        }

        public static List<IBuildTask> CreateDefaultTasks()
        {
            return new List<IBuildTask>
            {
                new TaskPrepare(),
                new TaskCollectMaterialShaderVariants(),
                new TaskAnalyzeDependency(),
                new TaskBuilding(),
                new TaskEncryption(),
                new TaskCreateManifest(),
                new TaskBuildReport(),
                new TaskPublishCdn(),
                new TaskCleanUp(),
            };
        }
    }
}
