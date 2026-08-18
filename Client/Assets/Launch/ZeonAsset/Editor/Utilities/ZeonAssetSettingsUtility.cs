using System.IO;
using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>编辑器共用：Settings 加载与路径工具。</summary>
    public static class ZeonAssetSettingsUtility
    {
        public const string DefaultSettingsPath = ZeonAssetPathLayout.ModuleRoot + "/Settings/AssetCollectorSettings.asset";

        public static AssetCollectorSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AssetCollectorSettings>(DefaultSettingsPath);
            if (settings != null)
                return settings;

            var dir = Path.GetDirectoryName(DefaultSettingsPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            settings = AssetCollectorSettings.CreateDefault();
            AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ZeonAsset] 已创建默认 Settings: {DefaultSettingsPath}");
            return settings;
        }

        public static string GetBuildOutputPath(AssetCollectorSettings settings, BuildTarget target)
        {
            var folder = settings != null ? settings.OutputFolder : "Bundles";
            return ZeonAssetPathLayout.GetEditorBuildOutputRoot(folder, target.ToString());
        }

        public static string DrawFolderField(string label, string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var abs = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, string.Empty);
                    if (!string.IsNullOrEmpty(abs))
                    {
                        abs = abs.Replace('\\', '/');
                        var dataPath = Application.dataPath.Replace('\\', '/');
                        if (abs.StartsWith(dataPath))
                            path = "Assets" + abs.Substring(dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("Resource", "请选择工程 Assets 目录下的文件夹。", "OK");
                    }
                }
            }

            return path;
        }
    }
}
