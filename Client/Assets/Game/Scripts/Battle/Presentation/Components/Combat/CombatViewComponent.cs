using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗画面：受击闪白/朝向/抖动、击退偏移、攻击姿态、死亡。只改外观，不改逻辑位置。</summary>
    public class CombatViewComponent : Component
    {
        #region 依赖组件
        private ViewComponent _viewComp;
        private TransformComponent _transformComp;

        private ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        #endregion

        #region 时长/幅度常量
        private const float FlashDuration = 0.18f;
        private const float ShakeDuration = 0.22f;
        private const float ShakeAmplitude = 0.18f;
        private const float AttackPoseDuration = 0.16f;
        private const float DeathHoldAfterClip = 0.35f;
        private const float DeathDurationMin = 0.5f;
        private const float DeathDurationMax = 4f;
        private const float KnockDuration = 0.32f;
        private const float DefaultAirborneHeight = 2.2f;
        #endregion

        #region 原始缩放缓存（复活还原用）
        private sealed class ScaleCache
        {
            public Vector3 Value = Vector3.one;
            public bool Captured;
        }

        private readonly ScaleCache _scaleCache = new ScaleCache();
        #endregion

        #region 受击闪白 + 抖动
        private sealed class HitFeedbackState
        {
            public float FlashTimer;
            public float ShakeTimer;
            public Vector3 ShakeDir = Vector3.right;
            public bool UseCastFlash;     // true 时闪白色用施法色而非白色
            public Color CastFlashColor = Color.white;
        }

        private readonly HitFeedbackState _hitFeedback = new HitFeedbackState();
        #endregion

        /// <summary>攻击出手时身体前倾的持续计时（0 表示未播放）。</summary>
        private float _attackPoseTimer;

        #region 死亡表现
        private sealed class DeathState
        {
            public bool Pending;
            /// <summary>死亡表现已开始且尚未复活还原。</summary>
            public bool AwaitingReviveRestore;
            public float Timer;
            public System.Action OnFinished;
        }

        private readonly DeathState _death = new DeathState();
        #endregion

        #region 击退 Nudge（仅表现层软推）
        private sealed class KnockbackState
        {
            public Vector3 Offset;
            public float Timer;
        }

        private readonly KnockbackState _knockback = new KnockbackState();
        #endregion

        #region 击飞（Airborne）表现
        private sealed class AirborneState
        {
            public bool Visual;
            public float Lift;
            public float BurstTimer;
            public float BurstDuration;
            public float BurstHeight;
        }

        private readonly AirborneState _airborne = new AirborneState();
        #endregion

        public bool IsDeathPending => _death.Pending;
        public bool NeedsReviveRestore => _death.AwaitingReviveRestore;

        public override void OnStart()
        {
            CacheBaseScale();
        }

        #region 公开 API：触发各类表现事件
        public void OnCombatViewEvent(CombatViewEvent e)
        {
            switch (e.Type)
            {
                case ECombatViewEvent.AttackSwing:
                    PlayAttackPose();
                    break;
                case ECombatViewEvent.Hit:
                    // 受击动作由 Anim 轨数据驱动（AnimHandler）；此处仅身体闪白/抖动
                    PlayHit(e.HitFrom);
                    break;
            }
        }

        public void PlayAttackPose()
        {
            _attackPoseTimer = AttackPoseDuration;
        }

        public void CancelAttackPose()
        {
            _attackPoseTimer = 0f;
        }

        /// <summary>施法瞬间身体闪（能量释放感），比受击更短、偏技能色。</summary>
        public void PlayCastFlash(Color glow)
        {
            CacheBaseScale();
            _hitFeedback.FlashTimer = FlashDuration * 0.75f;
            _hitFeedback.CastFlashColor = glow;
            _hitFeedback.UseCastFlash = true;
        }

        public void PlayHit(Vector3 hitFromWorld)
        {
            CacheBaseScale();
            _hitFeedback.UseCastFlash = false;

            // 单位配表 SuppressHitBodyFeedback：不做抖动、闪白、朝向甩头
            if (Owner != null && CombatFeedbackRules.SuppressHitBodyFeedback(Owner.Id))
            {
                _hitFeedback.ShakeTimer = 0f;
                return;
            }

            _hitFeedback.FlashTimer = FlashDuration;
            _hitFeedback.ShakeTimer = ShakeDuration;

            Vector3 myPos = ViewComp?.ViewGameObject != null
                ? ViewComp.ViewGameObject.transform.position
                : TransformComp?.Position ?? Vector3.zero;

            // 抖动方向：被击退感（远离来源）
            Vector3 away = myPos - hitFromWorld;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.right;
            _hitFeedback.ShakeDir = away.normalized;

            // 朝向：面朝攻击来源
            Vector3 face = hitFromWorld - myPos;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
                ViewComp?.SnapFace(face);
        }

        /// <summary>Reaction Displacement：仅表现层软推，不改逻辑坐标。</summary>
        public void PlayKnockbackNudge(Vector3 dir, float distance)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f || distance <= 0f) return;
            _knockback.Offset = dir.normalized * distance;
            _knockback.Timer = KnockDuration;
        }

        /// <summary>击飞表现：模型上抬（不改逻辑坐标）。</summary>
        public void SetAirborneVisual(bool on)
        {
            _airborne.Visual = on;
            if (!on && _airborne.BurstTimer <= 0f)
                _airborne.Lift = 0f;
        }

        /// <summary>命中瞬间击飞弹起（不依赖 Buff 同步，木桩也可看见）。</summary>
        public void PlayAirborneBurst(float height = DefaultAirborneHeight, float duration = 0.85f)
        {
            if (height <= 0.05f) height = DefaultAirborneHeight;
            if (duration <= 0.05f) duration = 0.85f;
            _airborne.BurstHeight = height;
            _airborne.BurstDuration = duration;
            _airborne.BurstTimer = duration;
            _airborne.Lift = Mathf.Max(_airborne.Lift, height * 0.45f);
        }

        public void PlayDeath(System.Action onFinished)
        {
            // Dead 包常先于 Hit/Anim 到达：先挂上 Finish，再被 PlayDeath(null) 覆盖会导致尸体不销毁。
            // 只允许升级回调（null → Action），不允许降级（Action → null）。
            if (onFinished != null)
                _death.OnFinished = onFinished;

            if (_death.Pending)
                return;

            _death.Pending = true;
            _death.AwaitingReviveRestore = true;
            _hitFeedback.FlashTimer = FlashDuration;
            CacheBaseScale();
            if (_scaleCache.Captured && ViewComp?.ViewGameObject != null)
                ViewComp.ViewGameObject.transform.localScale = _scaleCache.Value;

            Owner?.GetComponent<HeadUIComponent>()?.SetVisible(false);

            var anim = Owner?.GetComponent<AnimationComponent>();
            anim?.CrossFade(AnimationComponent.EAnimState.Die, 0.05f);
            float clipLen = anim != null
                ? anim.GetStateLength(AnimationComponent.StateDeath)
                : 1.2f;
            _death.Timer = Mathf.Clamp(clipLen + DeathHoldAfterClip, DeathDurationMin, DeathDurationMax);
        }

        /// <summary>复活 / 强制归位：还原外观与死亡锁。</summary>
        public void RestoreAfterRevive()
        {
            _death.Pending = false;
            _death.AwaitingReviveRestore = false;
            _death.Timer = 0f;
            _death.OnFinished = null;
            _hitFeedback.FlashTimer = 0f;
            _hitFeedback.ShakeTimer = 0f;
            _knockback.Timer = 0f;
            _knockback.Offset = Vector3.zero;
            _airborne.Visual = false;
            _airborne.BurstTimer = 0f;
            _airborne.Lift = 0f;
            _attackPoseTimer = 0f;
            _hitFeedback.UseCastFlash = false;

            if (ViewComp?.ViewGameObject != null)
            {
                CacheBaseScale();
                ViewComp.ViewGameObject.transform.localScale = _scaleCache.Value;
                ViewComp.ClearHitFlash();
                ViewComp.SnapToLogic();
            }

            Owner?.GetComponent<HeadUIComponent>()?.SetVisible(true);
            Owner?.GetComponent<AnimationComponent>()?.ClearDieLock();
        }
        #endregion

        #region 每帧驱动：计时衰减 / 位置合成
        public override void OnUpdate(float dt)
        {
            TickEffectTimers(dt);
            UpdateAirborneLift(dt);
            UpdateDeathCountdown(dt);
        }

        /// <summary>各表现效果的独立倒计时衰减。</summary>
        private void TickEffectTimers(float dt)
        {
            if (_attackPoseTimer > 0f) _attackPoseTimer -= dt;
            if (_hitFeedback.FlashTimer > 0f) _hitFeedback.FlashTimer -= dt;
            if (_hitFeedback.ShakeTimer > 0f) _hitFeedback.ShakeTimer -= dt;
            if (_knockback.Timer > 0f) _knockback.Timer -= dt;
            if (_airborne.BurstTimer > 0f) _airborne.BurstTimer -= dt;
        }

        /// <summary>击飞高度：弹起曲线（Burst）与常驻悬浮（Visual）取较高目标，再平滑跟随。</summary>
        private void UpdateAirborneLift(float dt)
        {
            float targetLift = 0f;
            if (_airborne.BurstTimer > 0f && _airborne.BurstDuration > 0.001f)
            {
                float remaining = Mathf.Clamp01(_airborne.BurstTimer / _airborne.BurstDuration);
                float t = 1f - remaining;
                float apex = t < 0.35f
                    ? Mathf.SmoothStep(0f, 1f, t / 0.35f)
                    : Mathf.SmoothStep(1f, 0f, (t - 0.35f) / 0.65f);
                targetLift = _airborne.BurstHeight * Mathf.Clamp01(apex);
            }
            else if (_airborne.Visual)
            {
                targetLift = DefaultAirborneHeight;
            }

            if (targetLift > 0.001f)
                _airborne.Lift = Mathf.MoveTowards(_airborne.Lift, targetLift, dt * 14f);
            else if (_airborne.Lift > 0f)
                _airborne.Lift = Mathf.MoveTowards(_airborne.Lift, 0f, dt * 10f);
        }

        private void UpdateDeathCountdown(float dt)
        {
            if (!_death.Pending) return;

            _death.Timer -= dt;
            if (_death.Timer <= 0f)
            {
                _death.Pending = false;
                var cb = _death.OnFinished;
                _death.OnFinished = null;
                cb?.Invoke();
            }
        }

        public override void OnLateUpdate(float dt)
        {
            if (ViewComp?.ViewGameObject == null) return;

            UpdateHitFlashVisual();

            var viewTransform = ViewComp.ViewGameObject.transform;
            Vector3 pos = viewTransform.position;
            Quaternion rot = viewTransform.rotation;

            ApplyKnockbackOffset(ref pos);
            ApplyAirborneOffset(ref pos);
            ApplyShakeOffset(ref pos);
            ApplyAttackPoseOffset(viewTransform, ref pos);

            viewTransform.position = pos;
            viewTransform.rotation = rot;
        }

        /// <summary>受击/死亡闪白：死亡态强制暗红闪，否则按施法/受击选色。</summary>
        private void UpdateHitFlashVisual()
        {
            if (_hitFeedback.FlashTimer > 0f)
            {
                float flashT = Mathf.Clamp01(_hitFeedback.FlashTimer / FlashDuration);
                Color hitColor = _death.Pending
                    ? new Color(0.45f, 0.05f, 0.05f, 1f)
                    : (_hitFeedback.UseCastFlash ? _hitFeedback.CastFlashColor : Color.white);
                ViewComp.SetHitFlash(hitColor, flashT * (_hitFeedback.UseCastFlash ? 0.65f : 1f));
            }
            else
            {
                _hitFeedback.UseCastFlash = false;
                ViewComp.ClearHitFlash();
            }
        }

        private void ApplyKnockbackOffset(ref Vector3 pos)
        {
            if (_knockback.Timer <= 0f) return;
            float t = Mathf.Clamp01(_knockback.Timer / KnockDuration);
            pos += _knockback.Offset * t;
        }

        private void ApplyAirborneOffset(ref Vector3 pos)
        {
            if (_airborne.Lift > 0.001f)
                pos += Vector3.up * _airborne.Lift;
        }

        private void ApplyShakeOffset(ref Vector3 pos)
        {
            if (_hitFeedback.ShakeTimer <= 0f) return;
            float t = _hitFeedback.ShakeTimer / ShakeDuration;
            float wave = Mathf.Sin((1f - t) * Mathf.PI * 6f) * ShakeAmplitude * t;
            pos += _hitFeedback.ShakeDir * wave;
        }

        private void ApplyAttackPoseOffset(Transform viewTransform, ref Vector3 pos)
        {
            if (_attackPoseTimer <= 0f) return;
            float t = _attackPoseTimer / AttackPoseDuration;
            Vector3 forward = viewTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                pos += forward.normalized * (0.12f * t);
        }
        #endregion

        private void CacheBaseScale()
        {
            if (ViewComp?.ViewGameObject == null) return;
            if (!_scaleCache.Captured)
            {
                _scaleCache.Value = ViewComp.ViewGameObject.transform.localScale;
                _scaleCache.Captured = true;
            }
        }
    }
}
