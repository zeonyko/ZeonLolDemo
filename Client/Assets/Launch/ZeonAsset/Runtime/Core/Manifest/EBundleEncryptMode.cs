namespace Game.ZeonAsset
{
    /// <summary>Bundle 加密模式（构建 TaskEncryption ↔ 运行时加载）。</summary>
    public enum EBundleEncryptMode
    {
        None = 0,
        /// <summary>文件头插入垃圾字节，加载时 LoadFromFile(path, offset)。</summary>
        Offset = 1,
        /// <summary>整文件 XOR，加载时解密进内存。</summary>
        Xor = 2,
    }
}
