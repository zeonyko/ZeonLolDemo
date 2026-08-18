using System;
using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>运行期资源配置。</summary>
    [Serializable]
    public class ZeonAssetConfig
    {
        public EPlayMode PlayMode = EPlayMode.EditorSimulate;

        public float TimeoutSeconds = 60f;
        public int MaxConcurrentDownloads = 4;
        public float UnloadDelaySeconds = 10f;

        /// <summary>Manifest 内逻辑包 ID（非路径分段）。</summary>
        public string DefaultPackageName = ZeonAssetPathLayout.DefaultPackageId;

        /// <summary>本地沙盒根覆盖。Host 默认：persistentDataPath/AssetBundles/</summary>
        public string BundleRoot;

        /// <summary>
        /// CDN 资源根。可被 VersionCheck.cdn_host 覆盖。
        /// 布局：{RemoteUrl}/Manifests/...  {RemoteUrl}/Bundles/{hash}.bundle
        /// </summary>
        public string RemoteUrl;

        /// <summary>备用 CDN 根（多源故障切换）。与 RemoteUrl 合并，RemoteUrl 优先。</summary>
        public string[] RemoteUrls;

        /// <summary>
        /// 开局脏包下载策略。Auto：WebGL=OnDemand，其它平台=Full。
        /// Full=清单 Diff 后一次下完；OnDemand=只提交清单，缺包由 PlayWhileDownload / Tag 再拉。
        /// </summary>
        public EBundleDownloadPolicy DownloadPolicy = EBundleDownloadPolicy.Auto;

        /// <summary>Host 加载缺包时自动下载再继续。OnDemand 策略下即使为 false 也会视为开启。</summary>
        public bool EnablePlayWhileDownload = true;

        /// <summary>XOR 解密密钥（与构建 EncryptXorKey 一致）。</summary>
        public byte XorDecryptKey = 0x5A;

        /// <summary>远端调度主地址。静态 json 走 GET，其它地址 POST。cdn_host 只认接口返回。</summary>
        public string VersionCheckUrl;

        /// <summary>备用调度。主地址失败时尝试。</summary>
        public string BackupVersionCheckUrl;

        /// <summary>有序调度列表（沙盒下次地址 + 主/备）。优先于单个 URL 字段。</summary>
        public string[] VersionCheckUrls;

        /// <summary>客户端 App 版本。空则 Application.version。</summary>
        public string AppVersion;

        public string Channel = "official";

        /// <summary>
        /// VersionCheck 上报的平台名（对应 CDN/{platform}）。
        /// 空则 Application.platform；编辑器 HostPlay 应填 Build Target（如 Android）。
        /// </summary>
        public string Platform;

        public bool AutoUpdateOnInit = true;
        /// <summary>
        /// Host / Offline 默认打开。没有 shaders.bundle 时运行时会跳过，不报错。
        /// </summary>
        public bool EnableShaderWarmup = true;
        public string ShaderBundleName = "shaders";
        public int DownloadRetryCount = 2;

        /// <summary>热更提交后是否 GC 沙盒孤儿 Bundle。</summary>
        public bool EnableSandboxGcAfterUpdate = true;

        /// <summary>
        /// 可选代码热更钩子。null = 跳过（不装 HybridCLR 也能跑）。
        /// 接入后由游戏层赋值，Resource 无需再改。
        /// </summary>
        [NonSerialized]
        public ICodeHotUpdateHook CodeHotUpdateHook;

        /// <summary>可选自定义解密器；空则按清单 EncryptMode 自动选择。</summary>
        [NonSerialized]
        public IDecrypter Decrypter;

        public static ZeonAssetConfig CreateDefault() => new ZeonAssetConfig();

        /// <summary>解析后的下载策略（Auto 已展开为 Full / OnDemand）。</summary>
        public EBundleDownloadPolicy ResolveDownloadPolicy()
        {
            if (DownloadPolicy == EBundleDownloadPolicy.Full)
                return EBundleDownloadPolicy.Full;
            if (DownloadPolicy == EBundleDownloadPolicy.OnDemand)
                return EBundleDownloadPolicy.OnDemand;
            return ZeonAssetPathHelper.CanUsePersistentFileCache()
                ? EBundleDownloadPolicy.Full
                : EBundleDownloadPolicy.OnDemand;
        }

        public bool IsOnDemandDownload => ResolveDownloadPolicy() == EBundleDownloadPolicy.OnDemand;

        /// <summary>按需加载时必须允许边玩边下，否则缺包会直接失败。</summary>
        public bool ShouldPlayWhileDownload() => EnablePlayWhileDownload || IsOnDemandDownload;

        public IDecrypter ResolveDecrypter(PackageBundle bundle)
        {
            if (Decrypter != null)
                return Decrypter;
            if (bundle == null)
                return NullDecrypter.Instance;

            switch (bundle.EncryptMode)
            {
                case EBundleEncryptMode.Offset:
                    return new OffsetDecrypter(Math.Max(0, bundle.LoadOffset));
                case EBundleEncryptMode.Xor:
                    return new XorDecrypter(XorDecryptKey);
                default:
                    return NullDecrypter.Instance;
            }
        }

        public string ResolveAppVersion()
        {
            return string.IsNullOrEmpty(AppVersion) ? UnityEngine.Application.version : AppVersion;
        }

        public string ResolvePlatform()
        {
            return string.IsNullOrWhiteSpace(Platform)
                ? UnityEngine.Application.platform.ToString()
                : Platform.Trim();
        }

        /// <summary>按优先级去重后的调度地址：VersionCheckUrls → VersionCheckUrl → BackupVersionCheckUrl。</summary>
        public string[] CollectVersionCheckUrls()
        {
            var list = new List<string>();
            if (VersionCheckUrls != null)
            {
                for (int i = 0; i < VersionCheckUrls.Length; i++)
                    AddVersionCheckUrl(list, VersionCheckUrls[i]);
            }

            AddVersionCheckUrl(list, VersionCheckUrl);
            AddVersionCheckUrl(list, BackupVersionCheckUrl);
            return list.ToArray();
        }

        private static void AddVersionCheckUrl(List<string> list, string url)
        {
            url = (url ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url))
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], url, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add(url);
        }
    }

    public enum EPlayMode
    {
        EditorSimulate = 0,
        /// <summary>真机：沙盒 + StreamingAssets 回退。HostBoot 走 VersionCheck，CDN 以接口 cdn_host 为准。</summary>
        HostPlay = 2,
    }

    /// <summary>开局是否把清单 Diff 出的脏 Bundle 全部下载。</summary>
    public enum EBundleDownloadPolicy
    {
        /// <summary>WebGL → OnDemand，其它平台 → Full。</summary>
        Auto = 0,
        /// <summary>开局一次下完所有脏包再进游戏。</summary>
        Full = 1,
        /// <summary>只提交清单；缺包时 PlayWhileDownload / CreateDownloaderByTags 再下。</summary>
        OnDemand = 2,
    }
}
