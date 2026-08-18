using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 把 .json 导成 TextAsset，才能进 Bundle，并走 LoadRawFile / LoadAsset&lt;TextAsset&gt;。
    /// Unity 原生已处理 .json：不能写在 fileExtensions 里，只能放在 overrideExts，
    /// 再通过 SetImporterOverride 启用（官方示例同理：fbb 主扩展 + 覆盖 fbx）。
    /// </summary>
    [ScriptedImporter(3, new[] { "zeonjson" }, new[] { "json" })]
    public sealed class JsonTextAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = File.ReadAllText(ctx.assetPath);
            var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
    }

    /// <summary>域重载 / 新增资源时，把 Assets 下 .json 切到 JsonTextAssetImporter。</summary>
    [InitializeOnLoad]
    internal static class JsonTextAssetImporterBootstrap
    {
        private static readonly HashSet<string> Pending = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static bool _flushScheduled;

        static JsonTextAssetImporterBootstrap()
        {
            EditorApplication.delayCall += EnsureAllOverrides;
        }

        private static void EnsureAllOverrides()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
                QueueIfNeeded(AssetDatabase.GUIDToAssetPath(guids[i]));
            FlushPending();
        }

        internal static void QueueIfNeeded(string assetPath)
        {
            if (!NeedsOverride(assetPath))
                return;
            Pending.Add(assetPath);
            ScheduleFlush();
        }

        private static void ScheduleFlush()
        {
            if (_flushScheduled)
                return;
            _flushScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _flushScheduled = false;
                FlushPending();
            };
        }

        private static void FlushPending()
        {
            if (Pending.Count == 0)
                return;

            var paths = new List<string>(Pending);
            Pending.Clear();

            var dirty = false;
            for (int i = 0; i < paths.Count; i++)
            {
                if (TryApplyOverride(paths[i]))
                    dirty = true;
            }

            if (dirty)
                AssetDatabase.Refresh();
        }

        private static bool NeedsOverride(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return false;
            if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                return false;
            if (!assetPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                return false;
            return AssetDatabase.GetImporterOverride(assetPath) != typeof(JsonTextAssetImporter);
        }

        private static bool TryApplyOverride(string assetPath)
        {
            if (!NeedsOverride(assetPath))
                return false;

            AssetDatabase.SetImporterOverride<JsonTextAssetImporter>(assetPath);
            return true;
        }
    }

    internal sealed class JsonTextAssetImporterPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets != null)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                    JsonTextAssetImporterBootstrap.QueueIfNeeded(importedAssets[i]);
            }

            if (movedAssets != null)
            {
                for (int i = 0; i < movedAssets.Length; i++)
                    JsonTextAssetImporterBootstrap.QueueIfNeeded(movedAssets[i]);
            }
        }
    }
}
