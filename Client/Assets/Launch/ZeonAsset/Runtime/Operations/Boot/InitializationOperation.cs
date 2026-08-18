using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 框架初始化：按 PlayMode 装载清单（全异步）。
    /// </summary>
    public class InitializationOperation : PooledOperation<InitializationOperation>
    {
        private IPlayModeServices _services;
        private ZeonAssetConfig _config;
        private string _packageName;
        private UnityWebRequest _request;
        private string _manifestPath;
        private Action<PackageManifest> _onManifestLoaded;
        private bool _manifestOptional;

        protected override string DiagnosticStep => "LoadManifest";

        public static InitializationOperation Create(
            IPlayModeServices services,
            ZeonAssetConfig config,
            string packageName,
            string manifestPath,
            Action<PackageManifest> onManifestLoaded,
            bool manifestOptional = false)
        {
            var op = Rent();
            op._services = services;
            op._config = config;
            op._packageName = packageName;
            op._manifestPath = manifestPath;
            op._onManifestLoaded = onManifestLoaded;
            op._manifestOptional = manifestOptional;
            return op;
        }

        protected override void InternalOnStart()
        {
            // EditorSimulate 可不依赖清单
            if (string.IsNullOrEmpty(_manifestPath))
            {
                _onManifestLoaded?.Invoke(null);
                Succeed();
                return;
            }

            // 全异步读清单，禁止 File.ReadAllText 阻塞主线程（WebGL 友好）
            var url = ZeonAssetPathHelper.ToRequestUrl(_manifestPath);
            _request = UnityWebRequest.Get(url);
            _request.timeout = Mathf.Max(1, (int)_config.TimeoutSeconds);
            _request.SendWebRequest();
        }

        protected override void InternalOnUpdate()
        {
            if (_request == null)
                return;

            Progress = _request.downloadProgress;
            if (!_request.isDone)
                return;

    #if UNITY_2020_2_OR_NEWER
            bool hasError = _request.result != UnityWebRequest.Result.Success;
    #else
            bool hasError = _request.isNetworkError || _request.isHttpError;
    #endif
            if (hasError)
            {
                if (_manifestOptional)
                {
                    ZeonAssetLog.Warn($"Optional manifest load failed, continue without it: {_manifestPath}, {_request.error}");
                    _onManifestLoaded?.Invoke(null);
                    Succeed();
                }
                else
                {
                    Fail($"Load manifest failed: {_manifestPath}, {_request.error}");
                }

                DisposeRequest();
                return;
            }

            try
            {
                var json = _request.downloadHandler.text;
                var manifest = PackageManifest.FromJson(json);
                if (string.IsNullOrEmpty(manifest.PackageName))
                    manifest.PackageName = _packageName;

                _onManifestLoaded?.Invoke(manifest);
                Succeed();
            }
            catch (Exception e)
            {
                if (_manifestOptional)
                {
                    ZeonAssetLog.Warn($"Optional manifest parse failed, continue without it: {e.Message}");
                    _onManifestLoaded?.Invoke(null);
                    Succeed();
                }
                else
                {
                    Fail(e.Message);
                }
            }
            finally
            {
                DisposeRequest();
            }
        }

        private void DisposeRequest()
        {
            if (_request != null)
            {
                _request.Dispose();
                _request = null;
            }
        }

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "manifest", _manifestPath);
            ZeonAssetLog.AppendKv(sb, "package", _packageName);
            ZeonAssetLog.AppendKv(sb, "mode", _config != null ? _config.PlayMode.ToString() : null);
        }

        internal override void OnRecycle()
        {
            DisposeRequest();
            _services = null;
            _config = null;
            _packageName = null;
            _manifestPath = null;
            _onManifestLoaded = null;
            _manifestOptional = false;
            base.OnRecycle();
        }
    }
}
