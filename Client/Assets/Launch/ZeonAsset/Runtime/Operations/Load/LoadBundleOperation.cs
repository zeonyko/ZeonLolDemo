using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步加载单个 Bundle（含依赖等待由上层编排）。
    /// </summary>
    public class LoadBundleOperation : PooledOperation<LoadBundleOperation, AssetBundle>
    {
        private BundleLoader _loader;

        internal override void AppendFailContext(System.Text.StringBuilder sb)
        {
            ZeonAssetLog.AppendKv(sb, "bundle", _loader != null ? _loader.BundleName : null);
            ZeonAssetLog.AppendKv(sb, "path", _loader != null ? _loader.FullPath : null);
        }

        public static LoadBundleOperation Create(BundleLoader loader)
        {
            var op = Rent();
            op._loader = loader;
            return op;
        }

        protected override void InternalOnStart()
        {
            if (_loader == null)
            {
                Fail("BundleLoader is null.");
                return;
            }

            if (_loader.State == BundleLoader.ELoadState.Loaded)
            {
                Result = _loader.Bundle;
                Succeed();
                return;
            }

            if (_loader.State == BundleLoader.ELoadState.Failed)
            {
                Fail(_loader.Error);
                return;
            }

            _loader.StartLoad();
        }

        protected override void InternalOnUpdate()
        {
            if (_loader == null)
                return;

            Progress = _loader.Progress;
            if (_loader.State == BundleLoader.ELoadState.Loading)
                return;

            if (_loader.State == BundleLoader.ELoadState.Failed)
            {
                Fail(_loader.Error);
                return;
            }

            Result = _loader.Bundle;
            Succeed();
        }

        internal override void OnRecycle()
        {
            _loader = null;
            base.OnRecycle();
        }
    }
}
