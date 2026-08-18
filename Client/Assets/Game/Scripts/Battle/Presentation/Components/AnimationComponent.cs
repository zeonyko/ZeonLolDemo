using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>角色动画。施法按出手点调倍速，让高潮帧对齐算伤害的时刻。走路按移速调快慢，避免脚滑。</summary>
    public class AnimationComponent : Component
    {
        public enum EAnimState
        {
            Idle,
            Move,
            Attack1,
            Attack2,
            Cast,
            Hit,
            Die
        }

        #region 状态名常量
        public const string StateIdle = "Idle";
        public const string StateWalk = "Walk";
        public const string StateHit = "Hit";
        /// <summary>与 HeroAnimator 状态名一致（控制器里是 Die，不是 Death）。</summary>
        public const string StateDeath = "Die";
        public const string StateAttack1 = "Attack1";
        public const string StateAttack2 = "Attack2";
        public const string StateAttack3 = "Attack3";
        public const string StateAttack4 = "Attack4";

        /// <summary>动画原始时长上，出手/发射高潮帧的相对进度（0~1）。</summary>
        public const float DefaultCastPeakRatio = 0.6f;

        /// <summary>
        /// Mixamo Mutant Walking 约等于此世界移速（m/s）时脚步不滑。
        /// 逻辑 MoveSpeed / 此值 = Walk 播放倍速。
        /// </summary>
        public const float WalkClipReferenceSpeed = 1.85f;

        private const float MinAnimSpeed = 0.35f;
        private const float MaxAnimSpeed = 4.0f;
        #endregion

        #region 对外状态
        public EAnimState CurrentState { get; private set; } = EAnimState.Idle;
        public string CurrentStateName { get; private set; } = StateIdle;
        public float CurrentPlaybackSpeed { get; private set; } = 1f;

        public System.Action<EAnimState> OnStateChanged;
        #endregion

        #region 动作占用状态（攻击/施法/受击期间锁定 Locomotion 切换）
        /// <summary>一段"动作"（攻击/施法/受击/死亡）占用期间的互斥与保底超时信息。</summary>
        private sealed class ActionHoldState
        {
            public bool DieLocked;  // 死亡后动画锁死，除 Die 外一律拒绝
            public bool Holding;    // 当前是否处于攻击/施法/受击等动作占用中
            public float Failsafe;  // Holding 期间的保底超时（秒），防止卡死在动作态

            public void Reset()
            {
                DieLocked = false;
                Holding = false;
                Failsafe = 0f;
            }
        }

        private readonly ActionHoldState _hold = new ActionHoldState();
        #endregion

        #region Animator 绑定与播放倍速
        private Animator _animator;
        private bool _bound;
        private float _baseAnimatorSpeed = 1f;
        private float _buffSpeedMul = 1f;
        #endregion

        #region 绑定与基础播放
        public void BindAnimator()
        {
            _animator = null;
            _bound = false;
            _hold.Reset();
            CurrentPlaybackSpeed = 1f;

            var view = Owner?.GetComponent<ViewComponent>()?.ViewGameObject;
            if (view == null) return;

            _animator = view.GetComponentInChildren<Animator>(true);
            if (_animator == null) return;

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            _baseAnimatorSpeed = 1f;
            _animator.speed = _baseAnimatorSpeed;
            _bound = true;
            CrossFadeState(StateIdle, 0f);
            CurrentState = EAnimState.Idle;
            CurrentStateName = StateIdle;
        }

        public void PlayAnimation(EAnimState state)
        {
            CrossFade(state, 0.1f);
        }

        public void CrossFade(EAnimState state, float duration)
        {
            if (_hold.DieLocked && state != EAnimState.Die)
                return;

            if (IsLocomotion(state) && _hold.Holding && !CanLocomotionInterruptAction())
                return;

            EnsureAnimator();
            string stateName = ToStateName(state);
            if (string.IsNullOrEmpty(stateName)) return;
            ResetAnimatorSpeed();
            ApplyLogicalState(state, stateName, duration, holdDurationHint: -1f);
        }

        public void CrossFadeByName(string stateName, float duration = 0.1f)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            if (_hold.DieLocked && !IsDeathName(stateName)) return;

            string normalized = NormalizeStateName(stateName);
            if (string.IsNullOrEmpty(normalized)) return;

            var logical = ParseLogicalState(normalized);
            if (IsLocomotion(logical) && _hold.Holding && !CanLocomotionInterruptAction())
                return;

            EnsureAnimator();
            ResetAnimatorSpeed();
            ApplyLogicalState(logical, normalized, duration, holdDurationHint: -1f);
        }

        /// <summary>施法动画：按出手点算倍速，让高潮帧对齐配置时间。动画本地播完；弹道/伤害以服务器为准，允许稍微晚到。</summary>
        public void CrossFadeCastAligned(
            string stateName,
            float hitTime,
            int skillId,
            float fade = 0.08f,
            float peakRatio = DefaultCastPeakRatio)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            if (_hold.DieLocked) return;

            string normalized = NormalizeStateName(stateName);
            if (string.IsNullOrEmpty(normalized)) return;

            var logical = ParseLogicalState(normalized);
            EnsureAnimator();
            if (_animator == null)
            {
                ApplyLogicalState(logical, normalized, fade, holdDurationHint: -1f);
                return;
            }

            if (peakRatio <= 0.05f || peakRatio > 1f)
                peakRatio = DefaultCastPeakRatio;

            ResetAnimatorSpeed();
            float clipLen = ProbeStateLength(normalized);
            float speed = 1f;
            if (hitTime >= 0.03f && clipLen > 0.01f)
            {
                float peakTime = clipLen * peakRatio;
                speed = Mathf.Clamp(peakTime / hitTime, MinAnimSpeed, MaxAnimSpeed);
            }

            SetAnimatorSpeed(speed);
            float holdHint = clipLen / Mathf.Max(0.01f, speed) + 0.35f;
            ApplyLogicalState(logical, normalized, fade, holdDurationHint: holdHint);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[AnimAlign] skill={skillId} state={normalized} clip={clipLen:F2}s hit={hitTime:F2}s " +
                $"peakRatio={peakRatio:F2} speed={speed:F2}");
#endif
        }

        public void ClearDieLock()
        {
            _hold.DieLocked = false;
            _hold.Holding = false;
            ResetAnimatorSpeed();
            CrossFade(EAnimState.Idle, 0.1f);
        }

        /// <summary>技能态切出时松开动作占用，由 Idle/Move.OnEnter 切走路/待机。自己不播 Walk。</summary>
        public void ReleaseActionHold()
        {
            if (_hold.DieLocked) return;
            _hold.Holding = false;
            _hold.Failsafe = 0f;
        }
        #endregion

        #region 每帧驱动：动作占用倒计时 / Walk 倍速跟随
        public override void OnUpdate(float dt)
        {
            if (_hold.DieLocked) return;

            if (_hold.Holding)
            {
                // 受击可被走位打断。Reaction 若没归一到 Hit，也会落到 Cast 并锁很久。
                if (HasActiveMoveIntent() && CanLocomotionInterruptAction())
                {
                    _hold.Holding = false;
                    _hold.Failsafe = 0f;
                    ReturnToLocomotion(0.08f);
                    return;
                }

                _hold.Failsafe -= dt;
                if (IsActionClipFinished() || _hold.Failsafe <= 0f)
                {
                    _hold.Holding = false;
                    _hold.Failsafe = 0f;
                    ReturnToLocomotion(0.12f);
                }
                return;
            }

            if (CurrentState == EAnimState.Move)
                SyncWalkPlaybackSpeed();
        }

        /// <summary>按当前逻辑移速对齐 Walk 播放倍速（含 Slow 倍率）。</summary>
        private void SyncWalkPlaybackSpeed()
        {
            if (_animator == null) return;

            var state = Owner?.GetComponent<StateComponent>();
            float mul = state != null ? state.MoveSpeedMultiplier : 1f;
            float intent = 1f;
            if (state != null && state.HasMoveIntent)
                intent = Mathf.Clamp01(state.MoveIntent.magnitude);

            float worldSpeed = GameConstants.MoveSpeed * mul * intent;
            float refSpeed = WalkClipReferenceSpeed > 0.1f ? WalkClipReferenceSpeed : 1.85f;
            float speed = Mathf.Clamp(worldSpeed / refSpeed, MinAnimSpeed, MaxAnimSpeed);
            SetAnimatorSpeed(speed);
        }

        private void ApplyLogicalState(
            EAnimState logical, string stateName, float duration, float holdDurationHint)
        {
            if (logical == EAnimState.Die)
            {
                _hold.DieLocked = true;
                _hold.Holding = false;
                _hold.Failsafe = 0f;
                ResetAnimatorSpeed();
            }
            else if (IsActionState(logical))
            {
                _hold.Holding = true;
                if (logical == EAnimState.Hit)
                {
                    ResetAnimatorSpeed();
                    // 受击可被移动打断；站立时也只短锁，避免长时间卡在 Hit
                    _hold.Failsafe = 0.28f;
                }
                else
                {
                    _hold.Failsafe = holdDurationHint > 0.1f ? holdDurationHint : 2.8f;
                }
            }
            else
            {
                _hold.Holding = false;
                _hold.Failsafe = 0f;
                if (logical == EAnimState.Move)
                    SyncWalkPlaybackSpeed();
                else
                    ResetAnimatorSpeed();
            }

            CrossFadeState(stateName, Mathf.Max(0f, duration));
            CurrentState = logical;
            OnStateChanged?.Invoke(CurrentState);
        }
        #endregion

        #region 播放倍速与底层 Animator 操作
        /// <summary>在 1x 下探测状态原始时长（秒）。</summary>
        public float GetStateLength(string stateName)
        {
            EnsureAnimator();
            return ProbeStateLength(stateName);
        }

        /// <summary>在 1x 下探测状态原始时长（秒）。</summary>
        private float ProbeStateLength(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return 1f;

            float prevSpeed = _animator.speed;
            _animator.speed = 1f;
            _animator.Play(stateName, 0, 0f);
            _animator.Update(0f);

            float len = _animator.GetCurrentAnimatorStateInfo(0).length;
            if (len < 0.01f)
            {
                var clips = _animator.GetCurrentAnimatorClipInfo(0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                    len = clips[0].clip.length;
            }

            _animator.speed = prevSpeed;
            return len > 0.01f ? len : 1f;
        }

        public void SetBuffSpeedMultiplier(float mul)
        {
            _buffSpeedMul = mul > 0.05f ? mul : 1f;
            if (_bound && _animator != null && !_hold.Holding && !_hold.DieLocked)
                SetAnimatorSpeed(CurrentPlaybackSpeed);
        }

        private void SetAnimatorSpeed(float speed)
        {
            CurrentPlaybackSpeed = speed;
            if (_animator != null)
                _animator.speed = speed * _buffSpeedMul;
        }

        private void ResetAnimatorSpeed()
        {
            SetAnimatorSpeed(_baseAnimatorSpeed);
        }

        private bool IsActionClipFinished()
        {
            if (_animator == null) return true;
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (_animator.IsInTransition(0))
                return false;
            if (!info.IsName(CurrentStateName))
                return false;
            return info.normalizedTime >= 0.92f;
        }

        private void ReturnToLocomotion(float fade)
        {
            var state = Owner?.GetComponent<StateComponent>();
            if (state != null
                && state.CurrentType != EEntityFsmState.Idle
                && state.CurrentType != EEntityFsmState.Move)
                return;

            if (state != null && state.HasMoveIntent)
            {
                CrossFadeState(StateWalk, fade);
                CurrentState = EAnimState.Move;
                CurrentStateName = StateWalk;
                SyncWalkPlaybackSpeed();
                return;
            }

            ResetAnimatorSpeed();
            CrossFadeState(StateIdle, fade);
            CurrentState = EAnimState.Idle;
            CurrentStateName = StateIdle;
        }

        private void EnsureAnimator()
        {
            if (_bound && _animator != null) return;
            BindAnimator();
        }

        private void CrossFadeState(string stateName, float fadeDuration)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return;

            bool reenter = IsActionName(stateName);
            if (!reenter && CurrentStateName == stateName && fadeDuration > 0.0001f)
                return;

            CurrentStateName = stateName;
            _animator.CrossFade(stateName, fadeDuration, 0, 0f);
        }
        #endregion

        #region 状态名解析与逻辑状态映射
        private static bool IsLocomotion(EAnimState state)
            => state == EAnimState.Idle || state == EAnimState.Move;

        private static bool IsActionState(EAnimState state)
            => state is EAnimState.Attack1 or EAnimState.Attack2
                or EAnimState.Cast or EAnimState.Hit;

        /// <summary>受击不锁死位移表现；攻击/施法仍保持 actionHold。</summary>
        private bool CanLocomotionInterruptAction()
            => CurrentState == EAnimState.Hit
                || (Owner?.GetComponent<SkillCastComponent>()?.IsCasting != true
                    && IsInLocomotionFsm());

        bool IsInLocomotionFsm()
        {
            var fsm = Owner?.GetComponent<StateComponent>();
            return fsm != null
                && (fsm.CurrentType == EEntityFsmState.Idle || fsm.CurrentType == EEntityFsmState.Move);
        }

        private bool HasActiveMoveIntent()
        {
            var state = Owner?.GetComponent<StateComponent>();
            return state != null && state.HasMoveIntent;
        }

        private static bool IsActionName(string stateName)
            => stateName == StateAttack1 || stateName == StateAttack2
                || stateName == StateAttack3 || stateName == StateAttack4;

        private static string ToStateName(EAnimState state) => state switch
        {
            EAnimState.Idle => StateIdle,
            EAnimState.Move => StateWalk,
            EAnimState.Hit => StateHit,
            EAnimState.Die => StateDeath,
            EAnimState.Attack1 => StateAttack1,
            EAnimState.Attack2 => StateAttack2,
            EAnimState.Cast => StateAttack3,
            _ => StateIdle
        };

        private static bool IsDeathName(string name)
        {
            string n = NormalizeStateName(name);
            return string.Equals(n, StateDeath, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeStateName(string raw)
        {
            string s = raw ?? "";
            if (s.StartsWith("Anim_", System.StringComparison.OrdinalIgnoreCase))
                s = s.Substring(5);
            s = s.Trim();

            if (string.Equals(s, "Move", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Walk", System.StringComparison.OrdinalIgnoreCase))
                return StateWalk;
            if (string.Equals(s, "Die", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Death", System.StringComparison.OrdinalIgnoreCase))
                return StateDeath;
            if (string.Equals(s, "Idle", System.StringComparison.OrdinalIgnoreCase)) return StateIdle;
            if (string.Equals(s, "Hit", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "HitReact", System.StringComparison.OrdinalIgnoreCase))
                return StateHit;
            if (string.Equals(s, "Attack1", System.StringComparison.OrdinalIgnoreCase)) return StateAttack1;
            if (string.Equals(s, "Attack2", System.StringComparison.OrdinalIgnoreCase)) return StateAttack2;
            if (string.Equals(s, "Attack3", System.StringComparison.OrdinalIgnoreCase)) return StateAttack3;
            if (string.Equals(s, "Attack4", System.StringComparison.OrdinalIgnoreCase)) return StateAttack4;
            return s;
        }

        private static EAnimState ParseLogicalState(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return EAnimState.Attack1;
            if (normalized == StateIdle) return EAnimState.Idle;
            if (normalized == StateWalk) return EAnimState.Move;
            if (normalized == StateHit) return EAnimState.Hit;
            if (normalized == StateDeath) return EAnimState.Die;
            if (normalized == StateAttack1) return EAnimState.Attack1;
            if (normalized == StateAttack2) return EAnimState.Attack2;
            if (normalized == StateAttack3 || normalized == StateAttack4)
                return EAnimState.Cast;
            return EAnimState.Cast;
        }

        public static string DefaultAnimForSkill(int skillId) =>
            NormalizeStateName(SkillPresentationModules.ResolveDefaultAnimKey(skillId));
        #endregion
    }
}
