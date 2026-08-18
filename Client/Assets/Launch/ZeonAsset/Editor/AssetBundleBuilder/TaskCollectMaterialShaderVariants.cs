using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 构建期：若工程里已有 ShaderVariantCollection，把待打包材质 Keywords 合并进去。
    /// 没有 SVC 则跳过，不生成占位 Shader。
    /// </summary>
    public class TaskCollectMaterialShaderVariants : IBuildTask
    {
        public string Name => "TaskCollectMaterialShaderVariants";

        public const string DefaultSearchRoot = "Assets/Game";
        private const int MaxVariants = 2048;

        public void Run(BuildContext context)
        {
            var svc = FindGameShaderVariantCollection();
            if (svc == null)
            {
                context.Log("未找到 ShaderVariantCollection（Assets/Game），跳过材质变体收集。");
                return;
            }

            svc.Clear();
            int added = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectMaterialPaths(context, materials);

            foreach (var matPath in materials)
            {
                if (added >= MaxVariants)
                    break;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null || mat.shader == null)
                    continue;

                var shader = mat.shader;
                var keywords = mat.shaderKeywords ?? Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);

                if (!TryAddVariant(svc, shader, PassType.ForwardBase, keywords, seen))
                    continue;

                added++;
            }

            EditorUtility.SetDirty(svc);
            AssetDatabase.SaveAssets();
            context.Log($"材质 Keywords → {AssetDatabase.GetAssetPath(svc)}: materials={materials.Count}, variants≈{svc.variantCount} (cap={MaxVariants})");
        }

        private static ShaderVariantCollection FindGameShaderVariantCollection()
        {
            return FindInFolder(DefaultSearchRoot);
        }

        private static ShaderVariantCollection FindInFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return null;

            var guids = AssetDatabase.FindAssets("t:ShaderVariantCollection", new[] { folder });
            if (guids == null || guids.Length == 0)
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
        }

        private static void CollectMaterialPaths(BuildContext context, HashSet<string> materials)
        {
            if (context?.BundleMap == null)
                return;

            foreach (var pair in context.BundleMap)
            {
                var paths = pair.Value?.AssetPaths;
                if (paths == null)
                    continue;

                for (int i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    {
                        materials.Add(path);
                        continue;
                    }

                    // Prefab / Model：依赖里的材质
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    {
                        var deps = AssetDatabase.GetDependencies(path, true);
                        for (int d = 0; d < deps.Length; d++)
                        {
                            if (deps[d].EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                                materials.Add(deps[d]);
                        }
                    }
                }
            }
        }

        private static bool TryAddVariant(
            ShaderVariantCollection svc,
            Shader shader,
            PassType passType,
            string[] keywords,
            HashSet<string> seen)
        {
            if (svc == null || shader == null)
                return false;

            var key = shader.name + "|" + passType + "|" + string.Join(",", keywords ?? Array.Empty<string>());
            if (!seen.Add(key))
                return false;

            try
            {
                var variant = new ShaderVariantCollection.ShaderVariant(shader, passType, keywords);
                return svc.Add(variant);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
