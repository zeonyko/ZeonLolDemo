namespace Client.Battle
{
    /// <summary>正在做什么：待机、移动、技能、眩晕（互斥）。</summary>
    public enum EEntityFsmState
    {
        Idle = 0,
        Move = 1,
        Skill = 2,
        Stun = 3,
    }
}
