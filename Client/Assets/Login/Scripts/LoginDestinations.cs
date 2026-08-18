namespace Login
{
    /// <summary>
    /// 场景地址。登录后目标改 <see cref="Current"/>；Login / Game 互不引用。
    /// </summary>
    public static class LoginDestinations
    {
        public const string LoginScene = "Assets/Login/Scenes/Login.unity";
        public const string MainBattle = "Assets/Game/Scenes/MainBattle.unity";

        /// <summary>进入按钮目标。默认正式对局；换模块改这一行。</summary>
        public static string Current = MainBattle;

        /// <summary>进战斗前必须下齐的 ZeonAsset Tag（HostPlay）。</summary>
        public static readonly string[] BattleResourceTags = { "Game" };
    }
}
