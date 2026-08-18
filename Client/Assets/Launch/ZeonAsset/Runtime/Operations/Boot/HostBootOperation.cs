using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// Host 一键启动：Initialize → HotUpdate →（可选）代码钩子 → ShaderWarmup。
    /// </summary>
    public class HostBootOperation : PooledOperation<HostBootOperation>
    {
        private enum EStep
        {
            Init,
            Update,
            CodeHotUpdate,
            Warmup,
            Done,
        }

        private InitializationOperation _initOp;
        private HotUpdateOperation _updateOp;
        private AsyncOperationBase _codeOp;
        private ShaderWarmupOperation _warmupOp;
        private ZeonAssetConfig _config;
        private EStep _step;

        protected override string DiagnosticStep => _step.ToString();

        public static HostBootOperation Create(InitializationOperation initOp, ZeonAssetConfig config)
        {
            var op = Rent();
            op._initOp = initOp;
            op._config = config;
            op._step = EStep.Init;
            return op;
        }

        protected override void InternalOnStart()
        {
            _step = EStep.Init;
        }

        protected override void InternalOnUpdate()
        {
            if (_step == EStep.Init)
            {
                if (_initOp != null && !_initOp.IsDone)
                {
                    Progress = _initOp.Progress * 0.15f;
                    LoadingProgress.SetProgress(
                        ELoadingPhase.Checking, Progress, "正在读取清单");
                    return;
                }

                if (_initOp != null && !_initOp.IsSucceed)
                {
                    FailFrom(_initOp);
                    return;
                }

                _initOp = null;
                _updateOp = HotUpdateOperation.Create(_config);
                OperationSystem.Start(_updateOp);
                _step = EStep.Update;
                return;
            }

            if (_step == EStep.Update)
            {
                if (_updateOp != null && !_updateOp.IsDone)
                {
                    Progress = 0.15f + _updateOp.Progress * 0.5f;
                    if (_updateOp.IsDownloading)
                    {
                        long cur = _updateOp.DownloadCurrentBytes;
                        long total = _updateOp.DownloadTotalBytes;
                        float p = total > 0
                            ? Mathf.Clamp01((float)cur / total)
                            : Mathf.Clamp01(_updateOp.Progress);
                        LoadingProgress.SetDownload(
                            cur,
                            total,
                            _updateOp.DownloadFinishedCount,
                            _updateOp.DownloadTotalCount);
                        LoadingProgress.SetProgress(
                            ELoadingPhase.Downloading, p, "正在下载资源");
                    }
                    else
                    {
                        LoadingProgress.SetProgress(
                            ELoadingPhase.Checking, Progress, "正在检查更新");
                    }

                    return;
                }

                if (_updateOp != null && !_updateOp.IsSucceed)
                {
                    FailFrom(_updateOp);
                    return;
                }

                _updateOp = null;
                // 清单已生效。未注入钩子则跳过，不依赖任何代码热更 SDK。
                if (_config?.CodeHotUpdateHook != null)
                {
                    _codeOp = _config.CodeHotUpdateHook.CreateLoadOperation(AssetManager.RawFiles);
                    if (_codeOp != null)
                    {
                        OperationSystem.Start(_codeOp);
                        _step = EStep.CodeHotUpdate;
                        return;
                    }
                }

                BeginWarmupOrFinish();
                return;
            }

            if (_step == EStep.CodeHotUpdate)
            {
                if (_codeOp != null && !_codeOp.IsDone)
                {
                    Progress = 0.65f + _codeOp.Progress * 0.15f;
                    LoadingProgress.SetProgress(
                        ELoadingPhase.LoadingCode, Progress, "正在加载代码");
                    return;
                }

                if (_codeOp != null && !_codeOp.IsSucceed)
                {
                    FailFrom(_codeOp);
                    return;
                }

                _codeOp = null;
                BeginWarmupOrFinish();
                return;
            }

            if (_step == EStep.Warmup)
            {
                if (_warmupOp != null && !_warmupOp.IsDone)
                {
                    Progress = 0.8f + _warmupOp.Progress * 0.2f;
                    LoadingProgress.SetProgress(
                        ELoadingPhase.WarmingShaders, Progress, "正在预热 Shader");
                    return;
                }

                if (_warmupOp != null && !_warmupOp.IsSucceed)
                    ZeonAssetLog.Warn("ShaderWarmup skipped after fail: " + _warmupOp.Error);

                _warmupOp = null;
                Progress = 1f;
                _step = EStep.Done;
                Succeed();
            }
        }

        private void BeginWarmupOrFinish()
        {
            if (_config != null && _config.EnableShaderWarmup)
            {
                _warmupOp = ShaderWarmupOperation.Create(_config.ShaderBundleName);
                OperationSystem.Start(_warmupOp);
                _step = EStep.Warmup;
                return;
            }

            Progress = 1f;
            _step = EStep.Done;
            Succeed();
        }

        protected override void OnCancelRequested()
        {
            _updateOp?.Cancel();
            _codeOp?.Cancel();
            _warmupOp?.Cancel();
        }

        internal override void OnRecycle()
        {
            _initOp = null;
            _updateOp = null;
            _codeOp = null;
            _warmupOp = null;
            _config = null;
            base.OnRecycle();
        }
    }
}
