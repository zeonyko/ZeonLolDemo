using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 把 persistentDataPath/zeon.log（或屏幕缓冲）POST 到调度 API 的 /device-log。
    /// 地址优先 VersionCheck.log_upload_url，其次从 dispatch / cdn_host 推算同宿主。
    /// </summary>
    public static class ZeonDeviceLogUploader
    {
        [Serializable]
        private class UploadBody
        {
            public string text;
            public string device;
            public string platform;
            public string app_version;
            public string source;
        }

        [Serializable]
        private class UploadResponse
        {
            public int code;
            public UploadResponseData data;
            public string message;
        }

        [Serializable]
        private class UploadResponseData
        {
            public string file;
            public string path;
            public int bytes;
        }

        public static string ResolveUploadUrl()
        {
            var vc = AssetManager.ActiveVersionCheck;
            if (vc != null && !string.IsNullOrWhiteSpace(vc.log_upload_url))
                return vc.log_upload_url.Trim();

            if (BootDispatchCache.TryReadLogUploadUrl(out var cached) && !string.IsNullOrWhiteSpace(cached))
                return cached.Trim();

            if (vc != null)
            {
                var fromDispatch = OriginToDeviceLog(vc.dispatch_url);
                if (!string.IsNullOrEmpty(fromDispatch))
                    return fromDispatch;
                var fromCdn = OriginToDeviceLog(vc.cdn_host);
                if (!string.IsNullOrEmpty(fromCdn))
                    return fromCdn;
            }

            var fromBoot = OriginToDeviceLog(BootDispatchCache.TryRead());
            return fromBoot ?? string.Empty;
        }

        public static string CollectLogText()
        {
            ZeonAssetScreenLog.Ensure();
            ZeonAssetScreenLog.Pump();
            try
            {
                var path = ZeonAssetScreenLog.FilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var fileText = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(fileText))
                        return fileText;
                }
            }
            catch
            {
                // fall through
            }

            return ZeonAssetScreenLog.Text ?? string.Empty;
        }

        public static IEnumerator UploadCoroutine(Action<bool, string> onDone)
        {
            var url = ResolveUploadUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                onDone?.Invoke(false, "无上报地址：请先完成 VersionCheck（调度 API 会下发 log_upload_url）");
                yield break;
            }

            var text = CollectLogText();
            if (string.IsNullOrWhiteSpace(text))
            {
                onDone?.Invoke(false, "日志为空");
                yield break;
            }

            var body = new UploadBody
            {
                text = text,
                device = SystemInfo.deviceModel,
                platform = Application.platform.ToString(),
                app_version = Application.version,
                source = "gm_panel",
            };
            var json = JsonUtility.ToJson(body);
            var bytes = Encoding.UTF8.GetBytes(json);

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                req.timeout = 30;
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                var ok = req.result == UnityWebRequest.Result.Success;
#else
                var ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok)
                {
                    onDone?.Invoke(false, "HTTP " + req.responseCode + " " + req.error);
                    yield break;
                }

                var respText = req.downloadHandler?.text ?? string.Empty;
                try
                {
                    var resp = JsonUtility.FromJson<UploadResponse>(respText);
                    if (resp != null && resp.code == 200 && resp.data != null && !string.IsNullOrEmpty(resp.data.file))
                    {
                        onDone?.Invoke(true, "已上传 " + resp.data.file + " (" + resp.data.bytes + "B)");
                        yield break;
                    }
                }
                catch
                {
                    // ignore parse, still treat 2xx as ok
                }

                onDone?.Invoke(true, "已上传 (" + bytes.Length + "B) → " + url);
            }
        }

        private static string OriginToDeviceLog(string anyUrl)
        {
            if (string.IsNullOrWhiteSpace(anyUrl))
                return null;
            var s = anyUrl.Trim();
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var uri = new Uri(s);
                var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port) { Path = "/device-log" };
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return null;
            }
        }
    }
}
