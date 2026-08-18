using UnityEditor;
using UnityEngine;
using Game.ZeonAsset;
using Launch;

namespace Launch.Editor
{
    [CustomEditor(typeof(LaunchBoot))]
    public class LaunchBootEditor : UnityEditor.Editor
    {
        private SerializedProperty _playMode;
        private SerializedProperty _downloadPolicy;

        private void OnEnable()
        {
            _playMode = serializedObject.FindProperty("PlayMode");
            _downloadPolicy = serializedObject.FindProperty("DownloadPolicy");
            BootConfig.ReloadFromDiskForEditor();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_playMode);

            var playMode = (EPlayMode)_playMode.intValue;
            if (playMode == EPlayMode.EditorSimulate)
            {
                EditorGUILayout.HelpBox(
                    "EditorSimulate：AssetDatabase，不请求 VersionCheck / CDN，不读 boot_config。\n" +
                    "连服：Tools/local_dev.json中的server_url。只需 start_server。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(_downloadPolicy);
                EditorGUILayout.HelpBox(
                    "HostPlay：与真机同一条链路——boot_config → 远程 VersionCheck → CDN / 游戏服。\n" +
                    "请开 Tools/local_dev_server（或正式调度）。\n" +
                    "当前 boot：\n" + BootConfig.VersionCheckUrl + "\n" +
                    "安装包固定 HostPlay + OnDemand，不读本页 PlayMode / DownloadPolicy。",
                    MessageType.Info);

                var target = EditorUserBuildSettings.activeBuildTarget;
                if (target == BuildTarget.Android || target == BuildTarget.iOS)
                {
                    EditorGUILayout.HelpBox(
                        "Active Build Target 是 " + target + "：HostPlay 会加载该平台 AB。\n" +
                        "Windows 编辑器（DX11）里地图常会粉紫。看画面用 EditorSimulate；" +
                        "测热更请切 StandaloneWindows64 再 Build/Publish，或真机验证。",
                        MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
