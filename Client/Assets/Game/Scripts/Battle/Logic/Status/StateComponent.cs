using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>实体状态：标记管「能不能做」，行为态管「正在做什么」。</summary>
    public class StateComponent : Component
    {
        #region FSM 状态实例
        private readonly EntityStateMachine _machine = new EntityStateMachine();

        private readonly IdleState _idle = new IdleState();
        private readonly MoveState _move = new MoveState();
        private readonly SkillState _skill = new SkillState();
        private readonly StunState _stun = new StunState();

        private IEntityFsmState _current;
        private bool _subscribed;
        #endregion

        #region 状态标记（叠了几层）
        /// <summary>当前生效的状态标记。</summary>
        public EntityStateTag CurrentStateMask => _machine.CurrentStateMask;

        /// <summary>某个标记加上或去掉时通知（true = 加上）。</summary>
        public event System.Action<EntityStateTag, bool> OnStateTagChanged
        {
            add => _machine.OnStateTagChanged += value;
            remove => _machine.OnStateTagChanged -= value;
        }

        public bool CanMove => _machine.CanMove;
        public bool CanAttack => _machine.CanAttack;
        public float MoveSpeedMultiplier => _machine.MoveSpeedMultiplier;

        public bool HasState(EntityStateTag tag) => _machine.HasState(tag);

        public bool CanCastSkill(bool isMagicSkill, bool allowsWhileRooted = false)
            => _machine.CanCastSkill(isMagicSkill, allowsWhileRooted);

        public bool AddState(EntityStateTag tag) => _machine.AddState(tag);

        public void RemoveState(EntityStateTag tag) => _machine.RemoveState(tag);

        /// <summary>添加一个到期后自动清除的限时状态（Stun/Root/Slow 等）。</summary>
        public bool AddTimedState(EntityStateTag tag, float duration, float magnitude = 0f)
            => _machine.AddTimedState(tag, Time.time, duration, magnitude);

        /// <summary>按 Buff 配置的状态位组合批量施加限时状态。</summary>
        public bool ApplyStatusFlags(int statusFlags, float duration, float slowMagnitude = 0f)
            => _machine.ApplyStatusFlags(statusFlags, Time.time, duration, slowMagnitude);

        /// <summary>清除所有控制类状态（Stun/Root/Silence 等），不影响其余 Tag。</summary>
        public void ClearControl() => _machine.ClearControl();

        public void ForceClearState(EntityStateTag tag) => _machine.ForceClearState(tag);

        public void ClearAll() => _machine.ClearAll();

        /// <summary>远端 Snapshot 控制位对齐（Root/Stun/Slow 等）。</summary>
        public void ReconcileControlMask(EntityStateTag authMask)
            => _machine.ReconcileControlMask(authMask);
        #endregion

        #region 行为 FSM：状态与属性
        /// <summary>本帧期望移动方向（由 Input 写入，世界水平向量）。</summary>
        public Vector3 MoveIntent { get; set; }

        public EEntityFsmState CurrentType => _current != null ? _current.Type : EEntityFsmState.Idle;

        public bool HasMoveIntent => MoveIntent.sqrMagnitude > 0.0001f;

        /// <summary>当前能不能位移。眩晕硬锁；引导/蓄力禁移由施法组件自己管。</summary>
        public bool AllowsLocomotion => CurrentType != EEntityFsmState.Stun;
        #endregion

        #region 生命周期
        public override void OnAwake()
        {
            _idle.Bind(this);
            _move.Bind(this);
            _skill.Bind(this);
            _stun.Bind(this);
            _current = _idle;
        }

        public override void OnStart()
        {
            if (!_subscribed)
            {
                _machine.OnStateTagChanged += HandleStateTagChanged;
                _subscribed = true;
            }
            _current.OnEnter();
        }

        public override void OnDestroy()
        {
            if (_subscribed)
            {
                _machine.OnStateTagChanged -= HandleStateTagChanged;
                _subscribed = false;
            }
        }

        public override void OnUpdate(float dt)
        {
            _machine.Tick(Time.time);

            // Stun / 击飞：强制进 Stun 行为态（打断技能、禁移）
            if (HasState(EntityStateTag.Stun | EntityStateTag.Airborne))
            {
                if (CurrentType != EEntityFsmState.Stun)
                    ChangeState(EEntityFsmState.Stun);
            }
            else if (CurrentType == EEntityFsmState.Idle || CurrentType == EEntityFsmState.Move)
            {
                ResolveLocomotion();
            }

            _current?.OnUpdate(dt);
        }
        #endregion

        #region FSM 切换与转移逻辑
        /// <summary>检查通过后切入技能态并开时间轴。</summary>
        public bool TryEnterSkill(
            int skillId,
            Vector3 dir,
            bool allowLogical,
            CastPresentationTarget presentationTarget = default)
        {
            if (CurrentType == EEntityFsmState.Stun)
                return false;

            if (HasState(EntityStateTag.Airborne))
                return false;

            _skill.Configure(skillId, dir, allowLogical, presentationTarget);

            // 连招 / 取消打断：已在 Skill 态时强制重进，重启 Timeline
            if (CurrentType == EEntityFsmState.Skill)
            {
                _skill.OnExit();
                _skill.OnEnter();
                return true;
            }

            ChangeState(EEntityFsmState.Skill);
            return CurrentType == EEntityFsmState.Skill;
        }

        /// <summary>切换到目标行为态：先退出当前态，再进入目标态；目标不变或无效时忽略。</summary>
        public void ChangeState(EEntityFsmState next)
        {
            IEntityFsmState target = ResolveState(next);
            if (target == null || target == _current) return;

            _current?.OnExit();
            _current = target;
            _current.OnEnter();
        }

        /// <summary>按移动意图在 Idle/Move 间切换；Skill 结束 / Stun 解除时也走这里。</summary>
        public void ResolveLocomotion()
        {
            if (HasState(EntityStateTag.Stun))
            {
                ChangeState(EEntityFsmState.Stun);
                return;
            }

            // 离开 Skill 时 Casting 仍在，直到 OnExit；这里只看 Root/Stun/击飞
            bool hardLocked = HasState(EntityStateTag.Root | EntityStateTag.Stun | EntityStateTag.Airborne);
            if (hardLocked || !HasMoveIntent)
                ChangeState(EEntityFsmState.Idle);
            else
                ChangeState(EEntityFsmState.Move);
        }

        /// <summary>行为类型 -> 对应的 FSM 状态实例映射。</summary>
        private IEntityFsmState ResolveState(EEntityFsmState type) => type switch
        {
            EEntityFsmState.Idle => _idle,
            EEntityFsmState.Move => _move,
            EEntityFsmState.Skill => _skill,
            EEntityFsmState.Stun => _stun,
            _ => _idle
        };

        /// <summary>Tag 门禁广播的回调：Stun/Airborne 变化时强制切入/退出 Stun 行为态。</summary>
        private void HandleStateTagChanged(EntityStateTag tag, bool added)
        {
            if ((tag & (EntityStateTag.Stun | EntityStateTag.Airborne)) == 0) return;

            if (added)
                ChangeState(EEntityFsmState.Stun);
            else if (CurrentType == EEntityFsmState.Stun
                     && !HasState(EntityStateTag.Stun | EntityStateTag.Airborne))
                ResolveLocomotion();
        }
        #endregion
    }
}
