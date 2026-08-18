using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 内容寻址用 Hash（MD5 小写十六进制）。
    /// </summary>
    public static class HashUtility
    {
        private const int BufferSize = 64 * 1024;

        public static string ComputeFileMd5(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return string.Empty;

            using (var md5 = MD5.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                       FileOptions.SequentialScan))
            {
                return ToHex(md5.ComputeHash(stream));
            }
        }

        public static string ComputeBytesMd5(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            using (var md5 = MD5.Create())
                return ToHex(md5.ComputeHash(bytes));
        }

        public static string ComputeTextMd5(string text)
        {
            if (text == null)
                return string.Empty;
            return ComputeBytesMd5(Encoding.UTF8.GetBytes(text));
        }

        public static bool EqualsIgnoreCase(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
                return true;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
