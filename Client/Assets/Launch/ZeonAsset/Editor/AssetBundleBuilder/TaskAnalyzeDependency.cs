using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 2. 依赖分析与冗余资源提取 (Auto-Promote) + DAG 循环依赖校验。
    /// </summary>
    public class TaskAnalyzeDependency : IBuildTask
    {
        public string Name => "TaskAnalyzeDependency";

        private const string AutoExtractPrefix = "auto_extracted_";

        public void Run(BuildContext context)
        {
            BuildImplicitDependencyGraph(context);
            AutoPromoteSharedDependencies(context);
            ExtractShadersBundle(context);
            BuildBundleDependencyEdges(context);
            ValidateNoCircularDependency(context);

            context.Log($"依赖分析完成: Bundles={context.BundleMap.Count}, ImplicitNodes={context.ImplicitDependencyGraph.Count}");
        }

        /// <summary>
        /// 遍历所有待打包资源的 GetDependencies，构建 AssetPath -> 引用 Bundle 集合。
        /// </summary>
        private static void BuildImplicitDependencyGraph(BuildContext context)
        {
            context.ImplicitDependencyGraph.Clear();

            foreach (var pair in context.BundleMap)
            {
                var bundleName = pair.Key;
                var bundle = pair.Value;

                foreach (var assetPath in bundle.AssetPaths)
                {
                    var dependencies = AssetDatabase.GetDependencies(assetPath, true);
                    foreach (var dep in dependencies)
                    {
                        if (string.IsNullOrEmpty(dep) || dep == assetPath)
                            continue;

                        // 脚本/shader 等由 Unity 特殊处理，不参与 AB 冗余提取
                        if (ShouldIgnoreDependency(dep))
                            continue;

                        if (!context.ImplicitDependencyGraph.TryGetValue(dep, out var owners))
                        {
                            owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            context.ImplicitDependencyGraph[dep] = owners;
                        }

                        owners.Add(bundleName);
                    }
                }
            }
        }

        /// <summary>
        /// 被多个 Bundle 隐式引用的资源单独打包，避免重复。
        /// </summary>
        private static void AutoPromoteSharedDependencies(BuildContext context)
        {
            int threshold = Mathf.Max(2, context.Parameters.Settings.AutoPromoteShareThreshold);
            var promoteList = new List<string>();

            foreach (var pair in context.ImplicitDependencyGraph)
            {
                var assetPath = pair.Key;
                var owners = pair.Value;

                if (context.OwnedAssetPaths.Contains(assetPath))
                    continue;

                if (owners.Count < threshold)
                    continue;

                promoteList.Add(assetPath);
            }

            promoteList.Sort(StringComparer.OrdinalIgnoreCase);

            int promoteCount = 0;
            foreach (var assetPath in promoteList)
            {
                var bundleName = AutoExtractPrefix + AssetBundleCollector.NormalizeBundleName(assetPath);
                var bundle = context.GetOrCreateBundle(bundleName);
                bundle.IsAutoExtracted = true;

                if (!bundle.AssetPaths.Contains(assetPath))
                    bundle.AssetPaths.Add(assetPath);

                context.OwnedAssetPaths.Add(assetPath);

                // 同步写入收集列表，便于 Manifest 生成 Address
                context.CollectedAssets.Add(new CollectAssetInfo
                {
                    AssetPath = assetPath,
                    Address = assetPath,
                    BundleName = bundleName,
                    GroupName = "AutoExtracted",
                    CollectorName = "AutoPromote",
                });

                promoteCount++;
                context.Log($"Auto-Promote: {assetPath} -> {bundleName} (shared by {context.ImplicitDependencyGraph[assetPath].Count} bundles)");
            }

            context.Log($"Auto-Promote 完成，提升共享依赖 {promoteCount} 个");
        }

        /// <summary>
        /// 把 Shader 和 ShaderVariantCollection 提取到独立 shaders.bundle。
        /// </summary>
        private static void ExtractShadersBundle(BuildContext context)
        {
            const string shadersBundleName = "shaders";

            var shaderAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectShaderAssetsFromSet(context.OwnedAssetPaths, shaderAssets);
            CollectShaderAssetsFromSet(context.ImplicitDependencyGraph.Keys, shaderAssets);

            // 全工程扫描项目内 Shader / SVC（不含 Packages）
            CollectShaderAssetsByFind(shaderAssets);

            if (shaderAssets.Count == 0)
            {
                context.LogWarning("未发现任何可打包 Shader 资源，shaders.bundle 将不会生成");
                return;
            }

            // 从其它 Bundle 中移除
            var emptyBundles = new List<string>();
            foreach (var pair in context.BundleMap)
            {
                if (pair.Key.Equals(shadersBundleName, StringComparison.OrdinalIgnoreCase))
                    continue;

                pair.Value.AssetPaths.RemoveAll(IsShaderRelatedAsset);
                if (pair.Value.AssetPaths.Count == 0)
                    emptyBundles.Add(pair.Key);
            }

            for (int i = 0; i < emptyBundles.Count; i++)
                context.BundleMap.Remove(emptyBundles[i]);

            var bundle = context.GetOrCreateBundle(shadersBundleName);
            bundle.IsAutoExtracted = true;
            if (!bundle.Tags.Contains("Shaders"))
                bundle.Tags.Add("Shaders");

            foreach (var assetPath in shaderAssets)
            {
                if (!bundle.AssetPaths.Contains(assetPath))
                    bundle.AssetPaths.Add(assetPath);

                context.OwnedAssetPaths.Add(assetPath);

                bool exists = false;
                for (int i = 0; i < context.CollectedAssets.Count; i++)
                {
                    if (context.CollectedAssets[i].AssetPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        context.CollectedAssets[i].BundleName = shadersBundleName;
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    context.CollectedAssets.Add(new CollectAssetInfo
                    {
                        AssetPath = assetPath,
                        Address = assetPath,
                        BundleName = shadersBundleName,
                        GroupName = "Shaders",
                        CollectorName = "ForceExtract",
                        Tags = { "Shaders" },
                    });
                }
            }

            context.Log($"Shader 强制提取完成: count={shaderAssets.Count} -> {shadersBundleName}.bundle");
            foreach (var p in shaderAssets)
                context.Log($"  + {p}");
        }

        private static void CollectShaderAssetsFromSet(IEnumerable<string> paths, HashSet<string> result)
        {
            if (paths == null)
                return;
            foreach (var path in paths)
            {
                if (IsShaderRelatedAsset(path))
                    result.Add(path);
            }
        }

        private static void CollectShaderAssetsByFind(HashSet<string> result)
        {
            var shaderGuids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" });
            for (int i = 0; i < shaderGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                if (IsPackableProjectShader(path))
                    result.Add(path);
            }

            var svcGuids = AssetDatabase.FindAssets("t:ShaderVariantCollection", new[] { "Assets" });
            for (int i = 0; i < svcGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(svcGuids[i]);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
            }
        }

        private static bool IsPackableProjectShader(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return false;

            // 跳过编辑器专用、包缓存镜像
            var normalized = assetPath.Replace('\\', '/');
            if (normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return IsShaderRelatedAsset(assetPath);
        }

        private static bool IsShaderRelatedAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var ext = System.IO.Path.GetExtension(assetPath);
            return ext.Equals(".shader", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".shadergraph", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".shadersubgraph", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".shadervariants", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".compute", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 根据依赖资源归属，建立 Bundle -> Bundle 依赖边。
        /// </summary>
        private static void BuildBundleDependencyEdges(BuildContext context)
        {
            // assetPath -> owning bundle
            var assetOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in context.BundleMap)
            {
                foreach (var assetPath in pair.Value.AssetPaths)
                    assetOwner[assetPath] = pair.Key;
            }

            foreach (var pair in context.BundleMap)
            {
                var bundleName = pair.Key;
                var bundle = pair.Value;
                bundle.DependBundles.Clear();

                foreach (var assetPath in bundle.AssetPaths)
                {
                    var dependencies = AssetDatabase.GetDependencies(assetPath, true);
                    foreach (var dep in dependencies)
                    {
                        if (ShouldIgnoreDependency(dep))
                            continue;

                        if (!assetOwner.TryGetValue(dep, out var ownerBundle))
                            continue;

                        if (!ownerBundle.Equals(bundleName, StringComparison.OrdinalIgnoreCase))
                            bundle.DependBundles.Add(ownerBundle);
                    }
                }
            }
        }

        /// <summary>
        /// 检查依赖图是否有环；有环则终止构建。
        /// </summary>
        private static void ValidateNoCircularDependency(BuildContext context)
        {
            var nodes = context.BundleMap.Keys.ToList();
            var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodes)
            {
                indegree[node] = 0;
                graph[node] = new List<string>();
            }

            foreach (var pair in context.BundleMap)
            {
                var from = pair.Key;
                foreach (var to in pair.Value.DependBundles)
                {
                    if (!graph.ContainsKey(to))
                        continue;

                    graph[from].Add(to);
                    indegree[to] = indegree[to] + 1;
                }
            }

            var queue = new Queue<string>();
            foreach (var node in nodes)
            {
                if (indegree[node] == 0)
                    queue.Enqueue(node);
            }

            int visited = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                visited++;

                foreach (var next in graph[current])
                {
                    indegree[next]--;
                    if (indegree[next] == 0)
                        queue.Enqueue(next);
                }
            }

            if (visited == nodes.Count)
            {
                context.Log("DAG 校验通过：未检测到 Bundle 循环依赖");
                return;
            }

            var cycleNodes = indegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(n => n).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("检测到 Bundle 循环依赖，构建已终止。涉及节点：");
            foreach (var node in cycleNodes)
            {
                var deps = context.BundleMap[node].DependBundles.OrderBy(d => d);
                sb.AppendLine($"  - {node} -> [{string.Join(", ", deps)}]");
            }

            throw new BuildException(sb.ToString());
        }

        private static bool ShouldIgnoreDependency(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            assetPath = assetPath.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return true;

            var ext = System.IO.Path.GetExtension(assetPath);
            if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".js", StringComparison.OrdinalIgnoreCase))
                return true;

            // Lighting data 等通常不作为共享提取目标
            if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
