using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>资源收集：Group / Collector / PackRule。</summary>
    public class ZeonAssetCollectorWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private Vector2 _scroll;

        [MenuItem("ZeonAsset/收集", priority = 100)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetCollectorWindow>("收集");
            ZeonAssetEditorGuiUtility.ApplyWindowSize(w);
            w.Show();
        }

        private void OnEnable()
        {
            ZeonAssetEditorGuiUtility.ApplyWindowSize(this);
            _settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _settings = ZeonAssetEditorGuiUtility.DrawSettingsHeader(ref _settings);
            if (_settings == null)
            {
                EditorGUILayout.HelpBox("请先创建 AssetCollectorSettings。", MessageType.Warning);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();

            _settings.PackageName = EditorGUILayout.TextField("Package Id", _settings.PackageName);
            _settings.AutoPromoteShareThreshold = EditorGUILayout.IntField(
                "Auto-Promote 阈值", _settings.AutoPromoteShareThreshold);
            _settings.CollectAtlasSourceSprites = EditorGUILayout.Toggle(
                "收集图集源散图", _settings.CollectAtlasSourceSprites);
            if (!_settings.CollectAtlasSourceSprites)
                EditorGUILayout.HelpBox(
                    "SpriteAtlas 覆盖的散图不当独立资源。Prefab 继续引用散图，打包只进图集。",
                    MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Groups ({_settings.Groups.Count})", EditorStyles.boldLabel);

            for (int g = 0; g < _settings.Groups.Count; g++)
            {
                var group = _settings.Groups[g];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        group.Active = EditorGUILayout.Toggle(group.Active, GUILayout.Width(18));
                        group.GroupName = EditorGUILayout.TextField(group.GroupName);
                        if (GUILayout.Button("X", GUILayout.Width(24)))
                        {
                            _settings.Groups.RemoveAt(g);
                            break;
                        }
                    }

                    for (int c = 0; c < group.Collectors.Count; c++)
                    {
                        var collector = group.Collectors[c];
                        using (new EditorGUILayout.VerticalScope("box"))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                collector.Active = EditorGUILayout.Toggle(collector.Active, GUILayout.Width(18));
                                collector.CollectorName = EditorGUILayout.TextField(collector.CollectorName);
                                if (GUILayout.Button("X", GUILayout.Width(24)))
                                {
                                    group.Collectors.RemoveAt(c);
                                    break;
                                }
                            }

                            collector.CollectPath = ZeonAssetSettingsUtility.DrawFolderField(
                                "Collect Path", collector.CollectPath);
                            collector.Filter = (ECollectorFilter)EditorGUILayout.EnumPopup("Filter", collector.Filter);
                            if (collector.Filter == ECollectorFilter.CollectByExtension)
                                collector.CustomExtensions = EditorGUILayout.TextField(
                                    "Extensions", collector.CustomExtensions);
                            collector.PackRule = (EPackRule)EditorGUILayout.EnumPopup("Pack Rule", collector.PackRule);
                            collector.Addressable = EditorGUILayout.Toggle("Addressable", collector.Addressable);
                        }
                    }

                    if (GUILayout.Button("+ Collector"))
                        group.Collectors.Add(new AssetCollectorEntry());
                }
            }

            if (GUILayout.Button("+ Group"))
            {
                _settings.Groups.Add(new AssetCollectorGroup
                {
                    GroupName = "NewGroup",
                    Collectors = { new AssetCollectorEntry() }
                });
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("预览收集结果", GUILayout.Height(28)))
                PreviewCollect();
        }

        private void PreviewCollect()
        {
            var assets = AssetBundleCollector.Collect(_settings);
            AssetTagRuleUtility.ApplyPathRules(_settings, assets);
            var groups = AssetBundleCollector.GroupByBundle(assets);
            Debug.Log($"[ZeonAsset] 预览收集: Assets={assets.Count}, Bundles={groups.Count}");
            foreach (var pair in groups)
            {
                var tags = new HashSet<string>();
                foreach (var a in pair.Value)
                {
                    if (a.Tags == null) continue;
                    foreach (var t in a.Tags) tags.Add(t);
                }

                Debug.Log($"  Bundle [{pair.Key}] x{pair.Value.Count} tags=[{string.Join(",", tags)}]");
            }

            EditorUtility.DisplayDialog(
                "Resource",
                $"收集到 {assets.Count} 个资源，将生成 {groups.Count} 个 Bundle。\n详情见 Console。",
                "OK");
        }
    }
}
