namespace Client.Battle
{
    /// <summary>本局共享状态：自己是谁、是否开战、复活时间等。</summary>
    public class BattleSession
    {
        public long LocalPlayerId { get; set; }
        public string LocalPlayerName { get; set; } = "";
        public int LocalTeamId { get; set; }
        public bool MatchEnded { get; set; }
        /// <summary>是否已正式开战；大厅等待准备时为 false。</summary>
        public bool MatchPlaying { get; set; }
        public bool LocalPlayerDead { get; set; }
        public int WinnerTeamId { get; set; }
        public float MatchDuration { get; set; }
        /// <summary>本局已过时间（秒），给 HUD 和复活预估用。</summary>
        public float MatchElapsed { get; set; }
        /// <summary>预计复活完成的时刻（Time.time）。</summary>
        public float LocalRespawnEndsAt { get; set; } = -1f;
    }
}
