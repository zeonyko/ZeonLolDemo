using System;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 可选的代码热更钩子。ZeonAsset 不引用任何热更方案。
    /// <see cref="ZeonAssetConfig.CodeHotUpdateHook"/> 为 null 时 HostBoot 跳过本步，资源模块照常运行。
    /// 接入 HybridCLR / 华佗时由游戏层实现并注入，不必改 ZeonAsset。
    /// </summary>
    public interface ICodeHotUpdateHook
    {
        /// <summary>
        /// 创建代码加载操作。返回 null 表示跳过。
        /// 典型实现：按 Tag 下载 → LoadRawFileAsync → 加载程序集。
        /// </summary>
        AsyncOperationBase CreateLoadOperation(IRawFileProvider rawFiles);
    }

    /// <summary>
    /// RawFile / Tag 下载能力（代码热更与业务分包共用）。
    /// </summary>
    public interface IRawFileProvider
    {
        LoadRawFileOperation LoadRawFileAsync(string location);
        ZeonAssetDownloaderOperation CreateDownloaderByTags(params string[] tags);
    }
}
