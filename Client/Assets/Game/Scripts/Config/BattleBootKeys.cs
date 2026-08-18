namespace Client.Battle
{
    /// <summary>
    /// 进战场前必须暖场的资源（Prefab 等）。
    /// 配置表不在此列：由 <see cref="GameConfig.LoadAllAsync"/> 装入 <c>Configs/**/*.json</c>。
    /// Key 与业务加载相同（<c>Assets/Game</c> 相对路径、无扩展名）。
    /// </summary>
    public static class BattleBootKeys
    {
        public static readonly string[] Warmup =
        {
            "Prefabs/UI/BattlePanel",
            "Prefabs/UI/MatchResultPanel",
            "Prefabs/Battle/BattlePlaceholder",
        };
    }
}
