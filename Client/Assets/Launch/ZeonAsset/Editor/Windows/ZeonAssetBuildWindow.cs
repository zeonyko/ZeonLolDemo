using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>构建 AssetBundles（不发 CDN）。</summary>
    public class ZeonAssetBuildWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private BuildTarget _buildTarget;
        private bool _clearOutputFolder = true;
        private bool _useSbp = true;
        private bool _incrementalBuild;
        private EBundleEncryptMode _encryptMode = EBundleEncryptMode.None;
        private int _encryptOffset = 32;
        private int _encryptXorKey = 0x5A;
        private string _buildInfo = string.Empty;

        [MenuItem("ZeonAsset/构建", priority = 110)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetBuildWindow>("构建");
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

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Package Id", _settings.PackageName);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", _buildTarget);
            _settings.OutputFolder = EditorGUILayout.TextField("Output Folder", _settings.OutputFolder);
            _useSbp = EditorGUILayout.ToggleLeft("优先使用 SBP", _useSbp);

            // 清理输出 与 增量构建互斥
            var clear = EditorGUILayout.ToggleLeft("构建前清理输出目录", _clearOutputFolder && !_incrementalBuild);
            var incremental = EditorGUILayout.ToggleLeft(
                "增量构建（UseCache，不 ForceRebuild）",
                _incrementalBuild && !_clearOutputFolder);

            if (clear && !_clearOutputFolder)
            {
                _clearOutputFolder = true;
                _incrementalBuild = false;
            }
            else if (incremental && !_incrementalBuild)
            {
                _incrementalBuild = true;
                _clearOutputFolder = false;
            }
            else
            {
                _clearOutputFolder = clear;
                _incrementalBuild = incremental;
            }

            if (_clearOutputFolder)
                EditorGUILayout.HelpBox("全量构建：先清空输出目录再打包。与增量互斥。", MessageType.None);
            else if (_incrementalBuild)
                EditorGUILayout.HelpBox("增量构建：保留输出目录，启用构建缓存。与清理互斥。", MessageType.Info);

            _encryptMode = (EBundleEncryptMode)EditorGUILayout.EnumPopup("Encrypt Mode", _encryptMode);
            if (_encryptMode == EBundleEncryptMode.Offset)
                _encryptOffset = EditorGUILayout.IntField("Encrypt Offset", _encryptOffset);
            if (_encryptMode == EBundleEncryptMode.Xor)
                _encryptXorKey = EditorGUILayout.IntSlider("XOR Key", _encryptXorKey, 1, 255);

            var outPath = ZeonAssetSettingsUtility.GetBuildOutputPath(_settings, _buildTarget);
            EditorGUILayout.HelpBox(
                "输出: " + outPath +
                "\nCDN 清单: manifest_{yyyyMMdd_HHmm}_{hash}.bytes" +
                "\n沙盒: manifest_active.bytes | 首包: manifest_builtin.bytes\n构建不会发布 CDN。",
                MessageType.None);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.45f);
            if (GUILayout.Button("构建 AssetBundles", GUILayout.Height(34)))
            {
                _buildInfo = ZeonAssetEditorActions.ExecuteBuild(
                    _settings,
                    _buildTarget,
                    _clearOutputFolder,
                    _useSbp,
                    _incrementalBuild,
                    _encryptMode,
                    _encryptOffset,
                    (byte)_encryptXorKey);
                EditorUtility.DisplayDialog("Resource", _buildInfo, "OK");
            }
            GUI.backgroundColor = Color.white;

            if (!string.IsNullOrEmpty(_buildInfo))
                EditorGUILayout.HelpBox(_buildInfo, MessageType.None);
        }
    }
}
