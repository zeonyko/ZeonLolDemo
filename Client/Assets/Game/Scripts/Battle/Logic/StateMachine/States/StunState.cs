using Shared;

namespace Client.Battle
{
    /// <summary>受控：打断技能，不能移动/放技能。能不能做由状态标记叠层保证。</summary>
    public sealed class StunState : EntityFsmStateBase
    {
        public override EEntityFsmState Type => EEntityFsmState.Stun;

        public override void OnEnter()
        {
            // 只负责打断施法；眩晕姿态/特效由 StatusFxComponent 随 Tag 驱动
            SkillComp?.AbortCast();
        }

        public override void OnUpdate(float dt)
        {
            if (!Machine.HasState(EntityStateTag.Stun | EntityStateTag.Airborne))
                Machine.ResolveLocomotion();
        }
    }
}
