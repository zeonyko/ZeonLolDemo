using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 清单更新：读本地 ActiveManifest Header → VersionCheck(has_update) →
    /// 有更新则下载到 Cache/tmp/target.bytes → Diff Bundle。
    /// </summary>
    public class UpdateManifestOperation : PooledOperation<UpdateManifestOperation>
    {
        private enum EStep
        {
            LoadBuiltin,
            VersionCheck,
            DownloadManifest,
            Done,
        }

        private ZeonAssetConfig _config;
        private string _packageName;
        private string _sandboxRoot;
        private string _streamingRoot;
        private EStep _step;
        private UnityWebRequest _request;
        private string _pendingManifestUrl;
        private string _tempManifestPath;
        private PackageManifest _builtinManifest;
        private string[] _versionCheckUrls;
        private int _versionCheckUrlIndex;

        protected override string DiagnosticStep => _step.ToString();

        public VersionCheckData TargetVersion { get; private set; }
        public PackageManifest LocalManifest { get; private set; }
        public PackageManifest RemoteManifest { get; private set; }
        public List<PackageBundle> DirtyBundles { get; private set; } = new List<PackageBundle>();
        public string SandboxRoot => _sandboxRoot;
        public bool SkippedBecauseUpToDate { get; private set; }
        public string LocalManifestHash { get; private set; }
        public string TempManifestPath => _tempManifestPath;

        public static UpdateManifestOperation Create(ZeonAssetConfig config, string packageName)
        {
            var op = Rent();
            op._config = config ?? ZeonAssetConfig.CreateDefault();
            op._packageName = string.IsNullOrEmpty(packageName) ? op._config.DefaultPackageName : packageName;
            op._sandboxRoot = DiskCacheManager.GetSandboxRoot();
            op._streamingRoot = DiskCacheManager.GetStreamingRoot();
            op.DirtyBundles = new List<PackageBundle>();
            op._step = EStep.DownloadManifest;
            op.SkippedBecauseUpToDate = false;
            op.LocalManifestHash = null;
            op._tempManifestPath = DiskCacheManager.GetTempManifestPath(op._sandboxRoot);
            op._builtinManifest = null;
            return op;
        }

        protected override void InternalOnStart()
        {
            DiskCacheManager.EnsureSandboxDirectories(_sandboxRoot);

            // 步骤1：manifest_active（AppVersion 校验）→ 首包 manifest_builtin
            var appVersion = _config.ResolveAppVersion();
            if (DiskCacheManager.TryLoadActiveManifest(
                    appVersion, out var localManifest, out var sourcePath, out _))
            {
                LocalManifest = localManifest;
                LocalManifestHash = localManifest.ManifestHash;
                ZeonAssetLog.Info(
                    $"Local ActiveManifest hash={LocalManifestHash}, " +
                    $"AppVersion={localManifest.AppVersion}, source={sourcePath}");

                if (!string.IsNullOrEmpty(sourcePath) &&
                    (ZeonAssetPathHelper.IsStreamingAssetsLike(sourcePath) ||
                     string.Equals(
                         sourcePath,
                         DiskCacheManager.GetBuiltinManifestPath(),
                         StringComparison.OrdinalIgnoreCase)))
                {
                    _builtinManifest = localManifest;
                    AssetManager.SetBuiltinManifest(_builtinManifest);
                }
            }

            var builtinPath = DiskCacheManager.GetBuiltinManifestPath();
            if (_builtinManifest == null &&
                DiskCacheManager.TryReadManifestFile(builtinPath, out var probeableBuiltin))
            {
                _builtinManifest = probeableBuiltin;
                AssetManager.SetBuiltinManifest(_builtinManifest);
            }

            if (_builtinManifest == null && !string.IsNullOrEmpty(builtinPath))
            {
                _step = EStep.LoadBuiltin;
                _pendingManifestUrl = ZeonAssetPathHelper.ToRequestUrl(builtinPath);
                ZeonAssetLog.Info($"UWR 读取首包 Manifest → {_pendingManifestUrl}");
                _request = UnityWebRequest.Get(_pendingManifestUrl);
                _request.timeout = Mathf.Max(1, (int)_config.TimeoutSeconds);
                _request.SendWebRequest();
                return;
            }

            BeginVersionCheck();
        }

        private void ApplyCdnHostOverride()
        {
            if (TargetVersion == null)
                return;

            if (string.IsNullOrWhiteSpace(TargetVersion.cdn_host))
            {
                // Publish 产物里 cdn_host 常为空；若 VersionCheck 本身是 file://.../version_check.json，
                // 则把 CDN 根推成同目录，本机 PC 可完全不依赖 HTTP。
                var vc = CurrentVersionCheckUrl();
                if (!string.IsNullOrEmpty(vc) &&
                    vc.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                    vc.IndexOf("version_check.json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var trimmed = vc.Trim();
                    var cut = trimmed.LastIndexOf('/');
                    if (cut > "file://".Length)
                        TargetVersion.cdn_host = trimmed.Substring(0, cut);
                }
            }

            if (string.IsNullOrWhiteSpace(TargetVersion.cdn_host))
                return;

            _config.RemoteUrl = TargetVersion.cdn_host.Trim().TrimEnd('/');
            ZeonAssetLog.Info($"VersionCheck cdn_host → {_config.RemoteUrl}");
        }

        private void FinishUpToDate(PackageManifest manifest, bool skipNetwork)
        {
            RemoteManifest = manifest;
            DirtyBundles = DiskCacheManager.CollectDirtyBundles(
                manifest, _sandboxRoot, _streamingRoot, _builtinManifest)
                ?? new List<PackageBundle>();

            if (DirtyBundles.Count > 0)
            {
                if (string.IsNullOrEmpty(_config.RemoteUrl))
                {
                    Fail(
                        "本地缺 Bundle 但 cdn_host/RemoteUrl 为空，无法补齐。\n" +
                        "请确认 VersionCheck 返回了 cdn_host（本机开 Tools/local_dev_server）。");
                    return;
                }

                if (_config.IsOnDemandDownload)
                {
                    SkippedBecauseUpToDate = true;
                    ZeonAssetLog.Info(
                        $"OnDemand: Manifest 无更新，{DirtyBundles.Count} 个缺包延后按需下载。");
                    Progress = 1f;
                    _step = EStep.Done;
                    Succeed();
                    return;
                }

                // 无 Manifest 更新但仍缺 Bundle：只补 Bundle，提交时若无 tmp Manifest 则跳过覆盖
                SkippedBecauseUpToDate = false;
                ZeonAssetLog.Info(
                    $"Manifest 无更新，但仍有 {DirtyBundles.Count} 个 Bundle 需补齐");
                Progress = 1f;
                _step = EStep.Done;
                Succeed();
                return;
            }

            SkippedBecauseUpToDate = skipNetwork;
            Progress = 1f;
            _step = EStep.Done;
            ZeonAssetLog.Info(
                $"has_update=false / 本地已齐，跳过热更。hash={manifest?.ManifestHash}");
            Succeed();
        }

        protected override void InternalOnUpdate()
        {
            if (_request == null)
                return;

            if (_step == EStep.LoadBuiltin)
            {
                Progress = _request.downloadProgress * 0.15f;
                if (!_request.isDone)
                    return;

                if (!HasRequestError(_request))
                {
                    try
                    {
                        var data = _request.downloadHandler.data;
                        if (data != null && data.Length > 0)
                        {
                            _builtinManifest = PackageManifest.FromJsonBytes(data);
                            AssetManager.SetBuiltinManifest(_builtinManifest);
                            if (LocalManifest == null)
                            {
                                LocalManifest = _builtinManifest;
                                LocalManifestHash = LocalManifest?.ManifestHash;
                                ZeonAssetLog.Info($"UWR 首包 Manifest hash={LocalManifestHash}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ZeonAssetLog.Warn($"UWR 首包 Manifest 解析失败: {e.Message}");
                    }
                }
                else
                {
                    ZeonAssetLog.Warn(
                        $"UWR 首包 Manifest 失败: {_pendingManifestUrl}, {_request.error}");
                }

                DisposeRequest();
                BeginVersionCheck();
                return;
            }

            if (_step == EStep.VersionCheck)
            {
                Progress = 0.15f + _request.downloadProgress * 0.1f;
                if (!_request.isDone)
                    return;

                var url = CurrentVersionCheckUrl();
                if (HasRequestError(_request))
                {
                    var err = _request.error;
                    var code = _request.responseCode;
                    var body = _request.downloadHandler != null
                        ? _request.downloadHandler.text
                        : null;
                    DisposeRequest();
                    var detail = $"HTTP {code}: {err}";
                    if (!string.IsNullOrWhiteSpace(body))
                        detail += "\n" + body;
                    if (TryBeginNextVersionCheck($"HTTP failed: {url}, {detail}"))
                        return;
                    Fail($"VersionCheck HTTP failed: {url}, {detail}");
                    return;
                }

                try
                {
                    var json = _request.downloadHandler.text;
                    DisposeRequest();
                    var resp = VersionCheckResponse.FromJson(json);
                    if (resp == null || resp.code != 200 || resp.data == null)
                    {
                        if (TryBeginNextVersionCheck($"invalid response: {url}, code={resp?.code}"))
                            return;
                        Fail($"VersionCheck invalid response: code={resp?.code}");
                        return;
                    }

                    ContinueAfterVersionCheck(resp.data);
                }
                catch (Exception e)
                {
                    DisposeRequest();
                    if (TryBeginNextVersionCheck($"parse failed: {url}, {e.Message}"))
                        return;
                    Fail("VersionCheck parse failed: " + e.Message);
                }

                return;
            }

            if (_step == EStep.DownloadManifest)
            {
                Progress = 0.25f + _request.downloadProgress * 0.5f;
                if (!_request.isDone)
                    return;

                if (HasRequestError(_request))
                {
                    Fail($"Download remote manifest failed: {_pendingManifestUrl}, {_request.error}");
                    DisposeRequest();
                    return;
                }

                try
                {
                    var data = _request.downloadHandler.data;
                    if (data == null || data.Length == 0)
                    {
                        Fail("Remote manifest payload is empty.");
                        return;
                    }

                    RemoteManifest = PackageManifest.FromJsonBytes(data);
                    if (!string.IsNullOrEmpty(TargetVersion.manifest_name))
                        RemoteManifest.ManifestFileName = TargetVersion.manifest_name;

                    // 优先用 Header.ManifestHash 与 API 对齐
                    if (!ValidateManifestMeta(data, RemoteManifest))
                    {
                        Fail("Remote manifest hash mismatch against VersionCheck data.");
                        return;
                    }

                    // 落盘到 Cache/tmp/target.bytes，等 Bundle 齐后再原子提交（不可持久化平台跳过）
                    if (ZeonAssetPathHelper.CanUsePersistentFileCache())
                    {
                        try
                        {
                            DiskCacheManager.EnsureDirectory(DiskCacheManager.GetTempDir(_sandboxRoot));
                            File.WriteAllBytes(_tempManifestPath, data);
                        }
                        catch (Exception e)
                        {
                            ZeonAssetLog.Warn($"Write temp manifest skipped: {e.Message}");
                        }
                    }

                    DirtyBundles = DiskCacheManager.CollectDirtyBundles(
                        RemoteManifest, _sandboxRoot, _streamingRoot, _builtinManifest);

                    Progress = 1f;
                    _step = EStep.Done;
                    Succeed();
                }
                catch (Exception e)
                {
                    Fail(e.Message);
                }
                finally
                {
                    DisposeRequest();
                }
            }
        }

        private void BeginVersionCheck()
        {
            _versionCheckUrls = _config.CollectVersionCheckUrls();
            if (_versionCheckUrls == null || _versionCheckUrls.Length == 0)
            {
                Fail("VersionCheckUrl 为空。请配置 StreamingAssets/Launch/boot_config.json，并启动调度服务。");
                return;
            }

            _versionCheckUrlIndex = 0;
            SendVersionCheckRequest(_versionCheckUrls[0]);
        }

        private string CurrentVersionCheckUrl()
        {
            if (_versionCheckUrls == null ||
                _versionCheckUrlIndex < 0 ||
                _versionCheckUrlIndex >= _versionCheckUrls.Length)
                return _config?.VersionCheckUrl;
            return _versionCheckUrls[_versionCheckUrlIndex];
        }

        private bool TryBeginNextVersionCheck(string reason)
        {
            if (_versionCheckUrls == null || _versionCheckUrlIndex + 1 >= _versionCheckUrls.Length)
                return false;

            ZeonAssetLog.Warn($"VersionCheck 失败，改试备用地址。{reason}");
            _versionCheckUrlIndex++;
            SendVersionCheckRequest(_versionCheckUrls[_versionCheckUrlIndex]);
            return true;
        }

        private void SendVersionCheckRequest(string url)
        {
            _step = EStep.VersionCheck;
            if (VersionCheckResponse.PreferHttpGet(url))
            {
                var platform = _config.ResolvePlatform();
                var getUrl = AppendQuery(url, "platform", platform);
                if (!string.IsNullOrEmpty(LocalManifestHash))
                    getUrl = AppendQuery(getUrl, "local_manifest_hash", LocalManifestHash);
                getUrl = AppendQuery(getUrl, "_", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

                _request = UnityWebRequest.Get(getUrl);
                _request.timeout = Mathf.Max(1, (int)_config.TimeoutSeconds);
                _request.SetRequestHeader("Cache-Control", "no-cache, no-store");
                _request.SetRequestHeader("Pragma", "no-cache");
                _request.SendWebRequest();
                ZeonAssetLog.Info($"VersionCheck GET {getUrl} hash={LocalManifestHash}");
                return;
            }

            var reqBody = new VersionCheckRequest
            {
                app_version = _config.ResolveAppVersion(),
                local_manifest_hash = LocalManifestHash ?? string.Empty,
                channel = _config.Channel,
                platform = _config.ResolvePlatform(),
            };
            var json = JsonUtility.ToJson(reqBody);
            var bytes = Encoding.UTF8.GetBytes(json);
            _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            _request.uploadHandler = new UploadHandlerRaw(bytes);
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.SetRequestHeader("Content-Type", "application/json");
            _request.SetRequestHeader("Cache-Control", "no-cache, no-store");
            _request.SetRequestHeader("Pragma", "no-cache");
            _request.timeout = Mathf.Max(1, (int)_config.TimeoutSeconds);
            _request.SendWebRequest();
            ZeonAssetLog.Info(
                $"VersionCheck POST {url} platform={reqBody.platform} hash={reqBody.local_manifest_hash}");
        }

        private static string AppendQuery(string url, string key, string value)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || value == null)
                return url;
            var sep = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + sep + key + "=" + UnityWebRequest.EscapeURL(value);
        }

        private void ContinueAfterVersionCheck(VersionCheckData versionData)
        {
            TargetVersion = versionData ?? VersionCheckData.CreateNoUpdate();
            if (TargetVersion.notice == null)
                TargetVersion.notice = new VersionCheckNotice();

            BootDispatchCache.Save(
                TargetVersion.dispatch_url,
                TargetVersion.game_server_host,
                TargetVersion.game_server_port,
                TargetVersion.log_upload_url);

            if (TargetVersion.IsMaintenance)
            {
                var title = string.IsNullOrEmpty(TargetVersion.notice?.title)
                    ? "maintenance"
                    : TargetVersion.notice.title;
                var content = string.IsNullOrEmpty(TargetVersion.notice?.content)
                    ? "status=maintenance"
                    : TargetVersion.notice.content;
                LoadingProgress.Fail(
                    ELoadingFailKind.Maintenance, title, content, null,
                    $"[VersionCheck] maintenance: {title} - {content}");
                Fail($"[VersionCheck] maintenance: {title} - {content}");
                return;
            }

            if (TargetVersion.force_update)
            {
                var title = string.IsNullOrEmpty(TargetVersion.notice?.title)
                    ? "force_update"
                    : TargetVersion.notice.title;
                var content = string.IsNullOrEmpty(TargetVersion.notice?.content)
                    ? "force_update=true，需要安装新包。"
                    : TargetVersion.notice.content;
                LoadingProgress.Fail(
                    ELoadingFailKind.ForceAppUpdate, title, content,
                    TargetVersion.store_url,
                    $"[VersionCheck] force_update=true, store_url={TargetVersion.store_url}");
                Fail(
                    $"[VersionCheck] force_update=true, store_url={TargetVersion.store_url}. " +
                    "请引导玩家前往商店更新大包。");
                return;
            }

            ApplyCdnHostOverride();

            if (!TargetVersion.has_update)
            {
                if (LocalManifest == null)
                {
                    Fail("VersionCheck.has_update=false 但本地无 ActiveManifest/首包，无法启动。");
                    return;
                }

                FinishUpToDate(LocalManifest, skipNetwork: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetVersion.manifest_url) &&
                string.IsNullOrWhiteSpace(TargetVersion.manifest_name))
            {
                Fail("[VersionCheck] has_update=true 但 manifest_url/manifest_name 均为空。");
                return;
            }

            if (string.IsNullOrEmpty(_config.RemoteUrl) &&
                string.IsNullOrWhiteSpace(TargetVersion.manifest_url))
            {
                Fail("RemoteUrl/cdn_host/manifest_url 为空，无法下载 Manifest。");
                return;
            }

            BeginDownloadManifest();
        }

        private void BeginDownloadManifest()
        {
            _step = EStep.DownloadManifest;

            if (!string.IsNullOrWhiteSpace(TargetVersion.manifest_url))
            {
                _pendingManifestUrl = TargetVersion.manifest_url.Trim();
            }
            else if (!string.IsNullOrEmpty(TargetVersion.manifest_name))
            {
                var name = TargetVersion.manifest_name;
                if (!name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                    name += ".bytes";

                _pendingManifestUrl = ZeonAssetPathHelper.CombineUrl(
                    _config.RemoteUrl,
                    DiskCacheManager.ManifestsFolder + "/" + name);
            }
            else
            {
                Fail("[VersionCheck] has_update=true 但 manifest_url/manifest_name 均为空。");
                return;
            }

            ZeonAssetLog.Info($"Download Manifest → {_pendingManifestUrl}");
            _request = UnityWebRequest.Get(_pendingManifestUrl);
            _request.timeout = Mathf.Max(1, (int)_config.TimeoutSeconds);
            _request.SendWebRequest();
        }

        private bool ValidateManifestMeta(byte[] data, PackageManifest parsed)
        {
            if (TargetVersion == null || data == null)
                return true;

            if (TargetVersion.manifest_size > 0 && data.LongLength != TargetVersion.manifest_size)
            {
                ZeonAssetLog.Warn(
                    $"Manifest size mismatch: expect={TargetVersion.manifest_size}, got={data.LongLength}.");
                return false;
            }

            if (!string.IsNullOrEmpty(TargetVersion.manifest_hash))
            {
                // 优先比对 Header.ManifestHash
                if (!string.IsNullOrEmpty(parsed?.ManifestHash) &&
                    HashUtility.EqualsIgnoreCase(parsed.ManifestHash, TargetVersion.manifest_hash))
                    return true;

                var fileHash = HashUtility.ComputeBytesMd5(data);
                if (!HashUtility.EqualsIgnoreCase(fileHash, TargetVersion.manifest_hash) &&
                    !(fileHash.Length >= 8 &&
                      TargetVersion.manifest_hash.Length >= 8 &&
                      HashUtility.EqualsIgnoreCase(
                          fileHash.Substring(0, 8), TargetVersion.manifest_hash.Substring(0, 8))))
                {
                    ZeonAssetLog.Warn(
                        $"Manifest hash mismatch: api={TargetVersion.manifest_hash}, " +
                        $"header={parsed?.ManifestHash}, file={fileHash}");
                    return false;
                }
            }

            return true;
        }

        private void DisposeRequest()
        {
            if (_request != null)
            {
                _request.Dispose();
                _request = null;
            }
        }

        private static bool HasRequestError(UnityWebRequest request)
        {
    #if UNITY_2020_2_OR_NEWER
            return request.result != UnityWebRequest.Result.Success;
    #else
            return request.isNetworkError || request.isHttpError;
    #endif
        }

        internal override void AppendFailContext(StringBuilder sb)
        {
            var vcUrl = (_versionCheckUrls != null &&
                         _versionCheckUrlIndex >= 0 &&
                         _versionCheckUrlIndex < _versionCheckUrls.Length)
                ? _versionCheckUrls[_versionCheckUrlIndex]
                : _config?.VersionCheckUrl;
            ZeonAssetLog.AppendKv(sb, "vc", vcUrl);
            ZeonAssetLog.AppendKv(sb, "cdn", _config?.RemoteUrl);
            ZeonAssetLog.AppendKv(sb, "manifestUrl", _pendingManifestUrl);
            ZeonAssetLog.AppendKv(sb, "localHash", LocalManifestHash);
            ZeonAssetLog.AppendKv(sb, "targetName", TargetVersion?.manifest_name);
            ZeonAssetLog.AppendKv(sb, "targetHash", TargetVersion?.manifest_hash);
        }

        internal override void OnRecycle()
        {
            DisposeRequest();
            _config = null;
            _packageName = null;
            _sandboxRoot = null;
            _streamingRoot = null;
            _pendingManifestUrl = null;
            _tempManifestPath = null;
            _versionCheckUrls = null;
            _versionCheckUrlIndex = 0;
            TargetVersion = null;
            LocalManifest = null;
            RemoteManifest = null;
            DirtyBundles = null;
            SkippedBecauseUpToDate = false;
            LocalManifestHash = null;
            _builtinManifest = null;
            base.OnRecycle();
        }
    }
}
