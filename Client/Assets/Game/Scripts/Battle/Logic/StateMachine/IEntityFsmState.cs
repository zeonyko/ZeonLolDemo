namespace Client.Battle
{
    /// <summary>行为态接口：待机/移动/技能/眩晕互斥。</summary>
    public interface IEntityFsmState
    {
        EEntityFsmState Type { get; }

        void OnEnter();
        void OnExit();
        void OnUpdate(float dt);
    }
}
