using UnityEngine;
using Game.ZeonAsset;
using Launch;

namespace Login
{
    /// <summary>
    /// 热更入口。Launch（AOT）反射到这里；只负责进入登录场景。
    /// 登录 UI 已挂在场景 Prefab 上，此处不再动态创建界面。
    /// </summary>
    public sealed class LoginGameApp : IGameApp
    {
        public void StartApp()
        {
            var hash = AssetManager.ActiveManifest != null
                ? AssetManager.ActiveManifest.ManifestHash
                : "(no manifest)";
            Debug.Log($"[Login] LoginGameApp.StartApp  清单 hash={hash}");

            AppSceneRunner.EnsureHost().LoadScene(LoginDestinations.LoginScene, requireTags: null);
        }
    }
}
