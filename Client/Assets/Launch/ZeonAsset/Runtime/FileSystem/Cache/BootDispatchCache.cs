using System;
using System.IO;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 上次 VersionCheck 写入沙盒的运行时引导：下次调度地址 + 游戏服。
    /// 冷启动调度优先读这里；进战斗连服也优先读这里。
    /// </summary>
    public static class BootDispatchCache
    {
        public const string FileName = "boot_dispatch.json";

        [Serializable]
        private class Payload
        {
            public string dispatch_url;
            public string game_server_host;
            public int game_server_port;
            public string log_upload_url;
        }

        public static string GetFilePath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                ZeonAssetPathLayout.RootFolderName,
                FileName).Replace('\\', '/');
        }

        public static string TryRead()
        {
            var payload = TryReadPayload();
            var url = payload?.dispatch_url?.Trim();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }

        public static bool TryReadGameServer(out string host, out int port)
        {
            host = null;
            port = 0;
            var payload = TryReadPayload();
            if (payload == null)
                return false;

            host = (payload.game_server_host ?? string.Empty).Trim();
            port = payload.game_server_port;
            if (string.IsNullOrWhiteSpace(host) || port <= 0)
            {
                host = null;
                port = 0;
                return false;
            }

            return true;
        }

        public static bool TryReadLogUploadUrl(out string url)
        {
            url = null;
            var payload = TryReadPayload();
            var value = payload?.log_upload_url?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return false;
            url = value;
            return true;
        }

        public static void Clear()
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return;
            try
            {
                var path = GetFilePath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"删除 {FileName} 失败: {e.Message}");
            }
        }

        /// <summary>写入调度地址；游戏服 / 日志上报地址可选，空则保留沙盒里已有值。</summary>
        public static void Save(
            string dispatchUrl,
            string gameServerHost = null,
            int gameServerPort = 0,
            string logUploadUrl = null)
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return;

            dispatchUrl = (dispatchUrl ?? string.Empty).Trim();
            gameServerHost = (gameServerHost ?? string.Empty).Trim();
            logUploadUrl = (logUploadUrl ?? string.Empty).Trim();

            var existing = TryReadPayload() ?? new Payload();
            if (!string.IsNullOrWhiteSpace(dispatchUrl))
                existing.dispatch_url = dispatchUrl;
            if (!string.IsNullOrWhiteSpace(gameServerHost))
                existing.game_server_host = gameServerHost;
            if (gameServerPort > 0)
                existing.game_server_port = gameServerPort;
            if (!string.IsNullOrWhiteSpace(logUploadUrl))
                existing.log_upload_url = logUploadUrl;

            if (string.IsNullOrWhiteSpace(existing.dispatch_url) &&
                string.IsNullOrWhiteSpace(existing.game_server_host) &&
                string.IsNullOrWhiteSpace(existing.log_upload_url))
                return;

            try
            {
                var path = GetFilePath();
                DiskCacheManager.EnsureDirectory(Path.GetDirectoryName(path));
                var json = JsonUtility.ToJson(existing, true);
                File.WriteAllText(path, json);
                ZeonAssetLog.Info(
                    $"引导缓存已写入 {path}  dispatch={existing.dispatch_url} " +
                    $"game={existing.game_server_host}:{existing.game_server_port} " +
                    $"log={existing.log_upload_url}");
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"写入 {FileName} 失败: {e.Message}");
            }
        }

        private static Payload TryReadPayload()
        {
            if (!ZeonAssetPathHelper.CanUsePersistentFileCache())
                return null;

            var path = GetFilePath();
            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<Payload>(json);
            }
            catch (Exception e)
            {
                ZeonAssetLog.Warn($"读取 {FileName} 失败: {e.Message}");
                return null;
            }
        }
    }
}
