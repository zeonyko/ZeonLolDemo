using System.Text;
using UnityEngine;

namespace Game.ZeonAsset
{
    public enum EZeonAssetLogLevel
    {
        Off = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Verbose = 4,
    }

    /// <summary>
    /// 资源模块统一日志。Console 搜 <c>[ZeonAsset]</c>。
    /// 级别存 PlayerPrefs，编辑器菜单 <c>ZeonAsset / 日志级别</c> 可改。
    /// </summary>
    public static class ZeonAssetLog
    {
        public const string Tag = "[ZeonAsset] ";
        public const string PrefsKey = "ZeonAsset.LogLevel";

        private static bool _inited;
        private static EZeonAssetLogLevel _level;

        public static EZeonAssetLogLevel Level
        {
            get
            {
                EnsureInit();
                return _level;
            }
            set => SetLevel(value, persist: true);
        }

        public static void SetLevel(EZeonAssetLogLevel level, bool persist)
        {
            _inited = true;
            _level = level;
            if (persist)
                PlayerPrefs.SetInt(PrefsKey, (int)level);
        }

        public static void Error(string message)
        {
            if (Level < EZeonAssetLogLevel.Error || string.IsNullOrEmpty(message))
                return;
            Debug.LogError(Tag + message);
        }

        public static void Warn(string message)
        {
            if (Level < EZeonAssetLogLevel.Warn || string.IsNullOrEmpty(message))
                return;
            Debug.LogWarning(Tag + message);
        }

        public static void Info(string message)
        {
            if (Level < EZeonAssetLogLevel.Info || string.IsNullOrEmpty(message))
                return;
            Debug.Log(Tag + message);
        }

        public static void Verbose(string message)
        {
            if (Level < EZeonAssetLogLevel.Verbose || string.IsNullOrEmpty(message))
                return;
            Debug.Log(Tag + message);
        }

        internal static void OperationFail(
            AsyncOperationBase op,
            string error,
            AsyncOperationBase causedBy)
        {
            if (Level < EZeonAssetLogLevel.Error)
                return;

            var sb = new StringBuilder(256);
            sb.Append("FAIL ");
            sb.Append(op != null ? op.GetDiagnosticLabel() : "?");
            if (causedBy != null)
            {
                sb.Append(" ← ");
                sb.Append(causedBy.GetDiagnosticLabel());
            }

            sb.AppendLine();
            sb.Append("  err=").AppendLine(error ?? "");
            op?.AppendFailContext(sb);
            Error(sb.ToString().TrimEnd());
        }

        internal static void AppendKv(StringBuilder sb, string key, string value)
        {
            if (sb == null || string.IsNullOrEmpty(value))
                return;
            sb.Append("  ").Append(key).Append('=').AppendLine(value);
        }

        private static void EnsureInit()
        {
            if (_inited)
                return;
            _inited = true;
            var fallback =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                EZeonAssetLogLevel.Info;
#else
                EZeonAssetLogLevel.Error;
#endif
            _level = (EZeonAssetLogLevel)PlayerPrefs.GetInt(PrefsKey, (int)fallback);
        }
    }
}
