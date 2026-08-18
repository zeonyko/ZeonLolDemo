using System;
using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 不可靠持久化平台（如 WebGL）上的 Bundle 字节缓存。
    /// 下载成功后写入；加载时优先于本地路径。
    /// </summary>
    public static class BundleMemoryCache
    {
        private static readonly Dictionary<string, byte[]> Cache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static bool TryGet(string fileName, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(fileName))
                return false;
            return Cache.TryGetValue(PathFileName(fileName), out bytes) && bytes != null && bytes.Length > 0;
        }

        public static void Set(string fileName, byte[] bytes)
        {
            if (string.IsNullOrEmpty(fileName) || bytes == null || bytes.Length == 0)
                return;
            Cache[PathFileName(fileName)] = bytes;
        }

        public static bool Contains(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;
            return Cache.ContainsKey(PathFileName(fileName));
        }

        public static void Remove(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return;
            Cache.Remove(PathFileName(fileName));
        }

        public static void Clear() => Cache.Clear();

        public static int Count => Cache.Count;

        private static string PathFileName(string fileName)
        {
            var name = System.IO.Path.GetFileName(fileName);
            return string.IsNullOrEmpty(name) ? fileName : name;
        }
    }
}
