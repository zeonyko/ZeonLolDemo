using System;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 业务层安全资源句柄。Dispose/Release 等价。
    /// </summary>
    public class AssetHandle : IDisposable
    {
        private static readonly ObjectPool<AssetHandle> Pool = new ObjectPool<AssetHandle>(
            null,
            h => h.Reset());

        private AssetLoader _loader;
        private bool _released;
        private int _localRef;
        private Action<AssetHandle> _completed;

        public string Address => _loader != null ? _loader.Address : null;
        public string AssetPath => _loader != null ? _loader.AssetPath : null;
        public long PathID => _loader != null ? _loader.PathID : 0;
        public UnityEngine.Object AssetObject => _loader != null ? _loader.AssetObject : null;
        public bool IsValid => !_released && _localRef > 0 && _loader != null && _loader.IsValid;
        public bool IsDone { get; private set; }
        public string Error { get; private set; }
        public int RefCount => _loader != null ? _loader.RefCount : 0;

        public event Action<AssetHandle> Completed
        {
            add
            {
                if (IsDone)
                    value?.Invoke(this);
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        public T GetAsset<T>() where T : UnityEngine.Object
        {
            if (!IsValid)
                return null;
            return AssetObject as T;
        }

        public void Retain()
        {
            if (_released || _loader == null)
                return;
            _localRef++;
            _loader.Retain();
        }

        public void Release()
        {
            if (_released || _localRef <= 0)
                return;

            _localRef--;
            _loader?.Release();

            if (_localRef > 0)
                return;

            _released = true;
            _loader = null;
            IsDone = true;
            _completed = null;
            Pool.Release(this);
        }

        public void Dispose() => Release();

        internal void RaiseCompleted()
        {
            var cb = _completed;
            _completed = null;
            cb?.Invoke(this);
        }

        internal static AssetHandle Create(
            string address,
            string assetPath,
            UnityEngine.Object asset,
            BundleLoader ownerBundle = null,
            string error = null)
        {
            var handle = Pool.Get();
            handle._released = false;
            handle._localRef = 0;
            handle.Error = error;
            handle.IsDone = true;
            handle._completed = null;

            if (asset != null)
            {
                var loader = AssetLoaderManager.GetOrCreate(address, assetPath, asset, ownerBundle);
                handle._loader = loader;
                handle._localRef = 1;
                loader.Retain();
            }
            else
            {
                handle._loader = null;
                if (string.IsNullOrEmpty(error))
                    handle.Error = "Asset is null.";
            }

            return handle;
        }

        private void Reset()
        {
            _loader = null;
            _released = false;
            _localRef = 0;
            Error = null;
            IsDone = false;
            _completed = null;
        }
    }

    /// <summary>子资源句柄（图集 Sprite / 多子资产）。</summary>
    public class SubAssetHandle : IDisposable
    {
        private static readonly ObjectPool<SubAssetHandle> Pool = new ObjectPool<SubAssetHandle>(
            null,
            h => h.Reset());

        private AssetHandle _mainHandle;
        private UnityEngine.Object[] _subAssets;
        private bool _released;
        private Action<SubAssetHandle> _completed;

        public string Address => _mainHandle != null ? _mainHandle.Address : null;
        public UnityEngine.Object[] SubAssets => _subAssets;
        public bool IsValid => !_released && _mainHandle != null && _mainHandle.IsValid;
        public bool IsDone { get; private set; } = true;
        public string Error { get; private set; }

        public event Action<SubAssetHandle> Completed
        {
            add
            {
                if (IsDone)
                    value?.Invoke(this);
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        public T GetSubAsset<T>(string name) where T : UnityEngine.Object
        {
            if (_subAssets == null)
                return null;
            for (int i = 0; i < _subAssets.Length; i++)
            {
                var a = _subAssets[i];
                if (a != null && a.name == name && a is T typed)
                    return typed;
            }

            return null;
        }

        internal static SubAssetHandle Create(AssetHandle mainHandle, UnityEngine.Object[] subAssets, string error = null)
        {
            var handle = Pool.Get();
            handle._released = false;
            handle._mainHandle = mainHandle;
            handle._subAssets = subAssets;
            handle.Error = error;
            handle.IsDone = true;
            handle._completed = null;
            return handle;
        }

        public void Release()
        {
            if (_released)
                return;
            _released = true;
            _mainHandle?.Release();
            _mainHandle = null;
            _subAssets = null;
            _completed = null;
            Pool.Release(this);
        }

        public void Dispose() => Release();

        private void Reset()
        {
            _mainHandle = null;
            _subAssets = null;
            _released = false;
            Error = null;
            IsDone = false;
            _completed = null;
        }
    }

    /// <summary>场景加载句柄。</summary>
    public class SceneHandle : IDisposable
    {
        private static readonly ObjectPool<SceneHandle> Pool = new ObjectPool<SceneHandle>(
            null,
            h => h.Reset());

        private BundleLoader _ownerLoader;
        private bool _released;

        public string Address { get; private set; }
        public string ScenePath { get; private set; }
        public long PathID { get; private set; }
        public AsyncOperation UnityAsyncOp { get; private set; }
        public bool IsDone { get; private set; }
        public string Error { get; private set; }
        public bool IsValid => !_released && string.IsNullOrEmpty(Error) && IsDone;

        internal static SceneHandle Create(
            string address,
            string scenePath,
            AsyncOperation unityOp,
            BundleLoader ownerLoader = null,
            string error = null)
        {
            var handle = Pool.Get();
            handle._released = false;
            handle.Address = address;
            handle.ScenePath = scenePath;
            handle.PathID = PathId.Get(string.IsNullOrEmpty(address) ? scenePath : address);
            handle.UnityAsyncOp = unityOp;
            handle.Error = error;
            handle.IsDone = true;
            handle._ownerLoader = ownerLoader;
            ownerLoader?.Retain();
            return handle;
        }

        public void Release()
        {
            if (_released)
                return;
            _released = true;
            _ownerLoader?.Release();
            _ownerLoader = null;
            UnityAsyncOp = null;
            Pool.Release(this);
        }

        public void Dispose() => Release();

        private void Reset()
        {
            Address = null;
            ScenePath = null;
            PathID = 0;
            UnityAsyncOp = null;
            Error = null;
            IsDone = false;
            _ownerLoader = null;
            _released = false;
        }
    }

    /// <summary>
    /// 原生文件句柄（配置表 / DLL.bytes 等）。
    /// </summary>
    public class RawFileHandle : IDisposable
    {
        private static readonly ObjectPool<RawFileHandle> Pool = new ObjectPool<RawFileHandle>(
            null,
            h => h.Reset());

        private bool _released;
        private byte[] _data;

        public string Address { get; private set; }
        public string FilePath { get; private set; }
        public bool IsValid => !_released && _data != null;
        public bool IsDone { get; private set; }
        public string Error { get; private set; }

        public byte[] GetData() => IsValid ? _data : null;

        public string GetFilePath() => FilePath;

        internal static RawFileHandle Create(string address, string filePath, byte[] data, string error = null)
        {
            var handle = Pool.Get();
            handle._released = false;
            handle.Address = address;
            handle.FilePath = filePath;
            handle._data = data;
            handle.Error = error;
            handle.IsDone = true;
            return handle;
        }

        public void Release()
        {
            if (_released)
                return;
            _released = true;
            _data = null;
            Address = null;
            FilePath = null;
            Pool.Release(this);
        }

        public void Dispose() => Release();

        private void Reset()
        {
            _released = false;
            _data = null;
            Address = null;
            FilePath = null;
            Error = null;
            IsDone = false;
        }
    }
}
