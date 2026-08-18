using System.Collections.Generic;

namespace Shared
{
    /// <summary>从哪读配置文本。路径用正斜杠，相对配置根目录。</summary>
    public interface IConfigSource
    {
        /// <summary>这个配置文件在不在。</summary>
        bool Exists(string relativePath);

        /// <summary>读一份配置文本。没有文件就失败。</summary>
        bool TryReadText(string relativePath, out string text);

        /// <summary>列出目录里匹配的文件（不含子目录）。目录不存在返回空。</summary>
        IReadOnlyList<string> ListFiles(string relativeDir, string searchPattern);
    }

    /// <summary>拼配置相对路径，一律用正斜杠。</summary>
    public static class ConfigPath
    {
        public static string Combine(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return "";

            var chunks = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = Normalize(parts[i]);
                if (string.IsNullOrEmpty(p))
                    continue;
                chunks.Add(p);
            }

            return string.Join("/", chunks.ToArray());
        }

        public static string FileName(string relativePath)
        {
            string p = Normalize(relativePath);
            if (string.IsNullOrEmpty(p))
                return "";
            int slash = p.LastIndexOf('/');
            return slash >= 0 ? p.Substring(slash + 1) : p;
        }

        /// <summary>反斜杠改成正斜杠，去掉首尾斜杠。</summary>
        public static string Normalize(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return "";
            return relativePath.Replace('\\', '/').Trim('/');
        }
    }
}
