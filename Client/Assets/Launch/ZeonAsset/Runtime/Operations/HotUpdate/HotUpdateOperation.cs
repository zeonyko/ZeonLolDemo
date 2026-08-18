using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 一次热更会话：UpdateManifest →（可选）DownloadDirtyBundles → 切换 ActiveManifest。
    /// </summary>
    public class HotUpdateOperation : PooledOperation<HotUpdateOperation>
    {
        private enum EStep
        {
            UpdateManifest,
            Download,
            Done,
        }

        private ZeonAssetConfig _config;
        private string _packageName;
        private EStep _step;
        private UpdateManifestOperation _updateOp;
        private DownloadDirtyBundlesOperation _downloadOp;

        protected override string DiagnosticStep => _step.ToString();

        public VersionCheckData TargetVersion { get; private set; }
        public PackageManifest RemoteManifest { get; private set; }
        public List<PackageBundle> DirtyBundles { get; private set; }
        public string SandboxRoot { get; private set; }
        public bool IsDownloading => _step == EStep.Download;
        public long DownloadCurrentBytes => _downloadOp != null ? _downloadOp.CurrentBytes : 0;
        public long DownloadTotalBytes => _downloadOp != null ? _downloadOp.TotalBytes : 0;
        public int DownloadFinishedCount => _downloadOp != null ? _downloadOp.FinishedCount : 0;
        public int DownloadTotalCount => _downloadOp != null ? _downloadOp.TotalCount : 0;

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "policy", _config != null ? _config.ResolveDownloadPolicy().ToString() : null);
            ZeonAssetLog.AppendKv(sb, "cdn", _config != null ? _config.RemoteUrl : null);
            ZeonAssetLog.AppendKv(sb, "dirty", DirtyBundles != null ? DirtyBundles.Count.ToString() : null);
            ZeonAssetLog.AppendKv(sb, "target", TargetVersion != null ? TargetVersion.manifest_name : null);
        }

        public static HotUpdateOperation Create(ZeonAssetConfig config, string packageName = null)
        {
            var op = Rent();
            op._config = config ?? AssetManager.Config ?? ZeonAssetConfig.CreateDefault();
            op._packageName = string.IsNullOrEmpty(packageName)
                ? op._config.DefaultPackageName
                : packageName;
            op._step = EStep.UpdateManifest;
            return op;
        }

        protected override void InternalOnStart()
        {
            _updateOp = UpdateManifestOperation.Create(_config, _packageName);
            OperationSystem.Start(_updateOp);
            _step = EStep.UpdateManifest;
        }

        protected override void InternalOnUpdate()
        {
            if (_step == EStep.UpdateManifest)
            {
                if (_updateOp == null || !_updateOp.IsDone)
                {
                    Progress = (_updateOp?.Progress ?? 0f) * 0.3f;
                    LoadingProgress.SetProgress(
                        ELoadingPhase.Checking, Progress, "正在检查更新");
                    return;
                }

                if (!_updateOp.IsSucceed)
                {
                    FailFrom(_updateOp);
                    return;
                }

                TargetVersion = _updateOp.TargetVersion;
                RemoteManifest = _updateOp.RemoteManifest;
                DirtyBundles = _updateOp.DirtyBundles;
                SandboxRoot = _updateOp.SandboxRoot;
                var tempManifestPath = _updateOp.TempManifestPath;
                var skipped = _updateOp.SkippedBecauseUpToDate;

                // has_update=false：跳过下载，直接进游戏
                if (skipped)
                {
                    AssetManager.SetManifest(RemoteManifest);
                    AssetManager.SetActiveVersionCheck(TargetVersion);
                    if (AssetManager.PlayModeServices is BundlePlayModeServices bundleServices)
                        bundleServices.ApplyHotUpdate(RemoteManifest, SandboxRoot);

                    ZeonAssetLog.Info(
                        $"Hot-update skipped (has_update=false). hash={RemoteManifest?.ManifestHash}");
                    _updateOp = null;
                    Progress = 1f;
                    _step = EStep.Done;
                    Succeed();
                    return;
                }

                ZeonAssetLog.Info(
                    $"Hot-update target={TargetVersion?.manifest_name}, " +
                    $"dirty={DirtyBundles?.Count ?? 0}, policy={_config.ResolveDownloadPolicy()}");

                var toDownload = DirtyBundles;
                if (_config.IsOnDemandDownload)
                {
                    if ((DirtyBundles?.Count ?? 0) > 0)
                    {
                        ZeonAssetLog.Info(
                            $"OnDemand: skip boot download of {DirtyBundles.Count} dirty bundles. " +
                            "缺包由 PlayWhileDownload / CreateDownloaderByTags 拉取。");
                    }

                    toDownload = new List<PackageBundle>();
                }

                _downloadOp = DownloadDirtyBundlesOperation.Create(
                    _config,
                    _packageName,
                    SandboxRoot,
                    RemoteManifest,
                    toDownload,
                    TargetVersion,
                    tempManifestPath);
                _updateOp = null;
                OperationSystem.Start(_downloadOp);
                _step = EStep.Download;
                return;
            }

            if (_step == EStep.Download)
            {
                if (_downloadOp == null || !_downloadOp.IsDone)
                {
                    Progress = 0.3f + (_downloadOp?.Progress ?? 0f) * 0.7f;
                    return;
                }

                if (!_downloadOp.IsSucceed)
                {
                    FailFrom(_downloadOp);
                    return;
                }

                _downloadOp = null;
                AssetManager.SetManifest(RemoteManifest);
                AssetManager.SetActiveVersionCheck(TargetVersion);
                if (AssetManager.PlayModeServices is BundlePlayModeServices bundleServices)
                    bundleServices.ApplyHotUpdate(RemoteManifest, SandboxRoot);

                Progress = 1f;
                _step = EStep.Done;
                Succeed();
            }
        }

        internal override void OnRecycle()
        {
            _config = null;
            _packageName = null;
            _updateOp = null;
            _downloadOp = null;
            TargetVersion = null;
            RemoteManifest = null;
            DirtyBundles = null;
            SandboxRoot = null;
            base.OnRecycle();
        }
    }
}
