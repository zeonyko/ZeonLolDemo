using System;
using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 清单中的单个 Bundle 元数据。
    /// </summary>
    [Serializable]
    public class PackageBundle
    {
        /// <summary>逻辑 Bundle 名（不含扩展名）。</summary>
        public string BundleName;

        /// <summary>磁盘文件名（含 .bundle 等后缀）。CDN 布局下为 {Hash}.bundle。</summary>
        public string FileName;

        /// <summary>内容寻址 Hash（MD5 小写十六进制）。</summary>
        public string Hash;

        /// <summary>文件 CRC32，用于下载/缓存完整性校验。</summary>
        public uint CRC;

        /// <summary>文件字节大小。</summary>
        public long FileSize;

        /// <summary>业务标签，用于按 Tag 下载分组。</summary>
        public List<string> Tags = new List<string>();

        /// <summary>直接依赖的 Bundle 名列表。</summary>
        public List<string> DependBundles = new List<string>();

        /// <summary>加密类型（None / Offset / Xor）。</summary>
        public EBundleEncryptMode EncryptMode = EBundleEncryptMode.None;

        /// <summary>Offset 模式下 LoadFromFile 跳过的头字节数。</summary>
        public int LoadOffset;

        public PackageBundle()
        {
        }

        public PackageBundle(string bundleName)
        {
            BundleName = bundleName;
            FileName = bundleName + ".bundle";
        }
    }
}
