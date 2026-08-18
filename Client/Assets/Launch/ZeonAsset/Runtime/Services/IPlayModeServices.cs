using System;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 运行模式统一接口（策略模式）。
    /// </summary>
    public interface IPlayModeServices
    {
        string ModeName { get; }
        PackageManifest Manifest { get; }
        bool IsInitialized { get; }

        InitializationOperation InitializeAsync(ZeonAssetConfig config, string packageName);

        LoadAssetOperation LoadAssetAsync(string location, Type assetType);

        LoadSubAssetsOperation LoadSubAssetsAsync(string location, Type assetType);

        LoadSceneOperation LoadSceneAsync(string location, UnityEngine.SceneManagement.LoadSceneMode sceneMode);
    }
}
