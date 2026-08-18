using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 单个 Asset 的运行时加载器：持有资产对象，并对 Owner Bundle 做引用计数。
    /// 多个 AssetHandle 可共享同一 AssetLoader（按 PathID）。
    /// </summary>
    public class AssetLoader : RefCountObject
    {
        public long PathID { get; private set; }
        public string Address { get; private set; }
        public string AssetPath { get; private set; }
        public UnityEngine.Object AssetObject { get; private set; }
        public BundleLoader OwnerBundle { get; private set; }
        public bool IsValid => AssetObject != null;

        internal void Setup(string address, string assetPath, UnityEngine.Object asset, BundleLoader ownerBundle)
        {
            Address = address;
            AssetPath = assetPath;
            PathID = PathId.Get(string.IsNullOrEmpty(address) ? assetPath : address);
            AssetObject = asset;
            OwnerBundle = ownerBundle;
            ResetRefCount();
        }

        public override void Retain()
        {
            bool first = RefCount == 0;
            base.Retain();
            if (first)
                OwnerBundle?.Retain();
        }

        protected override void OnRefZero()
        {
            OwnerBundle?.Release();
            // 资产对象本身不 Destroy；由 Bundle Unload(false) 管理包体
            // 从缓存移除，允许下次重新加载
            AssetLoaderManager.Remove(this);
            AssetObject = null;
            OwnerBundle = null;
            Address = null;
            AssetPath = null;
            PathID = 0;
            AssetLoaderManager.ReleaseToPool(this);
        }

        internal void ResetForPool()
        {
            AssetObject = null;
            OwnerBundle = null;
            Address = null;
            AssetPath = null;
            PathID = 0;
            ResetRefCount();
        }
    }

    /// <summary>
    /// AssetLoader 缓存：同 PathID 复用，避免重复 LoadAsset。
    /// </summary>
    public static class AssetLoaderManager
    {
        private static readonly Dictionary<long, AssetLoader> Loaders = new Dictionary<long, AssetLoader>();
        private static readonly ObjectPool<AssetLoader> Pool = new ObjectPool<AssetLoader>();

        public static AssetLoader GetOrCreate(
            string address,
            string assetPath,
            UnityEngine.Object asset,
            BundleLoader ownerBundle)
        {
            long pathId = PathId.Get(string.IsNullOrEmpty(address) ? assetPath : address);
            if (Loaders.TryGetValue(pathId, out var existing) && existing.IsValid)
            {
                return existing;
            }

            var loader = Pool.Get();
            loader.Setup(address, assetPath, asset, ownerBundle);
            Loaders[pathId] = loader;
            return loader;
        }

        public static bool TryGet(long pathId, out AssetLoader loader)
        {
            return Loaders.TryGetValue(pathId, out loader) && loader != null && loader.IsValid;
        }

        internal static void Remove(AssetLoader loader)
        {
            if (loader == null)
                return;
            if (Loaders.TryGetValue(loader.PathID, out var cur) && ReferenceEquals(cur, loader))
                Loaders.Remove(loader.PathID);
        }

        internal static void ReleaseToPool(AssetLoader loader)
        {
            if (loader == null)
                return;
            loader.ResetForPool();
            Pool.Release(loader);
        }

        public static void Clear()
        {
            var snapshot = new List<AssetLoader>(Loaders.Values);
            Loaders.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var loader = snapshot[i];
                if (loader == null)
                    continue;
                // 强制释放 bundle 引用
                while (loader.RefCount > 0)
                    loader.Release();
            }
        }

        public static int CachedCount => Loaders.Count;

        public static IEnumerable<AssetLoader> Enumerate() => Loaders.Values;
    }
}
