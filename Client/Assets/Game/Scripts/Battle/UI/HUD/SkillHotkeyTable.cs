using System;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>技能热键表，从配置读取。</summary>
    public static class SkillHotkeyTable
    {
        #region 运行时数据结构
        /// <summary>解析后的技能热键条目（供 HUD 直接消费）。</summary>
        public readonly struct Entry
        {
            public readonly KeyCode Hotkey;
            public readonly int SkillId;
            public readonly string Label;
            public readonly float AnchorX;
            public readonly float AnchorY;
            public readonly float Size;
            public readonly Color IconTint;
            public readonly bool HasIconTint;

            public Entry(
                KeyCode hotkey, int skillId, string label, float x, float y, float size,
                Color iconTint, bool hasIconTint)
            {
                Hotkey = hotkey;
                SkillId = skillId;
                Label = label ?? "";
                AnchorX = x;
                AnchorY = y;
                Size = size;
                IconTint = iconTint;
                HasIconTint = hasIconTint;
            }

            public bool IsBound => SkillId > 0;
        }

        /// <summary>热键表文件根对象。</summary>
        [Serializable]
        private class HotkeyFile
        {
            public HotkeyDto[] Entries;
        }

        /// <summary>一条热键。Tint 填 -1 表示不改色。</summary>
        [Serializable]
        private class HotkeyDto
        {
            public string Hotkey;
            public int SkillId;
            public string Label;
            public float AnchorX;
            public float AnchorY;
            public float Size;
            public float TintR = -1f;
            public float TintG = -1f;
            public float TintB = -1f;
            public float TintA = -1f;
        }
        #endregion

        #region 数据加载
        private static Entry[] _all = Array.Empty<Entry>();

        public static Entry[] All => _all;

        public static void Load()
        {
            const string rel = "Input/SkillHotkeys.json";
            var file = ConfigService.LoadFile<HotkeyFile>(rel);
            if (file?.Entries == null || file.Entries.Length == 0)
            {
                Debug.LogError($"[SkillHotkey] 配置为空或不存在: {rel}");
                _all = Array.Empty<Entry>();
                return;
            }

            var list = new Entry[file.Entries.Length];
            for (int i = 0; i < file.Entries.Length; i++)
            {
                var dto = file.Entries[i];
                bool hasTint = dto.TintR >= 0f && dto.TintG >= 0f && dto.TintB >= 0f;
                Color tint = hasTint
                    ? new Color(dto.TintR, dto.TintG, dto.TintB, dto.TintA >= 0f ? dto.TintA : 0.92f)
                    : default;

                if (!Enum.TryParse(dto.Hotkey, ignoreCase: true, out KeyCode key))
                {
                    Debug.LogWarning($"[SkillHotkey] 无效热键 '{dto.Hotkey}'，跳过");
                    list[i] = new Entry(KeyCode.None, 0, dto.Label, dto.AnchorX, dto.AnchorY, dto.Size, tint, hasTint);
                    continue;
                }

                list[i] = new Entry(key, dto.SkillId, dto.Label, dto.AnchorX, dto.AnchorY, dto.Size, tint, hasTint);
            }

            _all = list;
            Debug.Log($"<color=cyan>[SkillHotkey] 已加载 {_all.Length} 项 ← {rel}</color>");
        }
        #endregion
    }
}
