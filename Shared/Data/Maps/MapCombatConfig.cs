using System;

namespace Shared
{
    /// <summary>这张图的战斗节奏：小兵移速、出兵间隔、塔攻范围。血量看单位表。</summary>
    [Serializable]
    public class MapCombatConfig
    {
        public string MapId = "HowlingAbyss";

        public float MinionMoveSpeed = 2.4f;
        /// <summary>小兵仇恨搜索范围。</summary>
        public float MinionAggroRange = 5.2f;
        public float TowerAttackRange = 9f;
        /// <summary>防御塔多久找一次人（秒）。</summary>
        public float TowerScanInterval = 0.15f;
        /// <summary>小兵波次间隔（秒）。</summary>
        public float MinionWaveInterval = 24f;
        /// <summary>同波次内小兵生成间隔（秒）。</summary>
        public float MinionSpawnInterval = 0.45f;
    }

    /// <summary>当前这张图的战斗节奏（小兵波次、塔攻距等）。</summary>
    public static class MapCombatCatalog
    {
        static MapCombatConfig _current = new MapCombatConfig();

        /// <summary>当前这张图用的配置。</summary>
        public static MapCombatConfig Current => _current;

        /// <summary>换上新配置。没有就用默认。</summary>
        public static void ClearAndLoad(MapCombatConfig cfg)
        {
            _current = cfg ?? new MapCombatConfig();
        }

        public static void ResetDefaults() => _current = new MapCombatConfig();
    }
}
