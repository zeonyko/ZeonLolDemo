using System;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 清单中的单个 Asset 元数据。
    /// </summary>
    [Serializable]
    public class PackageAsset
    {
        /// <summary>业务寻址地址（默认等于 AssetPath）。</summary>
        public string Address;

        /// <summary>工程内资源路径，例如 Assets/Game/Prefabs/Hero.prefab。</summary>
        public string AssetPath;

        /// <summary>所属 Bundle 在 PackageManifest.Bundles 中的索引。</summary>
        public int BundleID;

        /// <summary>
        /// 原生文件：Bundle 磁盘文件本身即为内容（非 Unity Object）。
        /// 原生文件：Bundle 磁盘文件本身即为内容（非 Unity Object）。
        /// DLL.bytes / 外部二进制可设为 true；否则按 TextAsset 从 AB 读取。
        /// </summary>
        public bool IsRawFile;

        public PackageAsset()
        {
        }

        public PackageAsset(string address, string assetPath, int bundleId, bool isRawFile = false)
        {
            Address = address;
            AssetPath = assetPath;
            BundleID = bundleId;
            IsRawFile = isRawFile;
        }
    }
}
