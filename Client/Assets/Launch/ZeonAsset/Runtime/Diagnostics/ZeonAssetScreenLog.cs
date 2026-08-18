using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 真机看日志：供 GM 面板展示 + persistentDataPath/zeon.log。
    /// </summary>
    public static class ZeonAssetScreenLog
    {
        public const int MaxLines = 240;
        public static event Action Changed;

        private static readonly ConcurrentQueue<string> Incoming = new ConcurrentQueue<string>();
        private static readonly List<string> Lines = new List<string>(MaxLines + 8);
        private static readonly StringBuilder Builder = new StringBuilder(24 * 1024);
        private static bool _hooked;
        private static string _filePath;
        private static string _cachedText = string.Empty;
        private static bool _dirty = true;

        public static string FilePath
        {
            get
            {
                Ensure();
                return _filePath;
            }
        }

        public static int LineCount
        {
            get
            {
                Pump();
                return Lines.Count;
            }
        }

        public static IReadOnlyList<string> GetLines()
        {
            Pump();
            return Lines;
        }

        public static string Text
        {
            get
            {
                Pump();
                if (!_dirty)
                    return _cachedText;
                Builder.Length = 0;
                for (int i = 0; i < Lines.Count; i++)
                {
                    if (i > 0)
                        Builder.Append('\n');
                    Builder.Append(Lines[i]);
                }
                _cachedText = Builder.ToString();
                _dirty = false;
                return _cachedText;
            }
        }

        public static void Ensure()
        {
            if (_hooked)
                return;
            _hooked = true;
            _filePath = Path.Combine(Application.persistentDataPath, "zeon.log");
            try
            {
                File.WriteAllText(_filePath, "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n");
            }
            catch
            {
                // ignored
            }

            // Development Build 自带左侧控制台又窄又难滚，统一走 GM「日志」页。
            try
            {
                Debug.developerConsoleVisible = false;
            }
            catch
            {
                // ignored
            }

            Incoming.Enqueue(Stamp() + "[Launch] log file: " + _filePath);
            Application.logMessageReceivedThreaded += OnLog;
        }

        public static void Clear()
        {
            Ensure();
            while (Incoming.TryDequeue(out _))
            {
            }

            Lines.Clear();
            _dirty = true;
            _cachedText = string.Empty;
            try
            {
                File.WriteAllText(_filePath, "=== cleared " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n");
            }
            catch
            {
                // ignored
            }

            Changed?.Invoke();
        }

        public static void Pump()
        {
            Ensure();
            var added = new List<string>();
            while (Incoming.TryDequeue(out var line))
            {
                Lines.Add(line);
                added.Add(line);
            }

            if (Lines.Count > MaxLines)
                Lines.RemoveRange(0, Lines.Count - MaxLines);

            if (added.Count == 0)
                return;

            _dirty = true;
            AppendFile(added);
            Changed?.Invoke();
        }

        /// <summary>压制 Unity 开发者控制台，避免和 GM 日志抢屏。</summary>
        public static void SuppressUnityConsole()
        {
            try
            {
                if (Debug.developerConsoleVisible)
                    Debug.developerConsoleVisible = false;
            }
            catch
            {
                // ignored
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!ShouldKeep(condition, type))
                return;

            var stamp = Stamp();
            Incoming.Enqueue(stamp + Prefix(type) + condition);
            if ((type == LogType.Error || type == LogType.Exception) &&
                !string.IsNullOrEmpty(stackTrace))
            {
                var lines = stackTrace.Split('\n');
                for (int i = 0; i < lines.Length && i < 12; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    Incoming.Enqueue(stamp + "  " + line);
                }
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff") + " ";
        }

        private static bool ShouldKeep(string condition, LogType type)
        {
            if (type != LogType.Log)
                return true;
            if (string.IsNullOrEmpty(condition))
                return false;
            return condition.IndexOf("[ZeonAsset]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[Launch]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[Game]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[GameEntry]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[GameConfig]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[EntityFactory]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[UnitViewCatalog]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[Battle]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[ConfigService]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("[BattleCatalog]", StringComparison.Ordinal) >= 0 ||
                   condition.IndexOf("HybridCLR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Prefix(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "[E] ";
                case LogType.Warning:
                    return "[W] ";
                default:
                    return "";
            }
        }

        private static void AppendFile(List<string> added)
        {
            if (string.IsNullOrEmpty(_filePath) || added == null || added.Count == 0)
                return;
            try
            {
                File.AppendAllText(_filePath, string.Join("\n", added.ToArray()) + "\n");
            }
            catch
            {
                // ignored
            }
        }
    }
}
