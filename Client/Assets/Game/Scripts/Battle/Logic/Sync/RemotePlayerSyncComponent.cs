using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>别人位置同步：连续包平滑跟上；硬拉立刻贴齐；控制态跟着状态标记走。</summary>
    public class RemotePlayerSyncComponent : Component
    {
        #region 依赖组件（用到再取）
        private TransformComponent _transformComp;
        private StateComponent _stateComp;
        private ViewComponent _viewComp;
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        private StateComponent StateComp =>
            _stateComp ??= Owner?.GetComponent<StateComponent>();
        private ViewComponent ViewComp =>
            _viewComp ??= Owner?.GetComponent<ViewComponent>();
        #endregion

        #region 别人位置：平滑跟上
        private SnapshotInterpolator _interpolator;
        private Vector3 _lastRawPos;     // 最近一次收到的别人位置
        private bool _hasRaw;            // 是否已经收到过位置
        private Vector3 _animSamplePos;  // 上一帧用来算移动速度的位置
        private bool _hasAnimSample;     // 是否已经采过一次
        private Vector3 _moveFaceDir;    // 用快照位移推的朝向（比插值帧更稳）
        private bool _hasMoveFace;
        #endregion

        #region 生命周期
        public override void OnAwake()
        {
            _interpolator = new SnapshotInterpolator();
            if (TransformComp != null)
            {
                _interpolator.Reset(TransformComp.Position, Time.time);
                _lastRawPos = TransformComp.Position;
                _animSamplePos = TransformComp.Position;
                _hasAnimSample = true;
            }
        }

        public override void OnLateUpdate(float dt)
        {
            if (TransformComp == null || _interpolator == null) return;

            if (!SyncDebugSettings.RemoteInterpolation)
            {
                if (_hasRaw)
                    TransformComp.SetPosition(_lastRawPos);
            }
            else if (_interpolator.TrySample(Time.time, out Vector3 renderPos))
            {
                TransformComp.SetPosition(renderPos);
            }

            UpdateLocomotionAnim(dt);
        }
        #endregion

        #region 收到服务器发来的位置
        public void OnReceiveSnapshot(Vector3 newPos, bool hardSnap, uint stateFlags = 0)
        {
            ApplyStateFlags(stateFlags);

            if (hardSnap)
            {
                ApplyHardSnap(newPos);
                return;
            }

            if (TransformComp == null) return;

            RememberMoveFace(newPos);
            _lastRawPos = newPos;
            _hasRaw = true;
            ReportDebugRawIfHero(newPos);

            float receiveTime = Time.time;
            if (!SyncDebugSettings.RemoteInterpolation)
            {
                TransformComp.SetPosition(newPos);
                _interpolator.Reset(newPos, receiveTime);
                return;
            }

            _interpolator.PushSnapshot(newPos, receiveTime);
        }

        /// <summary>位置立刻对齐。打断平滑跟上，画面马上贴过去。</summary>
        public void ApplyHardSnap(Vector3 targetPos)
        {
            if (TransformComp == null) return;

            RememberMoveFace(targetPos);
            _lastRawPos = targetPos;
            _hasRaw = true;
            ReportDebugRawIfHero(targetPos);

            TransformComp.SnapPosition(targetPos);
            _interpolator.Reset(targetPos, Time.time);
        }
        #endregion

        #region 表现层辅助（内部）
        /// <summary>脚底「R」只标远端英雄；小兵/野怪快照更勤，否则圈会乱跳到兵脚下。</summary>
        private void ReportDebugRawIfHero(Vector3 rawPos)
        {
            if (!SyncDebugSettings.ShowMarkers) return;
            if (Owner == null || BattleCache.Instance == null) return;
            if (!BattleCache.Instance.TryGet(Owner.Id, out var data)) return;
            if (data.EntityType != EEntityType.Player) return;
            SyncCompareComponent.Instance?.ReportRemoteRawSnapshot(rawPos);
        }

        private void ApplyStateFlags(uint stateFlags)
        {
            StateComp?.ReconcileControlMask((EntityStateTag)stateFlags);
        }

        /// <summary>用相邻快照位移记朝向；站着不动时保留上次方向。</summary>
        private void RememberMoveFace(Vector3 newPos)
        {
            if (!_hasRaw)
                return;

            Vector3 delta = newPos - _lastRawPos;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0004f)
                return;

            _moveFaceDir = delta.normalized;
            _hasMoveFace = true;
        }

        /// <summary>用位移推算远端 Walk/Idle，并驱动朝向。</summary>
        private void UpdateLocomotionAnim(float dt)
        {
            if (StateComp == null || TransformComp == null) return;
            if (Owner?.GetComponent<CombatViewComponent>()?.IsDeathPending == true)
                return;
            if (StateComp.CurrentType == EEntityFsmState.Skill
                || StateComp.CurrentType == EEntityFsmState.Stun)
                return;

            Vector3 pos = TransformComp.Position;
            if (!_hasAnimSample)
            {
                _animSamplePos = pos;
                _hasAnimSample = true;
                StateComp.MoveIntent = Vector3.zero;
                return;
            }

            Vector3 delta = pos - _animSamplePos;
            _animSamplePos = pos;
            delta.y = 0f;

            float speed = dt > 0.0001f ? delta.magnitude / dt : 0f;
            if (speed > 0.35f)
            {
                StateComp.MoveIntent = delta.normalized;
                // 优先用快照方向；没有时再用本帧插值位移。
                Vector3 face = _hasMoveFace ? _moveFaceDir : delta.normalized;
                ViewComp?.Face(face, 14f);
            }
            else
            {
                StateComp.MoveIntent = Vector3.zero;
            }
        }
        #endregion
    }
}
