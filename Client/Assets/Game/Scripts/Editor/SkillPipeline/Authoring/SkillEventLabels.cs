using System.Collections.Generic;

namespace Client.SkillAuthoring
{
    /// <summary>时间轴中文显示名；导出键仍是英文。</summary>
    public static class SkillEventLabels
    {
        private static readonly Dictionary<string, string> KeyZh = new Dictionary<string, string>
        {
            { "Windup", "前摇段" },
            { "Hit", "判定" },
            { "Recovery", "后摇段" },
            { "End", "结束" },
            { "ChannelStart", "引导开始" },
            { "ChannelEnd", "引导结束" },
            { "ChargeStart", "蓄力开始" },
            { "ChargeRelease", "蓄力释放" },
            { "AcceptInput", "可接下一技能" },
            { "AllowCancel", "可取消" },
            { "CommitBlink", "闪现位移" },
            { "CommitDash", "冲刺位移" },
            { "CommitJump", "起跳" },
            { "Play", "播放" },
            { "HitStop", "卡肉" },
        };

        public static string KeyLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return KeyZh.TryGetValue(key, out var zh) ? $"{zh} ({key})" : key;
        }

        public static string ClipTitle(string key, string param)
        {
            if (!string.IsNullOrEmpty(param)) return param;
            if (string.IsNullOrEmpty(key)) return "";
            return KeyZh.TryGetValue(key, out var zh) ? $"{zh} ({key})" : key;
        }

        public static string[] PopupLabels(string[] englishKeys)
        {
            if (englishKeys == null || englishKeys.Length == 0)
                return System.Array.Empty<string>();
            var labels = new string[englishKeys.Length];
            for (int i = 0; i < englishKeys.Length; i++)
                labels[i] = KeyLabel(englishKeys[i]);
            return labels;
        }
    }
}
