using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>本地玩家移动：只消费 InputPlayerComponent 的移动意图并预测发包。</summary>
    public class LocalPlayerMoveComponent : Component
    {
        private bool _sentMoving;

        private TransformComponent _transformComp;
        private PredictionMovementComponent _syncComp;
        private StateComponent _stateComp;
        private SkillCastComponent _skillComp;
        private ViewComponent _viewComp;
        private InputPlayerComponent _input;

        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        private PredictionMovementComponent SyncComp =>
            _syncComp ??= Owner?.GetComponent<PredictionMovementComponent>();
        private StateComponent StateComp =>
            _stateComp ??= Owner?.GetComponent<StateComponent>();
        private SkillCastComponent SkillComp =>
            _skillComp ??= Owner?.GetComponent<SkillCastComponent>();
        private ViewComponent ViewComp =>
            _viewComp ??= Owner?.GetComponent<ViewComponent>();
        private InputPlayerComponent InputPlayer =>
            _input ??= Owner?.GetComponent<InputPlayerComponent>();

        public override void OnPreUpdate(float dt)
        {
            if (TransformComp == null || SyncComp == null) return;

            var session = BattleSystem.Instance?.Session;
            if (session != null && (session.MatchEnded || session.LocalPlayerDead))
            {
                if (StateComp != null)
                    StateComp.MoveIntent = Vector3.zero;
                _sentMoving = false;
                return;
            }

            float safeDt = dt > 0.0001f ? dt : Time.deltaTime;
            if (safeDt > GameConstants.MaxInputDeltaTime)
                safeDt = GameConstants.MaxInputDeltaTime;

            bool canMove = StateComp == null || StateComp.CanMove;
            if (canMove && SkillComp != null && SkillComp.LocksMovementNow)
                canMove = false;

            Vector3 wishDir = InputPlayer != null ? InputPlayer.WishDir : Vector3.zero;
            Vector3 moveDir = canMove ? wishDir : Vector3.zero;

            if (StateComp != null)
                StateComp.MoveIntent = moveDir;

            ApplyMoveInterruptIfNeeded(wishDir);

            if (StateComp != null && !StateComp.AllowsLocomotion)
                moveDir = Vector3.zero;

            PredictAndSendMove(moveDir, safeDt);
            FaceMoveDir(moveDir);
        }

        private void ApplyMoveInterruptIfNeeded(Vector3 wishDir)
        {
            if (StateComp == null || StateComp.CurrentType != EEntityFsmState.Skill) return;
            if (wishDir.sqrMagnitude < 0.0001f) return;
            SkillComp?.TryCancelByMove();
        }

        private void PredictAndSendMove(Vector3 moveDir, float safeDt)
        {
            bool moving = moveDir.sqrMagnitude > 0.0001f;
            bool needSend = moving
                            || !TransformComp.IsGrounded
                            || Mathf.Abs(TransformComp.VelY) > 0.01f
                            || _sentMoving;

            float moveMul = StateComp?.MoveSpeedMultiplier ?? 1f;
            if (SyncComp.ShouldPredictLocally)
            {
                var moveState = new MovementState
                {
                    PosX = TransformComp.Position.x,
                    PosY = TransformComp.Position.y,
                    PosZ = TransformComp.Position.z,
                    VelY = TransformComp.VelY,
                    IsGrounded = TransformComp.IsGrounded
                };

                moveState = MovementSimulator.Simulate(moveState, new MoveInput
                {
                    MoveX = moveDir.x,
                    MoveZ = moveDir.z,
                    Jump = false,
                    DeltaTime = safeDt,
                    SpeedMultiplier = moveMul
                });

                float ox = TransformComp.Position.x;
                float oz = TransformComp.Position.z;
                TransformComp.ApplyMovementState(
                    moveState.PosX, moveState.PosY, moveState.PosZ,
                    moveState.VelY, moveState.IsGrounded);
                float dx = moveState.PosX - ox;
                float dz = moveState.PosZ - oz;
                if (moving && dx * dx + dz * dz < 0.000001f)
                {
                    moveDir = Vector3.zero;
                    moving = false;
                    if (StateComp != null)
                        StateComp.MoveIntent = Vector3.zero;
                }

                needSend = moving
                           || !moveState.IsGrounded
                           || Mathf.Abs(moveState.VelY) > 0.01f
                           || _sentMoving;
            }

            if (needSend)
            {
                // 停下这一下立刻发出，别在发送间隔里把「停」攒住。
                bool forceFlush = !moving && TransformComp.IsGrounded;
                SyncComp.RecordAndSendInput(moveDir.x, moveDir.z, jump: false, safeDt, forceFlush);
                _sentMoving = moving;
            }
        }

        void FaceMoveDir(Vector3 moveDir)
        {
            if (ViewComp == null) return;

            if (SkillComp != null && SkillComp.TryGetCastFacing(out var castFace))
            {
                ViewComp.Face(castFace, 30f);
                return;
            }

            if (moveDir.sqrMagnitude > 0.0001f)
                ViewComp.Face(moveDir, 18f);
        }
    }
}
