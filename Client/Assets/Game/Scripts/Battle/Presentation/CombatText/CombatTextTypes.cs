using System;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>飘字分类：决定颜色、字号、时长。</summary>
    public enum CombatTextKind
    {
        Physical = 0,
        Magical = 1,
        True = 2,
        Critical = 3,
        Heal = 4,
        Status = 5,
        Miss = 6,
    }

    /// <summary>单个 Kind 对应的飘字样式配置（颜色/字号/动画参数/前后缀）。</summary>
    [Serializable]
    public class CombatTextStyleEntry
    {
        public string Kind = "Physical";
        public float ColorR = 1f, ColorG = 0.82f, ColorB = 0.29f, ColorA = 1f;
        public float OutlineR, OutlineG, OutlineB, OutlineA = 0.9f;
        public float CharacterSize = 0.062f;
        public float ScalePeak = 1.18f;
        public float Lifetime = 0.52f;
        public float ArcStrength = 0.28f;
        public float PopDuration = 0.07f;
        public string Prefix = "";
        public string Suffix = "!";

        public Color FillColor => new Color(ColorR, ColorG, ColorB, ColorA);
        public Color OutlineColor => new Color(OutlineR, OutlineG, OutlineB, OutlineA);

        public CombatTextKind ResolveKind()
        {
            if (Enum.TryParse(Kind, true, out CombatTextKind k))
                return k;
            return CombatTextKind.Physical;
        }
    }

    /// <summary>飘字样式表文件：池参数 + 各分类样式。</summary>
    [Serializable]
    public class CombatTextStylesFile
    {
        public int Version = 1;
        public int PoolPrewarm = 32;
        public int PoolMax = 64;
        public float StackOffsetY = 0.22f;
        public float DefaultLifetime = 0.62f;
        public CombatTextStyleEntry[] Styles = Array.Empty<CombatTextStyleEntry>();
    }

    /// <summary>字体配置：优先加载工程字体；空或失败则走系统字体，再退到 Arial。</summary>
    [Serializable]
    public class CombatTextFontConfigFile
    {
        public int Version = 1;
        public string PreferredFontResource = "";
        public string[] OsFontFallbacks =
        {
            "Segoe UI", "Microsoft YaHei", "Arial", "Helvetica"
        };
        public int FontSize = 48;
        public string MaterialResource = "Materials/mat_combat_text_outline";
        public float OutlineWidth = 0.045f;
    }
}
