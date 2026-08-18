using System;
using System.IO;

namespace Game.ZeonAsset
{
    /// <summary>
    /// CRC32（IEEE），运行时下载校验与构建清单共用。
    /// </summary>
    public static class Crc32Utility
    {
        private const int BufferSize = 64 * 1024;
        private static readonly uint[] Table = CreateTable();

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            const uint poly = 0xEDB88320u;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (poly ^ (crc >> 1)) : (crc >> 1);
                table[i] = crc;
            }

            return table;
        }

        public static uint Compute(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return 0;

            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; i++)
                crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>分块计算文件 CRC，避免大文件一次读入内存。</summary>
        public static uint ComputeFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return 0;

            uint crc = 0xFFFFFFFFu;
            var buffer = new byte[BufferSize];
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                       FileOptions.SequentialScan))
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                        crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                }
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
