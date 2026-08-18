using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 编辑器模拟模式：AssetDatabase 直读，零打包秒级调试。
    /// </summary>
    public class EditorSimulateServices : IPlayModeServices
    {
        public string ModeName => "EditorSimulate";
        public PackageManifest Manifest { get; private set; }
        public bool IsInitialized { get; private set; }

        private ZeonAssetConfig _config;
        private string _packageName;

        public InitializationOperation InitializeAsync(ZeonAssetConfig config, string packageName)
        {
            _config = config ?? ZeonAssetConfig.CreateDefault();
            _packageName = string.IsNullOrEmpty(packageName) ? _config.DefaultPackageName : packageName;

#if !UNITY_EDITOR
            var unsupported = InitializationOperation.Create(this, _config, _packageName, null, manifest =>
            {
                IsInitialized = false;
            });
            OperationSystem.Start(unsupported);
            // 无清单路径会 Succeed；这里改为显式失败提示由业务检查平台
            ZeonAssetLog.Error("EditorSimulateMode is only available in Unity Editor.");
            IsInitialized = false;
            return unsupported;
#else
            string manifestPath = null;
            var root = string.IsNullOrEmpty(_config.BundleRoot)
                ? ZeonAssetPathHelper.GetDefaultBundleRoot(EPlayMode.HostPlay)
                : _config.BundleRoot;
            if (!string.IsNullOrEmpty(root))
            {
                var candidate = ZeonAssetPathHelper.GetManifestPath(root);
                if (ZeonAssetPathHelper.FileExistsCompat(candidate))
                    manifestPath = candidate;
            }

            // EditorSimulate：清单可选。解析失败也不阻断初始化（仍可用 AssetPath 直读）
            var op = InitializationOperation.Create(
                this,
                _config,
                _packageName,
                manifestPath,
                manifest =>
                {
                    Manifest = manifest;
                    IsInitialized = true;
                    if (manifest != null)
                        AssetManager.SetManifest(manifest);
                },
                manifestOptional: true);
            OperationSystem.Start(op);
            return op;
#endif
        }

        public LoadAssetOperation LoadAssetAsync(string location, Type assetType)
        {
            assetType = assetType ?? typeof(UnityEngine.Object);
            long key = InflightAssetLoadRegistry.MakeKey(location, assetType);

            long pathId = PathId.Get(location);
            if (AssetLoaderManager.TryGet(pathId, out var cached))
            {
                var instant = LoadAssetOperation.CreateFromCache(location, assetType, cached);
                OperationSystem.Start(instant);
                return instant;
            }

            if (InflightAssetLoadRegistry.TryGetPrimary(key, out var primary))
            {
                var follower = LoadAssetOperation.CreateFollower(location, assetType, primary);
                InflightAssetLoadRegistry.AttachFollower(key, follower);
                OperationSystem.Start(follower);
                return follower;
            }

            var op = LoadAssetOperation.Create(location, assetType, OnAssetStart, null);
            op.SetInflightKey(key);
            InflightAssetLoadRegistry.RegisterPrimary(key, op);
            OperationSystem.Start(op);
            return op;
        }

        public LoadSceneOperation LoadSceneAsync(string location, LoadSceneMode sceneMode)
        {
            var op = LoadSceneOperation.Create(location, sceneMode, OnSceneStart, null);
            OperationSystem.Start(op);
            return op;
        }

        public LoadSubAssetsOperation LoadSubAssetsAsync(string location, Type assetType)
        {
            assetType = assetType ?? typeof(UnityEngine.Object);
            var op = LoadSubAssetsOperation.Create(location, assetType, OnSubAssetsStart);
            OperationSystem.Start(op);
            return op;
        }

        private bool OnAssetStart(string location, Type assetType, bool _, LoadAssetOperation op)
        {
#if UNITY_EDITOR
            if (!IsInitialized)
            {
                op.CompleteWithError("EditorSimulateServices not initialized.");
                return true;
            }

            var assetPath = ResolveAssetPath(location);
            if (string.IsNullOrEmpty(assetPath))
            {
                op.CompleteWithError($"Asset not found: {location}");
                return true;
            }

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath(assetPath, assetType);
            op.CompleteWithAsset(location, assetPath, asset);
            return true;
#else
            op.CompleteWithError("EditorSimulate only available in Editor.");
            return true;
#endif
        }

        private bool OnSubAssetsStart(string location, Type assetType, LoadSubAssetsOperation op)
        {
#if UNITY_EDITOR
            if (!IsInitialized)
            {
                op.CompleteWithError("EditorSimulateServices not initialized.");
                return true;
            }

            var assetPath = ResolveAssetPath(location);
            if (string.IsNullOrEmpty(assetPath))
            {
                op.CompleteWithError($"Asset not found: {location}");
                return true;
            }

            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0)
            {
                op.CompleteWithError($"SubAssets empty: {assetPath}");
                return true;
            }

            if (assetType != null && assetType != typeof(UnityEngine.Object))
            {
                var filtered = new System.Collections.Generic.List<UnityEngine.Object>();
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] != null && assetType.IsInstanceOfType(assets[i]))
                        filtered.Add(assets[i]);
                }

                assets = filtered.ToArray();
            }

            op.CompleteWithSubAssets(location, assetPath, assets);
            return true;
#else
            op.CompleteWithError("EditorSimulate only available in Editor.");
            return true;
#endif
        }

        private bool OnSceneStart(string location, LoadSceneMode sceneMode, LoadSceneOperation op)
        {
#if UNITY_EDITOR
            if (!IsInitialized)
            {
                op.CompleteWithError("EditorSimulateServices not initialized.");
                return true;
            }

            var scenePath = ResolveAssetPath(location);
            if (string.IsNullOrEmpty(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                op.CompleteWithError($"Scene not found: {location}");
                return true;
            }

            var parameters = new LoadSceneParameters(sceneMode);
            var unityOp = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, parameters);
            op.CompleteWithScene(location, scenePath, unityOp);
            return true;
#else
            op.CompleteWithError("EditorSimulate only available in Editor.");
            return true;
#endif
        }

        private string ResolveAssetPath(string location)
        {
            if (string.IsNullOrEmpty(location))
                return null;

            if (Manifest != null)
            {
                if (Manifest.TryGetAssetByAddress(location, out var byAddress))
                    return byAddress.AssetPath;
                if (Manifest.TryGetAsset(location, out var byPath))
                    return byPath.AssetPath;
            }

            if (location.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return location;

            return location;
        }
    }
}
