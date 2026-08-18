namespace Client.Battle
{
    /// <summary>空闲：可接移动 / 技能。</summary>
    public sealed class IdleState : EntityFsmStateBase
    {
        public override EEntityFsmState Type => EEntityFsmState.Idle;

        public override void OnEnter()
        {
            AnimComp?.PlayAnimation(AnimationComponent.EAnimState.Idle);
        }
    }
}
