using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>各 Resource 编辑器窗口共用 GUI。</summary>
    public static class ZeonAssetEditorGuiUtility
    {
        public static BuildTarget GetActiveBuildTarget()
        {
            return EditorUserBuildSettings.activeBuildTarget;
        }

        public static void ApplyWindowSize(EditorWindow window)
        {
            EnsureEditorAntiAliasing();
            window.minSize = new Vector2(480, 360);
            window.maxSize = new Vector2(8192, 8192);
        }

        /// <summary>
        /// QualitySettings.antiAliasing==0 时 ObjectField 预览会刷屏报错。
        /// 仅在打开 Resource 窗口时调用，不在 InitializeOnLoad 全局改画质。
        /// </summary>
        public static void EnsureEditorAntiAliasing()
        {
            if (QualitySettings.antiAliasing >= 1)
                return;

            QualitySettings.antiAliasing = 2;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                if (QualitySettings.antiAliasing < 1)
                    QualitySettings.antiAliasing = 2;
            }

            QualitySettings.SetQualityLevel(QualitySettings.GetQualityLevel(), applyExpensiveChanges: false);
        }

        public static AssetCollectorSettings DrawSettingsHeader(ref AssetCollectorSettings settings)
        {
            EnsureEditorAntiAliasing();
            using (new EditorGUILayout.HorizontalScope())
            {
                settings = DrawScriptableObjectField("Settings", settings);
                if (GUILayout.Button("定位", GUILayout.Width(48)))
                {
                    settings = ZeonAssetSettingsUtility.LoadOrCreateSettings();
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }

            return settings;
        }

        public static T DrawScriptableObjectField<T>(string label, T value) where T : ScriptableObject
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                var path = value != null ? AssetDatabase.GetAssetPath(value) : "None";
                EditorGUILayout.SelectableLabel(
                    path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("选…", GUILayout.Width(36)))
                {
                    var start = string.IsNullOrEmpty(path) || path == "None"
                        ? "Assets"
                        : Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                    var picked = EditorUtility.OpenFilePanel("选择 " + typeof(T).Name, start, "asset");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        picked = picked.Replace('\\', '/');
                        var dataPath = Application.dataPath.Replace('\\', '/');
                        if (picked.StartsWith(dataPath))
                        {
                            var assetPath = "Assets" + picked.Substring(dataPath.Length);
                            value = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                        }
                    }
                }
            }

            var drop = GUILayoutUtility.GetLastRect();
            if (drop.Contains(Event.current.mousePosition) &&
                (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform))
            {
                var objs = DragAndDrop.objectReferences;
                for (int i = 0; i < objs.Length; i++)
                {
                    if (objs[i] is T typed)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                        if (Event.current.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            value = typed;
                        }

                        Event.current.Use();
                        break;
                    }
                }
            }

            return value;
        }
    }
}
