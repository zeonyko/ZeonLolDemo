using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>技能时间轴到点后改逻辑坐标。画面插值由 View 自己做。</summary>
    public static class SkillMotionApply
    {
        public static void Commit(Entity owner, string key, string param, Vector3 castDir, Vector3 aimPos = default)
        {
            if (owner == null) return;
            var transformComp = owner.GetComponent<TransformComponent>();
            if (transformComp == null) return;

            var syncComp = owner.GetComponent<PredictionMovementComponent>();
            var moveState = new MovementState
            {
                PosX = transformComp.Position.x,
                PosY = transformComp.Position.y,
                PosZ = transformComp.Position.z,
                VelY = transformComp.VelY,
                IsGrounded = transformComp.IsGrounded
            };

            if (key == SkillTimelineKeys.CommitJump)
            {
                if (!transformComp.IsGrounded) return;
                moveState = MovementSimulator.ApplyJump(moveState, GameConstants.JumpForce);
                transformComp.ApplyMovementState(
                    moveState.PosX, moveState.PosY, moveState.PosZ, moveState.VelY, moveState.IsGrounded);
                syncComp?.BeginJumpPrediction(GameConstants.JumpForce);
                owner.GetComponent<SkillCastComponent>()?.MarkMotionPredicted();
                return;
            }

            if (!SkillMotionCodec.TryParse(param, out float distance, out var targetType, out var collision))
                return;

            float dirX = castDir.x;
            float dirZ = castDir.z;
            float dist = distance;
            SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);

            if (targetType == ESkillMotionTargetType.LockTarget
                && aimPos.sqrMagnitude > 0.0001f)
            {
                bool front = collision == ESkillMotionCollisionPolicy.StopOnHitEnemy;
                float destX, destZ, faceX, faceZ;
                bool ok = front
                    ? SkillRules.TryGetLockFrontDestination(
                        transformComp.Position.x, transformComp.Position.z,
                        aimPos.x, aimPos.z, distance,
                        out destX, out destZ, out faceX, out faceZ)
                    : SkillRules.TryGetLockBehindDestination(
                        transformComp.Position.x, transformComp.Position.z,
                        aimPos.x, aimPos.z, distance,
                        out destX, out destZ, out faceX, out faceZ);
                if (ok)
                {
                    dirX = destX - transformComp.Position.x;
                    dirZ = destZ - transformComp.Position.z;
                    dist = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (dist < 0.05f)
                    {
                        dirX = faceX;
                        dirZ = faceZ;
                        dist = distance;
                    }
                    SkillRules.NormalizeHorizontal(ref dirX, ref dirZ);
                    owner.GetComponent<ViewComponent>()?.Face(new Vector3(faceX, 0f, faceZ), 40f);
                }
            }

            moveState = MovementSimulator.ApplySkillMotion(moveState, dirX, dirZ, dist);
            var landPos = new Vector3(moveState.PosX, moveState.PosY, moveState.PosZ);

            if (key == SkillTimelineKeys.CommitBlink)
                transformComp.SnapPosition(landPos);
            else
                transformComp.SetPosition(landPos);

            syncComp?.BeginMotionPrediction(dist, dirX, dirZ);
            owner.GetComponent<SkillCastComponent>()?.MarkMotionPredicted();
        }
    }
}
