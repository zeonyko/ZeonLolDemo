using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Game.ZeonAsset;

namespace Launch
{
    /// <summary>
    /// 包内启动引导：只读 StreamingAssets/Launch/boot_config.json 的调度 URL。
    /// CDN / 游戏服一律 VersionCheck 下发，不写在这份文件里。
    /// </summary>
    public static class BootConfig
    {
        public const string RelativePath = "Launch/boot_config.json";

        [Serializable]
        public class FileData
        {
            public string version_check_url = string.Empty;
            public string backup_version_check_url = string.Empty;
        }

        private static FileData _data;
        private static Task _loadTask;

        public static string VersionCheckUrl => Data.version_check_url ?? string.Empty;
        public static string BackupVersionCheckUrl => Data.backup_version_check_url ?? string.Empty;

        public static bool IsLoaded => _data != null;

        private static FileData Data
        {
            get
            {
                if (_data == null)
                    TryLoadSync();
                return _data ?? new FileData();
            }
        }

        public static string GetAbsolutePath()
        {
            return Path.Combine(Application.streamingAssetsPath, RelativePath).Replace('\\', '/');
        }

        /// <summary>可 File 读的平台同步加载；Android jar 等会失败，需走 EnsureLoadedCoroutine / Async。</summary>
        public static bool TryLoadSync()
        {
            if (_data != null)
                return true;

            var path = GetAbsolutePath();
            try
            {
                if (ZeonAssetPathHelper.CanProbeLocalFile(path) && File.Exists(path))
                {
                    ApplyJson(File.ReadAllText(path));
                    return _data != null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BootConfig] sync load failed: " + e.Message);
            }

            return false;
        }

        public static IEnumerator EnsureLoadedCoroutine()
        {
            if (_data != null)
                yield break;

            if (TryLoadSync())
                yield break;

            var url = ZeonAssetPathHelper.ToRequestUrl(GetAbsolutePath());
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[BootConfig] load failed: " + req.error + " url=" + url);
                    _data = new FileData();
                    yield break;
                }

                ApplyJson(req.downloadHandler?.text);
            }
        }

        public static Task EnsureLoadedAsync()
        {
            if (_data != null)
                return Task.CompletedTask;
            if (_loadTask != null)
                return _loadTask;

            _loadTask = EnsureLoadedAsyncCore();
            return _loadTask;
        }

        private static async Task EnsureLoadedAsyncCore()
        {
            if (TryLoadSync())
                return;

            var url = ZeonAssetPathHelper.ToRequestUrl(GetAbsolutePath());
            using (var req = UnityWebRequest.Get(url))
            {
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[BootConfig] load failed: " + req.error + " url=" + url);
                    _data = new FileData();
                    return;
                }

                ApplyJson(req.downloadHandler?.text);
            }
        }

        private static void ApplyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                _data = new FileData();
                Debug.LogWarning("[BootConfig] empty " + RelativePath);
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(json) && json[0] == '\uFEFF')
                    json = json.Substring(1);
                var parsed = JsonUtility.FromJson<FileData>(json);
                _data = parsed ?? new FileData();
                Debug.Log("[BootConfig] loaded vc=" + _data.version_check_url);
            }
            catch (Exception e)
            {
                _data = new FileData();
                Debug.LogError("[BootConfig] parse failed: " + e.Message);
            }
        }

#if UNITY_EDITOR
        public static void ReloadFromDiskForEditor()
        {
            _data = null;
            _loadTask = null;
            TryLoadSync();
        }
#endif
    }
}
