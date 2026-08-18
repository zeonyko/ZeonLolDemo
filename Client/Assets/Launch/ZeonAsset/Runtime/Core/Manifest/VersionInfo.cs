using System;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// VersionCheck 请求：报送本地 ActiveManifest Header.ManifestHash。
    /// </summary>
    [Serializable]
    public class VersionCheckRequest
    {
        public string app_version = "1.0.0";
        public string local_manifest_hash = string.Empty;
        public string platform = "windows";
        public string channel = "official";
        public string uid = string.Empty;
        public string device_id = string.Empty;
    }

    [Serializable]
    public class VersionCheckNotice
    {
        public string title = string.Empty;
        public string content = string.Empty;
    }

    /// <summary>
    /// VersionCheck data：无更新时仅 has_update=false（0 CDN 流量）；
    /// 有更新时下发 manifest_url / hash。
    /// </summary>
    [Serializable]
    public class VersionCheckData
    {
        public bool has_update;

        /// <summary>normal / maintenance</summary>
        public string status = "normal";

        public string manifest_name = string.Empty;
        public string manifest_hash = string.Empty;
        public long manifest_size;
        public string manifest_url = string.Empty;

        /// <summary>权威 CDN 根（Bundles 下载用）。空则本次无 CDN。</summary>
        public string cdn_host = string.Empty;

        /// <summary>可选：下次冷启动优先使用的调度地址，写入沙盒 boot_dispatch.json。</summary>
        public string dispatch_url = string.Empty;

        /// <summary>权威游戏服 Host（TCP）。空则客户端只能靠沙盒已有缓存；无缓存则进战斗失败。</summary>
        public string game_server_host = string.Empty;

        /// <summary>权威游戏服端口。≤0 表示未下发。</summary>
        public int game_server_port;

        /// <summary>开发用：上传真机日志的 API（与 VersionCheck 同宿主）。空则客户端可从 dispatch/cdn 推 origin。</summary>
        public string log_upload_url = string.Empty;

        public bool force_update;
        public string store_url = string.Empty;
        public VersionCheckNotice notice = new VersionCheckNotice();

        public bool IsMaintenance =>
            string.Equals(status, "maintenance", StringComparison.OrdinalIgnoreCase);

        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);

        public static VersionCheckData CreateNoUpdate()
        {
            return new VersionCheckData
            {
                has_update = false,
                status = "normal",
                notice = new VersionCheckNotice
                {
                    title = "已是最新",
                    content = "has_update=false，无需拉取 CDN Manifest。",
                },
            };
        }

        public static VersionCheckData CreateUpdate(
            string manifestName,
            string manifestHash,
            string manifestUrl = null,
            string cdnHost = null)
        {
            return new VersionCheckData
            {
                has_update = true,
                status = "normal",
                manifest_name = (manifestName ?? string.Empty).Trim(),
                manifest_hash = manifestHash ?? string.Empty,
                manifest_url = manifestUrl ?? string.Empty,
                cdn_host = cdnHost ?? string.Empty,
                notice = new VersionCheckNotice
                {
                    title = "发现更新",
                    content = "has_update=true，将下载新 Manifest 并 Diff Bundle。",
                },
            };
        }
    }

    [Serializable]
    public class VersionCheckResponse
    {
        public int code = 200;
        public VersionCheckData data = new VersionCheckData();

        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);

        public static VersionCheckResponse FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("VersionCheck response is null or empty.", nameof(json));

            json = StripBom(json).Trim();
            var resp = JsonUtility.FromJson<VersionCheckResponse>(json);
            if (resp == null)
                throw new InvalidOperationException("Failed to parse VersionCheckResponse.");
            if (resp.data == null)
                resp.data = new VersionCheckData();
            if (resp.data.notice == null)
                resp.data.notice = new VersionCheckNotice();
            return resp;
        }

        /// <summary>CDN 本地模拟 version_check.json（精简字段）。</summary>
        public static string ToCdnMockJson(
            bool hasUpdate,
            string manifestName,
            string manifestHash,
            string cdnHost = null,
            bool prettyPrint = true,
            string gameServerHost = null,
            int gameServerPort = 0)
        {
            var lean = new CdnVersionCheckFile
            {
                code = 200,
                data = new CdnVersionCheckData
                {
                    has_update = hasUpdate,
                    manifest_name = manifestName ?? string.Empty,
                    manifest_hash = manifestHash ?? string.Empty,
                    cdn_host = cdnHost ?? string.Empty,
                    game_server_host = gameServerHost ?? string.Empty,
                    game_server_port = gameServerPort,
                },
            };
            return JsonUtility.ToJson(lean, prettyPrint);
        }

        /// <summary>
        /// 静态 json（CDN version_check.json / file://）用 GET；其余调度地址用 POST。
        /// </summary>
        public static bool PreferHttpGet(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            var trimmed = url.Trim();
            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed.IndexOf("version_check.json", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripBom(string text)
        {
            if (!string.IsNullOrEmpty(text) && text[0] == '\uFEFF')
                return text.Substring(1);
            return text;
        }
    }

    /// <summary>CDN 根目录 version_check.json 的精简 data。</summary>
    [Serializable]
    public class CdnVersionCheckData
    {
        public bool has_update;
        public string manifest_name = string.Empty;
        public string manifest_hash = string.Empty;
        public string cdn_host = string.Empty;
        public string game_server_host = string.Empty;
        public int game_server_port;
        public string log_upload_url = string.Empty;
    }

    /// <summary>CDN 根目录 version_check.json。</summary>
    [Serializable]
    public class CdnVersionCheckFile
    {
        public int code = 200;
        public CdnVersionCheckData data = new CdnVersionCheckData();
    }
}
