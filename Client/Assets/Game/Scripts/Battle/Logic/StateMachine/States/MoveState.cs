namespace Client.Battle
{
    /// <summary>移动：有位移意图时的行为态。</summary>
    public sealed class MoveState : EntityFsmStateBase
    {
        public override EEntityFsmState Type => EEntityFsmState.Move;

        public override void OnEnter()
        {
            AnimComp?.PlayAnimation(AnimationComponent.EAnimState.Move);
        }

        public override void OnUpdate(float dt)
        {
            if (!Machine.HasMoveIntent)
            {
                Machine.ChangeState(EEntityFsmState.Idle);
                return;
            }

            // 受击等动作打断后，确保重新切入 Walk（OnEnter 不会再跑）
            if (AnimComp != null && AnimComp.CurrentState != AnimationComponent.EAnimState.Move)
                AnimComp.PlayAnimation(AnimationComponent.EAnimState.Move);
        }
    }
}
