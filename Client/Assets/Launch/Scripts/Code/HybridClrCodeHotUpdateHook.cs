using System;
using System.Collections.Generic;
using UnityEngine;
using Game.ZeonAsset;

namespace Launch
{
    /// <summary>
    /// 代码热更胶水：ZeonAsset 只认 ICodeHotUpdateHook。
    /// 顺序：HostBoot 清单生效 → 本钩子 → ShaderWarmup → GameAppBootstrap。
    /// </summary>
    public class HybridClrCodeHotUpdateHook : ICodeHotUpdateHook
    {
        public string[] DownloadTags { get; set; } = { "HotUpdate", "AOT" };
        public string[] AotDllLocations { get; set; } = Array.Empty<string>();
        public string[] HotUpdateDllLocations { get; set; } = Array.Empty<string>();

        public AsyncOperationBase CreateLoadOperation(IRawFileProvider rawFiles)
        {
            var aot = HybridClrDllPaths.ResolveLocations(AotDllLocations, HybridClrDllPaths.DefaultAotLocations());
            var hot = HybridClrDllPaths.ResolveLocations(
                HotUpdateDllLocations,
                HybridClrDllPaths.DefaultHotUpdateLocations);
            return HybridClrLoadOperation.Create(rawFiles, DownloadTags, aot, hot);
        }
    }

    /// <summary>Tag 下载 → LoadRawFile → AOT 元数据 → Assembly.Load。</summary>
    public class HybridClrLoadOperation : AsyncOperationBase
    {
        private enum EStep
        {
            Download,
            LoadDlls,
        }

        private IRawFileProvider _raw;
        private string[] _tags;
        private string[] _aotLocations;
        private string[] _hotUpdateLocations;
        private ZeonAssetDownloaderOperation _dl;
        private readonly List<LoadRawFileOperation> _loads = new List<LoadRawFileOperation>();
        private readonly List<RawFileHandle> _dllHandles = new List<RawFileHandle>();
        private readonly List<bool> _isAot = new List<bool>();
        private EStep _step;

        public static HybridClrLoadOperation Create(
            IRawFileProvider raw,
            string[] tags,
            string[] aotLocations,
            string[] hotUpdateLocations)
        {
            return new HybridClrLoadOperation
            {
                _raw = raw,
                _tags = tags ?? Array.Empty<string>(),
                _aotLocations = aotLocations ?? Array.Empty<string>(),
                _hotUpdateLocations = hotUpdateLocations ?? Array.Empty<string>(),
                _step = EStep.Download,
            };
        }

        protected override void InternalOnStart()
        {
            if (Application.isEditor)
            {
                Debug.Log("[Launch] 编辑器跳过 DLL 下载/Assembly.Load，将由 GameAppBootstrap 进入游戏。");
                Progress = 1f;
                Succeed();
                return;
            }

            if (_tags.Length > 0)
            {
                _dl = _raw.CreateDownloaderByTags(_tags);
                _step = EStep.Download;
                return;
            }

            BeginLoadDlls();
        }

        protected override void InternalOnUpdate()
        {
            if (_step == EStep.Download)
            {
                if (_dl != null && !_dl.IsDone)
                {
                    Progress = _dl.Progress * 0.4f;
                    return;
                }

                if (_dl != null && !_dl.IsSucceed)
                {
                    Fail(_dl.Error);
                    return;
                }

                _dl = null;
                BeginLoadDlls();
                return;
            }

            if (_step != EStep.LoadDlls)
                return;

            if (_loads.Count == 0)
            {
                Progress = 1f;
                Succeed();
                return;
            }

            float sum = 0f;
            int done = 0;
            for (int i = 0; i < _loads.Count; i++)
            {
                var load = _loads[i];
                if (!load.IsDone)
                {
                    sum += load.Progress;
                    continue;
                }

                if (!load.IsSucceed)
                {
                    Fail(load.Error);
                    return;
                }

                if (_dllHandles[i] == null)
                    _dllHandles[i] = load.Result;

                done++;
                sum += 1f;
            }

            Progress = 0.4f + 0.6f * (sum / _loads.Count);
            if (done < _loads.Count)
                return;

            for (int i = 0; i < _dllHandles.Count; i++)
            {
                if (!_isAot[i])
                    continue;
                if (!TryLoadAotMetadata(_dllHandles[i]))
                {
                    Fail("AOT metadata load failed: " + (_dllHandles[i]?.Address ?? "?"));
                    return;
                }
            }

            for (int i = 0; i < _dllHandles.Count; i++)
            {
                if (_isAot[i])
                    continue;
                if (!TryLoadHotUpdateAssembly(_dllHandles[i]))
                {
                    Fail("Assembly.Load failed: " + (_dllHandles[i]?.Address ?? "?"));
                    return;
                }
            }

            for (int i = 0; i < _dllHandles.Count; i++)
                _dllHandles[i]?.Release();

            Progress = 1f;
            Succeed();
        }

        private void BeginLoadDlls()
        {
            _step = EStep.LoadDlls;
            _loads.Clear();
            _dllHandles.Clear();
            _isAot.Clear();

            Enqueue(_aotLocations, isAot: true);
            Enqueue(_hotUpdateLocations, isAot: false);

            if (_loads.Count == 0)
            {
                Debug.Log("[Launch] 无 DLL location，跳过代码加载。");
                Progress = 1f;
                Succeed();
            }
        }

        private void Enqueue(string[] locations, bool isAot)
        {
            if (locations == null)
                return;
            for (int i = 0; i < locations.Length; i++)
            {
                if (string.IsNullOrEmpty(locations[i]))
                    continue;
                _loads.Add(_raw.LoadRawFileAsync(locations[i]));
                _dllHandles.Add(null);
                _isAot.Add(isAot);
            }
        }

        private static bool TryLoadAotMetadata(RawFileHandle handle)
        {
            var bytes = handle?.GetData();
            if (bytes == null || bytes.Length == 0)
                return false;

#if HYBRIDCLR
            var err = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(
                bytes, HybridCLR.HomologousImageMode.SuperSet);
            if (err != HybridCLR.LoadImageErrorCode.OK)
            {
                Debug.LogError($"[Launch] LoadMetadataForAOTAssembly failed: {err} addr={handle.Address}");
                return false;
            }

            Debug.Log($"[Launch] AOT metadata OK bytes={bytes.Length} addr={handle.Address}");
            return true;
#else
            Debug.Log(
                $"[Launch] HybridCLR stub AOT bytes={bytes.Length} addr={handle.Address}");
            return true;
#endif
        }

        private static bool TryLoadHotUpdateAssembly(RawFileHandle handle)
        {
            var bytes = handle?.GetData();
            if (bytes == null || bytes.Length == 0)
                return false;

#if HYBRIDCLR
            System.Reflection.Assembly.Load(bytes);
            Debug.Log($"[Launch] Assembly.Load OK bytes={bytes.Length} addr={handle.Address}");
            return true;
#else
            Debug.Log(
                $"[Launch] HybridCLR stub Assembly.Load bytes={bytes.Length} addr={handle.Address}");
            return true;
#endif
        }

        protected override void OnCancelRequested()
        {
            _dl?.Cancel();
            for (int i = 0; i < _loads.Count; i++)
                _loads[i]?.Cancel();
        }
    }
}
