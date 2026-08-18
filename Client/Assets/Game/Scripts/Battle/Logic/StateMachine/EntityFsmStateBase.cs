namespace Client.Battle
{
    /// <summary>行为态基类：记下所属状态组件，需要时再取常用组件。</summary>
    public abstract class EntityFsmStateBase : IEntityFsmState
    {
        protected StateComponent Machine { get; private set; }
        protected Entity Owner => Machine != null ? Machine.Owner : null;

        private AnimationComponent _animComp;
        private SkillCastComponent _skillComp;

        protected AnimationComponent AnimComp =>
            _animComp ??= Owner?.GetComponent<AnimationComponent>();
        protected SkillCastComponent SkillComp =>
            _skillComp ??= Owner?.GetComponent<SkillCastComponent>();

        public abstract EEntityFsmState Type { get; }

        internal void Bind(StateComponent machine)
        {
            Machine = machine;
            _animComp = null;
            _skillComp = null;
        }

        public virtual void OnEnter() { }
        public virtual void OnExit() { }
        public virtual void OnUpdate(float dt) { }
    }
}
