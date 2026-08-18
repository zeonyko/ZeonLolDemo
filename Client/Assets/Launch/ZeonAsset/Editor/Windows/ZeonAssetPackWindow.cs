using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>按 Tag 打包 StreamingAssets 首包。</summary>
    public class ZeonAssetPackWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private BuiltinDeliveryProfile _profile;
        private Vector2 _scroll;
        private BuildTarget _buildTarget;
        private string _packPreview = string.Empty;
        private string _packInfo = string.Empty;

        [MenuItem("ZeonAsset/打入首包", priority = 120)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetPackWindow>("打入首包");
            ZeonAssetEditorGuiUtility.ApplyWindowSize(w);
            w.Show();
        }

        private void OnEnable()
        {
            ZeonAssetEditorGuiUtility.ApplyWindowSize(this);
            _settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
            _profile = ZeonAssetEditorActions.LoadOrCreateDeliveryProfile();
            _buildTarget = ZeonAssetEditorGuiUtility.GetActiveBuildTarget();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _settings = ZeonAssetEditorGuiUtility.DrawSettingsHeader(ref _settings);
            if (_settings == null)
                return;

            if (_profile == null)
                _profile = ZeonAssetEditorActions.LoadOrCreateDeliveryProfile();

            _profile = ZeonAssetEditorGuiUtility.DrawScriptableObjectField("Delivery Profile", _profile);
            if (_profile == null)
                return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();

            _profile.PreferCdnAsSource = EditorGUILayout.ToggleLeft(
                "优先从模拟 CDN 读全量产物", _profile.PreferCdnAsSource);
            _profile.IncludeDependencyClosure = EditorGUILayout.ToggleLeft(
                "包含依赖闭包", _profile.IncludeDependencyClosure);
            _profile.IncludeUntaggedBundles = EditorGUILayout.ToggleLeft(
                "无 Tag Bundle 也进首包", _profile.IncludeUntaggedBundles);

            EditorGUILayout.Space(4);
            DrawStringList("Builtin Tags", _profile.BuiltinTags);
            DrawStringList("Always Include", _profile.AlwaysIncludeTags);
            DrawStringList("Exclude Tags", _profile.ExcludeTags);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("快速勾选 → Builtin", EditorStyles.boldLabel);
            DrawQuickTagToggles();

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_profile);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览体积", GUILayout.Height(30)))
                {
                    if (BuiltinPackUtility.TryPreview(
                            _settings, _profile, _buildTarget, out var result, out var error))
                        _packPreview = BuiltinManifestBuilder.FormatPreview(result);
                    else
                        _packPreview = error;
                }

                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                if (GUILayout.Button("写入 StreamingAssets", GUILayout.Height(30)))
                {
                    var report = BuiltinPackUtility.Pack(_settings, _profile, _buildTarget);
                    _packInfo = report.Message;
                    if (report.Subset != null)
                        _packPreview = BuiltinManifestBuilder.FormatPreview(report.Subset);
                    EditorUtility.DisplayDialog("Resource", report.Message, "OK");
                }
                GUI.backgroundColor = Color.white;
            }

            if (!string.IsNullOrEmpty(_packPreview))
                EditorGUILayout.HelpBox(_packPreview, MessageType.None);
            if (!string.IsNullOrEmpty(_packInfo) && _packInfo != _packPreview)
                EditorGUILayout.HelpBox(_packInfo, MessageType.Info);
        }

        private static void DrawStringList(string label, List<string> list)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = EditorGUILayout.TextField(list[i]);
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("+ " + label, GUILayout.Width(160)))
                list.Add(string.Empty);
        }

        private void DrawQuickTagToggles()
        {
            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            BuiltinPackUtility.CollectKnownTags(_settings, known);
            var sorted = new List<string>(known);
            sorted.Sort(System.StringComparer.OrdinalIgnoreCase);

            int i = 0;
            while (i < sorted.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < 3 && i < sorted.Count; c++, i++)
                    {
                        var tag = sorted[i];
                        bool on = _profile.BuiltinTags.Exists(t =>
                            string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase));
                        bool next = GUILayout.Toggle(on, tag, "Button");
                        if (next == on) continue;
                        if (next) _profile.BuiltinTags.Add(tag);
                        else
                            _profile.BuiltinTags.RemoveAll(t =>
                                string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }
    }
}
