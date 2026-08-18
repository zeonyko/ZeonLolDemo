using System;
using System.Collections.Generic;
using System.IO;

namespace Shared
{
    /// <summary>从指定文件夹读 JSON。权威源在仓库 Config/，运行时读各自同步产物。</summary>
    public sealed class FileConfigSource : IConfigSource
    {
        /// <summary>配置所在文件夹的绝对路径。</summary>
        public string RootDirectory { get; }

        public FileConfigSource(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("rootDirectory is empty", nameof(rootDirectory));
            RootDirectory = Path.GetFullPath(rootDirectory);
        }

        public bool Exists(string relativePath)
        {
            string abs = ToAbsolute(relativePath);
            return !string.IsNullOrEmpty(abs) && File.Exists(abs);
        }

        public bool TryReadText(string relativePath, out string text)
        {
            text = null;
            string abs = ToAbsolute(relativePath);
            if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                return false;

            text = File.ReadAllText(abs);
            return true;
        }

        public IReadOnlyList<string> ListFiles(string relativeDir, string searchPattern)
        {
            string absDir = ToAbsolute(relativeDir);
            if (string.IsNullOrEmpty(absDir) || !Directory.Exists(absDir))
                return Array.Empty<string>();

            string pattern = string.IsNullOrEmpty(searchPattern) ? "*.json" : searchPattern;
            string[] files = Directory.GetFiles(absDir, pattern, SearchOption.TopDirectoryOnly);
            var list = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                string rel = ToRelative(files[i]);
                if (!string.IsNullOrEmpty(rel))
                    list.Add(rel);
            }

            return list;
        }

        /// <summary>在候选路径里找第一个真实存在的文件夹。</summary>
        public static string ResolveExistingDirectory(params string[] absoluteOrRelativeCandidates)
        {
            if (absoluteOrRelativeCandidates == null)
                return null;

            for (int i = 0; i < absoluteOrRelativeCandidates.Length; i++)
            {
                string c = absoluteOrRelativeCandidates[i];
                if (string.IsNullOrEmpty(c))
                    continue;
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full))
                    return full;
            }

            return null;
        }

        public override string ToString() => RootDirectory;

        string ToAbsolute(string relativePath)
        {
            string rel = ConfigPath.Normalize(relativePath);
            if (string.IsNullOrEmpty(rel))
                return RootDirectory;
            return Path.GetFullPath(Path.Combine(RootDirectory, rel.Replace('/', Path.DirectorySeparatorChar)));
        }

        string ToRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return "";

            string full = Path.GetFullPath(absolutePath);
            string root = RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return ConfigPath.Normalize(full.Substring(root.Length));
            return ConfigPath.Normalize(Path.GetFileName(full));
        }
    }
}
