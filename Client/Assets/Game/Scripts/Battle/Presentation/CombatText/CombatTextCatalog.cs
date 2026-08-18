using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>加载飘字和字体配置。</summary>
    public static class CombatTextCatalog
    {
        #region 常量与缓存状态
        const string StylesRelative = "Presentation/CombatTextStyles.json";
        const string FontRelative = "Presentation/FontConfig.json";

        static bool _loaded;
        static CombatTextStylesFile _styles;
        static CombatTextFontConfigFile _font;
        static readonly Dictionary<CombatTextKind, CombatTextStyleEntry> Map =
            new Dictionary<CombatTextKind, CombatTextStyleEntry>(8);
        #endregion

        #region 公开访问入口
        public static CombatTextStylesFile Styles
        {
            get
            {
                EnsureLoaded();
                return _styles;
            }
        }

        public static CombatTextFontConfigFile FontConfig
        {
            get
            {
                EnsureLoaded();
                return _font;
            }
        }

        #endregion

        #region 加载与刷新
        /// <summary>首次访问时加载配置并建立 Kind → 样式的映射表。</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _styles = LoadStyles();
            _font = LoadFont();
            Map.Clear();
            if (_styles?.Styles != null)
            {
                for (int i = 0; i < _styles.Styles.Length; i++)
                {
                    var e = _styles.Styles[i];
                    if (e == null) continue;
                    Map[e.ResolveKind()] = e;
                }
            }
        }

        /// <summary>强制重新加载（每次开战调用一次，便于热调数值）。</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
        #endregion

        #region 样式查询
        /// <summary>按 Kind 取样式；未配置时回退 Physical，再兜底内置默认。</summary>
        public static CombatTextStyleEntry GetStyle(CombatTextKind kind)
        {
            EnsureLoaded();
            if (Map.TryGetValue(kind, out var e) && e != null)
                return e;
            if (Map.TryGetValue(CombatTextKind.Physical, out e) && e != null)
                return e;
            return DefaultPhysical();
        }

        #endregion

        #region 文件加载
        /// <summary>从配置读取样式，失败或缺失时回退内置样式。</summary>
        static CombatTextStylesFile LoadStyles()
        {
            if (!ConfigService.IsReady || !ConfigService.Exists(StylesRelative))
                return BuiltInStyles();

            var file = ConfigService.LoadFile<CombatTextStylesFile>(StylesRelative);
            return file != null ? file : BuiltInStyles();
        }

        /// <summary>从配置读取字体，失败或缺失时使用默认值。</summary>
        static CombatTextFontConfigFile LoadFont()
        {
            if (!ConfigService.IsReady || !ConfigService.Exists(FontRelative))
                return new CombatTextFontConfigFile();

            var file = ConfigService.LoadFile<CombatTextFontConfigFile>(FontRelative);
            return file != null ? file : new CombatTextFontConfigFile();
        }

        #endregion

        #region 内置默认样式
        /// <summary>无配置文件时的内置样式集合。</summary>
        static CombatTextStylesFile BuiltInStyles()
        {
            return new CombatTextStylesFile
            {
                Styles = new[]
                {
                    DefaultPhysical(),
                    Make(CombatTextKind.Magical, new Color(0.71f, 0.55f, 1f), 0.062f, 1.18f, "!", ""),
                    Make(CombatTextKind.Critical, new Color(1f, 0.23f, 0.23f), 0.09f, 1.38f, "!", "CRITICAL! "),
                    Make(CombatTextKind.Status, Color.white, 0.052f, 1.12f, "!", ""),
                    Make(CombatTextKind.Heal, new Color(0.36f, 1f, 0.54f), 0.062f, 1.15f, "", "+"),
                }
            };
        }

        static CombatTextStyleEntry DefaultPhysical() =>
            Make(CombatTextKind.Physical, new Color(1f, 0.82f, 0.29f), 0.062f, 1.18f, "!", "");

        /// <summary>按类型/颜色/字号等参数拼装一条样式条目。</summary>
        static CombatTextStyleEntry Make(
            CombatTextKind kind, Color c, float size, float peak, string suffix, string prefix)
        {
            return new CombatTextStyleEntry
            {
                Kind = kind.ToString(),
                ColorR = c.r, ColorG = c.g, ColorB = c.b, ColorA = c.a,
                OutlineA = 0.92f,
                CharacterSize = size,
                ScalePeak = peak,
                Lifetime = kind == CombatTextKind.Critical ? 0.72f : 0.58f,
                ArcStrength = kind == CombatTextKind.Critical ? 0.45f : 0.35f,
                PopDuration = 0.08f,
                Prefix = prefix,
                Suffix = suffix
            };
        }
        #endregion
    }
}
