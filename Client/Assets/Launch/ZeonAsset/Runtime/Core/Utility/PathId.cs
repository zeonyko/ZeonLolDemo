using System;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 稳定路径哈希（PathID）。运行期用 long 做字典 Key，避免反复字符串比较/拼接产生 GC。
    /// </summary>
    public static class PathId
    {
        public static long Get(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            // FNV-1a 64-bit
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < path.Length; i++)
                {
                    char c = path[i];
                    if (c == '\\')
                        c = '/';
                    // 忽略大小写，保证 Windows 路径一致
                    if (c >= 'A' && c <= 'Z')
                        c = (char)(c + 32);

                    hash ^= c;
                    hash *= 1099511628211UL;
                }

                return (long)hash;
            }
        }
    }
}
