using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// Shader 预热：加载 shaders.bundle，并对 ShaderVariantCollection 调用 WarmUp。
    /// </summary>
    public class ShaderWarmupOperation : PooledOperation<ShaderWarmupOperation>
    {
        private enum EStep
        {
            Download,
            LoadBundle,
            LoadAssets,
            WarmUp,
            Done,
        }

        private string _bundleName;
        private EStep _step;
        private BundleLoader _loader;
        private readonly List<BundleLoader> _depLoaders = new List<BundleLoader>();
        private AssetBundleRequest _request;
        private UnityEngine.Object[] _assets;
        private int _warmIndex;
        private ZeonAssetDownloaderOperation _dlOp;
        private PackageBundle _packageBundle;

        protected override string DiagnosticStep => _step.ToString();

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "bundle", _bundleName);
        }

        public static ShaderWarmupOperation Create(string shaderBundleName = null)
        {
            var op = Rent();
            op._bundleName = string.IsNullOrEmpty(shaderBundleName)
                ? (AssetManager.Config?.ShaderBundleName ?? "shaders")
                : shaderBundleName;
            op._step = EStep.Download;
            op._warmIndex = 0;
            op._depLoaders.Clear();
            op._dlOp = null;
            op._packageBundle = null;
            return op;
        }

        protected override void InternalOnStart()
        {
            var manifest = AssetManager.ActiveManifest;
            if (manifest == null)
            {
                ZeonAssetLog.Info("ShaderWarmup skipped: no ActiveManifest.");
                Succeed();
                return;
            }

            PackageBundle packageBundle = null;
            if (!manifest.TryGetBundle(_bundleName, out packageBundle))
            {
                for (int i = 0; i < manifest.Bundles.Count; i++)
                {
                    var b = manifest.Bundles[i];
                    if (b.Tags != null && b.Tags.Contains("Shaders"))
                    {
                        packageBundle = b;
                        _bundleName = b.BundleName;
                        break;
                    }
                }
            }

            if (packageBundle == null)
            {
                ZeonAssetLog.Warn($"Shader bundle not found: {_bundleName}. Skip warmup.");
                Succeed();
                return;
            }

            _packageBundle = packageBundle;
            var missing = CollectMissingShaderBundles(packageBundle);
            if (missing.Count > 0)
            {
                var config = AssetManager.Config;
                if (CdnUrlUtility.ResolveRemoteRoots(config).Count == 0)
                {
                    ZeonAssetLog.Warn(
                        $"ShaderWarmup: {missing.Count} bundles missing and no CDN, try load anyway.");
                    BeginLoadShaderBundle();
                    return;
                }

                _dlOp = ZeonAssetDownloaderOperation.CreateByBundles(config, missing);
                OperationSystem.Start(_dlOp);
                _step = EStep.Download;
                return;
            }

            BeginLoadShaderBundle();
        }

        private static List<PackageBundle> CollectMissingShaderBundles(PackageBundle root)
        {
            var missing = new List<PackageBundle>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectMissingShaderRecursive(root, missing, visited);
            return missing;
        }

        private static void CollectMissingShaderRecursive(
            PackageBundle bundle,
            List<PackageBundle> missing,
            HashSet<string> visited)
        {
            if (bundle == null || !visited.Add(bundle.BundleName))
                return;

            var manifest = AssetManager.ActiveManifest;
            if (bundle.DependBundles != null && manifest != null)
            {
                for (int i = 0; i < bundle.DependBundles.Count; i++)
                {
                    if (manifest.TryGetBundle(bundle.DependBundles[i], out var dep))
                        CollectMissingShaderRecursive(dep, missing, visited);
                }
            }

            var sandbox = AssetManager.PlayModeServices is BundlePlayModeServices bps
                ? bps.GetBundleRoot()
                : DiskCacheManager.GetSandboxRoot();
            if (!DiskCacheManager.IsBundleAvailableLocally(
                    bundle, sandbox, DiskCacheManager.GetStreamingRoot(), AssetManager.BuiltinManifest))
                missing.Add(bundle);
        }

        private void BeginLoadShaderBundle()
        {
            var packageBundle = _packageBundle;
            var manifest = AssetManager.ActiveManifest;
            var root = AssetManager.PlayModeServices is BundlePlayModeServices bps
                ? bps.GetBundleRoot()
                : DiskCacheManager.GetSandboxRoot();
            var streaming = DiskCacheManager.GetStreamingRoot();

            var fullPath = ZeonAssetPathHelper.ResolveBundleLoadPath(root, streaming, packageBundle.FileName);
            _loader = BundleLoaderManager.GetOrCreate(
                packageBundle.BundleName, packageBundle.FileName, fullPath,
                packageBundle.EncryptMode, packageBundle.LoadOffset);

            _depLoaders.Clear();
            if (packageBundle.DependBundles != null && manifest != null)
            {
                for (int i = 0; i < packageBundle.DependBundles.Count; i++)
                {
                    if (!manifest.TryGetBundle(packageBundle.DependBundles[i], out var depInfo))
                        continue;
                    var depPath = ZeonAssetPathHelper.ResolveBundleLoadPath(root, streaming, depInfo.FileName);
                    var depLoader = BundleLoaderManager.GetOrCreate(
                        depInfo.BundleName, depInfo.FileName, depPath,
                        depInfo.EncryptMode, depInfo.LoadOffset);
                    _depLoaders.Add(depLoader);
                    depLoader.StartLoad();
                }
            }

            _loader.SetDirectDependencies(new List<BundleLoader>(_depLoaders));
            _loader.StartLoad();
            _loader.Retain();
            _step = EStep.LoadBundle;
        }

        protected override void InternalOnUpdate()
        {
            if (_step == EStep.Download)
            {
                if (_dlOp == null)
                {
                    BeginLoadShaderBundle();
                    return;
                }

                Progress = _dlOp.Progress * 0.3f;
                if (!_dlOp.IsDone)
                    return;
                if (!_dlOp.IsSucceed)
                {
                    FailFrom(_dlOp);
                    return;
                }

                _dlOp = null;
                BeginLoadShaderBundle();
                return;
            }

            if (_step == EStep.LoadBundle)
            {
                if (_loader == null)
                {
                    Fail("Shader BundleLoader is null.");
                    return;
                }

                for (int i = 0; i < _depLoaders.Count; i++)
                {
                    var dep = _depLoaders[i];
                    if (dep.State == BundleLoader.ELoadState.Failed)
                    {
                        Fail(dep.Error);
                        return;
                    }

                    if (dep.State != BundleLoader.ELoadState.Loaded)
                    {
                        Progress = dep.Progress * 0.2f;
                        return;
                    }
                }

                if (_loader.State == BundleLoader.ELoadState.Loading)
                {
                    Progress = 0.2f + _loader.Progress * 0.2f;
                    return;
                }

                if (_loader.State == BundleLoader.ELoadState.Failed)
                {
                    Fail(_loader.Error);
                    return;
                }

                if (_loader.Bundle == null)
                {
                    Fail("Shader bundle is null.");
                    return;
                }

                _request = _loader.Bundle.LoadAllAssetsAsync();
                _step = EStep.LoadAssets;
                return;
            }

            if (_step == EStep.LoadAssets)
            {
                if (_request == null || !_request.isDone)
                {
                    Progress = 0.4f + (_request?.progress ?? 0f) * 0.3f;
                    return;
                }

                _assets = _request.allAssets;
                _request = null;
                _warmIndex = 0;
                _step = EStep.WarmUp;
                return;
            }

            if (_step == EStep.WarmUp)
            {
                if (_assets == null || _assets.Length == 0)
                {
                    Progress = 1f;
                    Succeed();
                    return;
                }

                int budget = 2;
                while (budget-- > 0 && _warmIndex < _assets.Length)
                {
                    var obj = _assets[_warmIndex++];
                    if (obj is ShaderVariantCollection svc)
                    {
                        svc.WarmUp();
                        ZeonAssetLog.Info($"ShaderVariantCollection.WarmUp: {svc.name}");
                    }
                }

                Progress = 0.7f + 0.3f * ((float)_warmIndex / _assets.Length);
                if (_warmIndex >= _assets.Length)
                {
                    Progress = 1f;
                    Succeed();
                }
            }
        }

        protected override void OnCancelRequested()
        {
            _dlOp?.Cancel();
        }

        internal override void OnRecycle()
        {
            _loader?.Release();
            _loader = null;
            _depLoaders.Clear();
            _request = null;
            _assets = null;
            _bundleName = null;
            _dlOp = null;
            _packageBundle = null;
            base.OnRecycle();
        }
    }
}
