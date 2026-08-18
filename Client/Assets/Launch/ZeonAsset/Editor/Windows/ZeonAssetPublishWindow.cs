using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>发布模拟 CDN（只增不删）。</summary>
    public class ZeonAssetPublishWindow : EditorWindow
    {
        private AssetCollectorSettings _settings;
        private BuildTarget _buildTarget;
        private string _publishInfo = string.Empty;

        [MenuItem("ZeonAsset/发布 CDN", priority = 115)]
        public static void Open()
        {
            var w = GetWindow<ZeonAssetPublishWindow>("发布 CDN");
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
            EditorGUILayout.LabelField("Package", _settings.PackageName);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", _buildTarget);
            _settings.CdnFolder = EditorGUILayout.TextField("CDN Folder", _settings.CdnFolder);

            var outputPath = ZeonAssetSettingsUtility.GetBuildOutputPath(_settings, _buildTarget);
            var cdnRoot = TaskPublishCdn.GetCdnRoot(
                _settings.CdnFolder, _buildTarget.ToString());
            EditorGUILayout.HelpBox($"源: {outputPath}\n目标: {cdnRoot}\n只增不删。", MessageType.None);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            if (GUILayout.Button("发布到模拟 CDN", GUILayout.Height(34)))
            {
                _publishInfo = ZeonAssetEditorActions.ExecutePublish(_settings, _buildTarget);
                EditorUtility.DisplayDialog("Resource", _publishInfo, "OK");
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("打开 CDN 目录"))
            {
                if (!Directory.Exists(cdnRoot))
                    Directory.CreateDirectory(cdnRoot);
                EditorUtility.RevealInFinder(cdnRoot);
            }

            if (!string.IsNullOrEmpty(_publishInfo))
                EditorGUILayout.HelpBox(_publishInfo, MessageType.None);
        }
    }
}
