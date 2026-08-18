using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>读配置的总入口。给相对路径就行，不用管文件在磁盘哪。</summary>
    public static class ConfigService
    {
        #region --- 启动 / 关闭 ---

        /// <summary>当前从哪读配置。</summary>
        public static IConfigSource Source { get; private set; }

        /// <summary>当前用哪套 JSON 读写。</summary>
        public static IConfigJsonCodec Json { get; private set; }

        /// <summary>是否已经初始化好。</summary>
        public static bool IsReady => Source != null && Json != null;

        /// <summary>首次加载前先交给读文件方式和 JSON 读写。</summary>
        public static void Initialize(IConfigSource source, IConfigJsonCodec codec)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (codec == null)
                throw new ArgumentNullException(nameof(codec));

            Source = source;
            Json = codec;
            Log($"Initialize source={source}");
        }

        /// <summary>清掉读文件方式和 JSON 读写。</summary>
        public static void Shutdown()
        {
            Source = null;
            Json = null;
        }

        #endregion

        #region --- 文件在不在 ---

        /// <summary>这个配置文件在不在。</summary>
        public static bool Exists(params string[] relativeParts)
        {
            EnsureReady();
            return Source.Exists(ConfigPath.Combine(relativeParts));
        }

        #endregion

        #region --- 读文件 ---

        /// <summary>读一份配置。</summary>
        public static T LoadFile<T>(params string[] relativeParts) where T : class
        {
            return LoadFileByPath<T>(ConfigPath.Combine(relativeParts));
        }

        /// <summary>按路径读配置。没有或读失败返回空。</summary>
        public static T LoadFileByPath<T>(string relativePath) where T : class
        {
            EnsureReady();
            string rel = ConfigPath.Normalize(relativePath);
            if (string.IsNullOrEmpty(rel) || !Source.TryReadText(rel, out string text))
            {
                LogError($"文件不存在: {rel}");
                return null;
            }

            try
            {
                return Json.FromJson<T>(text);
            }
            catch (Exception ex)
            {
                LogError($"解析失败 {rel}: {ex.Message}");
                return null;
            }
        }

        /// <summary>读目录里匹配文件的原文，失败的跳过。</summary>
        public static List<(string relativePath, string text)> LoadTexts(
            string relativeDir, string searchPattern)
        {
            EnsureReady();
            var list = new List<(string relativePath, string text)>();
            IReadOnlyList<string> files = Source.ListFiles(relativeDir, searchPattern);
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i];
                if (Source.TryReadText(rel, out string text))
                    list.Add((rel, text));
            }

            return list;
        }

        /// <summary>读目录里匹配文件，并记下路径方便查错。</summary>
        public static List<(string path, T data)> LoadFilesWithPath<T>(
            string relativeDir, string searchPattern) where T : class
        {
            EnsureReady();
            var list = new List<(string path, T data)>();
            foreach (var (path, text) in LoadTexts(relativeDir, searchPattern))
            {
                try
                {
                    list.Add((path, Json.FromJson<T>(text)));
                }
                catch (Exception ex)
                {
                    LogError($"解析失败 {path}: {ex.Message}");
                    list.Add((path, null));
                }
            }

            return list;
        }

        #endregion

        #region --- 内部检查 ---

        private static void EnsureReady()
        {
            if (!IsReady)
                throw new InvalidOperationException("ConfigService 未 Initialize");
        }

        private static void Log(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log($"[ConfigService] {msg}");
#else
            Console.WriteLine($"[ConfigService] {msg}");
#endif
        }

        private static void LogError(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError($"[ConfigService] {msg}");
#else
            Console.WriteLine($"[ConfigService] ERROR {msg}");
#endif
        }

        #endregion
    }
}
