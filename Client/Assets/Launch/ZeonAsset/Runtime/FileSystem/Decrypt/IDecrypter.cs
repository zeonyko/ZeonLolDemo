using System.IO;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 运行时解密接口（阶段1管线有加密节点预留；此处提供运行时对接点）。
    /// </summary>
    public interface IDecrypter
    {
        /// <summary>对原始 Bundle 字节解密。返回解密后数据。</summary>
        byte[] Decrypt(byte[] rawBytes, string bundleName);
    }

    /// <summary>
    /// 空解密（透传）。
    /// </summary>
    public sealed class NullDecrypter : IDecrypter
    {
        public static readonly NullDecrypter Instance = new NullDecrypter();

        public byte[] Decrypt(byte[] rawBytes, string bundleName) => rawBytes;
    }

    /// <summary>
    /// 简单 XOR 解密示例（与编辑器 TaskEncryption 对称时可对接）。
    /// </summary>
    public sealed class XorDecrypter : IDecrypter
    {
        private readonly byte _key;

        public XorDecrypter(byte key = 0x5A)
        {
            _key = key;
        }

        public byte[] Decrypt(byte[] rawBytes, string bundleName)
        {
            if (rawBytes == null || rawBytes.Length == 0)
                return rawBytes;

            var result = new byte[rawBytes.Length];
            for (int i = 0; i < rawBytes.Length; i++)
                result[i] = (byte)(rawBytes[i] ^ _key);
            return result;
        }
    }

    /// <summary>
    /// 文件头偏移解密：跳过前 N 字节（对应打包时插入的垃圾头）。
    /// </summary>
    public sealed class OffsetDecrypter : IDecrypter
    {
        private readonly int _offset;

        public OffsetDecrypter(int offset = 32)
        {
            _offset = offset < 0 ? 0 : offset;
        }

        public byte[] Decrypt(byte[] rawBytes, string bundleName)
        {
            if (rawBytes == null || rawBytes.Length <= _offset)
                return rawBytes;

            var result = new byte[rawBytes.Length - _offset];
            System.Buffer.BlockCopy(rawBytes, _offset, result, 0, result.Length);
            return result;
        }
    }
}
