using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Launch;

namespace Launch.Editor
{
    /// <summary>
    /// boot_config：从 local_dev.json 的 check_url 生成；菜单 / 出包共用。
    /// EditorSimulate 不依赖此文件；HostPlay / 安装包需要。
    /// </summary>
    public static class BootConfigLocalDevStamp
    {
        private const string AssetPath = "Assets/StreamingAssets/Launch/boot_config.json";

        public static string BootAbsPath
        {
            get
            {
                return Path.GetFullPath(
                    Path.Combine(Application.dataPath, "StreamingAssets/Launch/boot_config.json"));
            }
        }

        [MenuItem("Launch/生成启动配置", false, 50)]
        public static void GenerateBootConfig()
        {
            string url;
            string error;
            if (!TryCopyFromLocalDev(out url, out error))
            {
                EditorUtility.DisplayDialog("生成启动配置", error, "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "生成启动配置",
                "已从 Tools/local_dev.json 的 check_url 写入：\n" + url,
                "OK");
            PingBootConfig();
        }

        [MenuItem("Launch/打开启动配置", false, 51)]
        public static void OpenBootConfig()
        {
            var abs = BootAbsPath;
            if (!File.Exists(abs))
            {
                if (EditorUtility.DisplayDialog(
                    "启动配置",
                    "还没有 boot_config.json。现在从 local_dev.json 生成？",
                    "生成",
                    "取消"))
                {
                    GenerateBootConfig();
                }

                return;
            }

            PingBootConfig();
            EditorUtility.RevealInFinder(abs);
            BootConfig.ReloadFromDiskForEditor();
        }

        public static bool TryCopyFromLocalDev(out string url, out string error)
        {
            url = "";
            error = "";
            var checkUrl = ReadCheckUrl();
            if (string.IsNullOrWhiteSpace(checkUrl))
            {
                error = "Tools/local_dev.json 没有 check_url。请先跑 Tools/init-初始化.bat。";
                return false;
            }

            if (!IsUsableBootUrl(checkUrl))
            {
                error = "check_url 无效（不能是空或 127.0.0.1）：\n" + checkUrl;
                return false;
            }

            return TryWriteUrl(checkUrl.Trim(), out url, out error);
        }

        public static bool HasUsableBootConfig()
        {
            return TryReadBootUrl(out var url) && IsUsableBootUrl(url);
        }

        public static bool TryReadBootUrl(out string url)
        {
            url = "";
            try
            {
                var path = BootAbsPath;
                if (!File.Exists(path))
                    return false;
                var raw = File.ReadAllText(path);
                url = ReadJsonString(raw, "version_check_url");
                return !string.IsNullOrWhiteSpace(url);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryWriteUrl(string checkUrl, out string url, out string error)
        {
            url = "";
            error = "";
            checkUrl = (checkUrl ?? "").Trim();
            if (!IsUsableBootUrl(checkUrl))
            {
                error = "check_url 无效，不能写入 boot_config。";
                return false;
            }

            try
            {
                var bootAbs = BootAbsPath;
                var dir = Path.GetDirectoryName(bootAbs);
                if (string.IsNullOrEmpty(dir))
                {
                    error = "找不到 boot_config 目录";
                    return false;
                }

                Directory.CreateDirectory(dir);
                url = checkUrl;
                var json =
                    "{\n" +
                    "  \"version_check_url\": \"" + url + "\",\n" +
                    "  \"backup_version_check_url\": \"\"\n" +
                    "}\n";
                File.WriteAllText(bootAbs, json);
                AssetDatabase.Refresh();
                BootConfig.ReloadFromDiskForEditor();
                Debug.Log("[BootConfig] wrote " + url);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static bool IsUsableBootUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0)
                return false;
            if (url.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0)
                return false;
            if (url.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (url.IndexOf("zeon.local", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static string ReadCheckUrl()
        {
            try
            {
                var repo = GetRepoRoot();
                var devPath = Path.Combine(repo, "Tools", "local_dev.json");
                if (!File.Exists(devPath))
                    return "";
                var raw = File.ReadAllText(devPath);
                var check = ReadJsonString(raw, "check_url");
                if (!string.IsNullOrWhiteSpace(check))
                    return check.Trim();

                var host = ReadJsonString(raw, "lan_host");
                var port = ReadJsonInt(raw, "version_check_port", 8080);
                if (!string.IsNullOrWhiteSpace(host))
                    return "http://" + host.Trim() + ":" + port + "/version-check";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BootConfig] read local_dev.json: " + e.Message);
            }

            return "";
        }

        private static void PingBootConfig()
        {
            var obj = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (obj == null)
                return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private static string GetRepoRoot()
        {
            var client = Directory.GetParent(Application.dataPath)?.FullName;
            var repo = Directory.GetParent(client ?? "")?.FullName;
            if (string.IsNullOrEmpty(repo))
                throw new Exception("找不到仓库根目录。");
            return repo;
        }

        private static int ReadJsonInt(string json, string key, int fallback)
        {
            var s = ReadJsonString(json, key);
            int n;
            return int.TryParse(s, out n) && n > 0 ? n : fallback;
        }

        private static string ReadJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return "";
            var needle = "\"" + key + "\"";
            var i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0)
                return "";
            var colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0)
                return "";
            var p = colon + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p]))
                p++;
            if (p >= json.Length)
                return "";
            if (json[p] == '"')
            {
                var end = json.IndexOf('"', p + 1);
                return end > p ? json.Substring(p + 1, end - p - 1) : "";
            }

            var e = p;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-'))
                e++;
            return json.Substring(p, e - p);
        }
    }

    /// <summary>出包前检查 boot；scripts-only 跳过。</summary>
    public sealed class BootConfigBuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorUserBuildSettings.buildScriptsOnly)
                return;

            if (BootConfigLocalDevStamp.HasUsableBootConfig())
                return;

            throw new BuildFailedException(
                "没有启动配置。请先跑 Tools/init-初始化.bat（会写入 boot_config），或菜单 Launch / 生成启动配置。");
        }
    }
}
