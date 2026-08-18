using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>路径前缀 → Tag 规则。</summary>
    public class ZeonAssetTagWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private Vector2 _scroll;
        private string _scanRoot = "Assets/";
        private string _filter = string.Empty;
        private string _status = string.Empty;

        [MenuItem("ZeonAsset/标签", priority = 105)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetTagWindow>("标签");
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
                return;

            if (_settings.TagRules == null)
                _settings.TagRules = new List<AssetPathTagRule>();

            using (new EditorGUILayout.HorizontalScope())
            {
                _scanRoot = EditorGUILayout.TextField("扫描根目录", _scanRoot);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var abs = EditorUtility.OpenFolderPanel("选择目录", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        abs = abs.Replace('\\', '/');
                        var data = Application.dataPath.Replace('\\', '/');
                        if (abs.StartsWith(data))
                            _scanRoot = "Assets" + abs.Substring(data.Length);
                    }
                }

                if (GUILayout.Button("生成子目录规则", GUILayout.Width(120)))
                {
                    int n = AssetTagRuleUtility.GenerateRulesFromTopFolders(_settings, _scanRoot);
                    EditorUtility.SetDirty(_settings);
                    _status = n > 0
                        ? $"已新增 {n} 条。请把 Tags 改成等级，如 Level1。"
                        : "没有新增（目录无效或已存在）。";
                }
            }

            _filter = EditorGUILayout.TextField("过滤", _filter);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 空规则", GUILayout.Width(100)))
                {
                    _settings.TagRules.Add(new AssetPathTagRule
                    {
                        PathPrefix = AssetTagRuleUtility.NormalizePrefix(_scanRoot),
                        Tags = new List<string>(),
                    });
                    EditorUtility.SetDirty(_settings);
                }

                if (GUILayout.Button("预览命中", GUILayout.Width(100)))
                {
                    var cov = AssetTagRuleUtility.PreviewCoverage(_settings);
                    var sb = new StringBuilder();
                    sb.AppendLine($"命中预览（{cov.Count} 条）：");
                    for (int i = 0; i < cov.Count; i++)
                    {
                        var tags = cov[i].tags.Count > 0 ? string.Join(",", cov[i].tags) : "-";
                        sb.AppendLine($"  {cov[i].pathPrefix}  [{tags}]  x{cov[i].assetCount}");
                    }

                    _status = sb.ToString();
                }
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("开", GUILayout.Width(18));
                GUILayout.Label("Path Prefix", GUILayout.MinWidth(220));
                GUILayout.Label("Tags", GUILayout.MinWidth(120));
                GUILayout.Label("备注", GUILayout.Width(80));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();

            for (int i = 0; i < _settings.TagRules.Count; i++)
            {
                var rule = _settings.TagRules[i];
                if (rule == null) continue;

                if (!string.IsNullOrEmpty(_filter))
                {
                    var f = _filter;
                    bool hit =
                        (rule.PathPrefix != null &&
                         rule.PathPrefix.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (rule.Note != null &&
                         rule.Note.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (rule.Tags != null && rule.Tags.Exists(t =>
                            t != null && t.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0));
                    if (!hit) continue;
                }

                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    rule.Active = EditorGUILayout.Toggle(rule.Active, GUILayout.Width(18));
                    rule.PathPrefix = EditorGUILayout.TextField(rule.PathPrefix, GUILayout.MinWidth(200));
                    if (GUILayout.Button("…", GUILayout.Width(24)))
                    {
                        var abs = EditorUtility.OpenFolderPanel("路径前缀", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(abs))
                        {
                            abs = abs.Replace('\\', '/');
                            var data = Application.dataPath.Replace('\\', '/');
                            if (abs.StartsWith(data))
                                rule.PathPrefix = AssetTagRuleUtility.NormalizePrefix(
                                    "Assets" + abs.Substring(data.Length));
                        }
                    }

                    var tagsJoined = rule.Tags != null ? string.Join(",", rule.Tags) : string.Empty;
                    var nextTags = EditorGUILayout.TextField(tagsJoined, GUILayout.MinWidth(100));
                    if (nextTags != tagsJoined)
                    {
                        if (rule.Tags == null) rule.Tags = new List<string>();
                        rule.Tags.Clear();
                        var parts = nextTags.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                        for (int p = 0; p < parts.Length; p++)
                            rule.Tags.Add(parts[p].Trim());
                    }

                    rule.Note = EditorGUILayout.TextField(rule.Note ?? string.Empty, GUILayout.Width(80));

                    if (GUILayout.Button("↑", GUILayout.Width(22)) && i > 0)
                    {
                        var tmp = _settings.TagRules[i - 1];
                        _settings.TagRules[i - 1] = rule;
                        _settings.TagRules[i] = tmp;
                        break;
                    }

                    if (GUILayout.Button("↓", GUILayout.Width(22)) && i < _settings.TagRules.Count - 1)
                    {
                        var tmp = _settings.TagRules[i + 1];
                        _settings.TagRules[i + 1] = rule;
                        _settings.TagRules[i] = tmp;
                        break;
                    }

                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        _settings.TagRules.RemoveAt(i);
                        break;
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);
        }
    }
}
