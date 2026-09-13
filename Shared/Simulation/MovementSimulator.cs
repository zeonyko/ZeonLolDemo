using System;

namespace Shared
{
    /// <summary>当前位置、垂直速度、是否落地。本地先动和服务器共用。</summary>
    public struct MovementState
    {
        #region --- 字段 ---

        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelY;
        public bool IsGrounded;

        #endregion

        #region --- 构造 ---

        /// <summary>用坐标生成状态。贴地就算落地。</summary>
        public static MovementState FromPosition(float x, float y, float z)
        {
            return new MovementState
            {
                PosX = x,
                PosY = y,
                PosZ = z,
                VelY = 0f,
                IsGrounded = y <= GameConstants.GroundY + 0.001f
            };
        }

        #endregion
    }

    /// <summary>一帧的移动意图。和网络包分开，这里只给模拟器用。</summary>
    public struct MoveInput
    {
        /// <summary>水平意图（方向或摇杆幅度≤1）；不含移速/减速倍率。</summary>
        public float MoveX;
        public float MoveZ;
        public bool Jump;
        public float DeltaTime;
        /// <summary>移速倍率（减速等）。没填当 1。服务器必须用自己的状态来写。</summary>
        public float SpeedMultiplier;
    }

    /// <summary>走一步：按意图移动、撞墙缩短、再算跳跃和重力。本地先动和服务器都调这一份。</summary>
    public static class MovementSimulator
    {
        #region --- 走一步 ---

        /// <summary>推进一帧移动。</summary>
        public static MovementState Simulate(MovementState state, MoveInput input)
        {
            float dt = input.DeltaTime;
            if (dt <= 0f)
                return state;

            // 一帧时间太长就截住，防止卡住后瞬移。
            if (dt > GameConstants.MaxInputDeltaTime)
                dt = GameConstants.MaxInputDeltaTime;

            WorldCollision.ClampToWalkable(ref state.PosX, ref state.PosZ);
            float originX = state.PosX;
            float originZ = state.PosZ;
            ApplyIntentDisplacement(ref state, input, dt);
            ClampAgainstWalls(ref state, originX, originZ);
            WorldCollision.ClampToWalkable(ref state.PosX, ref state.PosZ);
            ApplyJumpAndGravity(ref state, input, dt);
            return state;
        }

        /// <summary>摇杆幅度限制在 1 以内，再按移速移动。</summary>
        private static void ApplyIntentDisplacement(ref MovementState state, MoveInput input, float dt)
        {
            float moveX = input.MoveX;
            float moveZ = input.MoveZ;
            float magSq = moveX * moveX + moveZ * moveZ;
            if (magSq > 1.0001f)
            {
                float inv = 1f / (float)Math.Sqrt(magSq);
                moveX *= inv;
                moveZ *= inv;
            }

            float speedMul = input.SpeedMultiplier > 0f ? input.SpeedMultiplier : 1f;
            state.PosX += moveX * GameConstants.MoveSpeed * speedMul * dt;
            state.PosZ += moveZ * GameConstants.MoveSpeed * speedMul * dt;
        }

        /// <summary>撞墙就把这一步缩短。</summary>
        private static void ClampAgainstWalls(ref MovementState state, float originX, float originZ)
        {
            float movedX = state.PosX - originX;
            float movedZ = state.PosZ - originZ;
            float movedDistSq = movedX * movedX + movedZ * movedZ;
            if (movedDistSq <= 0.0000001f)
                return;

            float movedDist = (float)Math.Sqrt(movedDistSq);
            float actual = WorldCollision.ClampMoveDistance(
                originX, originZ, movedX, movedZ, movedDist);
            if (actual >= movedDist)
                return;

            float inv = 1f / movedDist;
            state.PosX = originX + movedX * inv * actual;
            state.PosZ = originZ + movedZ * inv * actual;
        }

        /// <summary>落地可跳；空中受重力。</summary>
        private static void ApplyJumpAndGravity(ref MovementState state, MoveInput input, float dt)
        {
            if (state.IsGrounded)
            {
                state.VelY = -0.5f;
                if (input.Jump)
                {
                    state.VelY = GameConstants.JumpForce;
                    state.IsGrounded = false;
                }
            }
            else
            {
                state.VelY -= GameConstants.Gravity * dt;
            }

            state.PosY += state.VelY * dt;
            if (state.PosY <= GameConstants.GroundY)
            {
                state.PosY = GameConstants.GroundY;
                state.VelY = 0f;
                state.IsGrounded = true;
            }
        }

        #endregion

        #region --- 技能位移 ---

        /// <summary>技能水平位移（闪现/冲锋）。撞墙缩短。高度和速度不变。</summary>
        public static MovementState ApplySkillMotion(MovementState state, float dirX, float dirZ, float distance)
        {
            float magSq = dirX * dirX + dirZ * dirZ;
            if (magSq < 0.0001f)
            {
                dirX = 0f;
                dirZ = 1f;
            }
            else
            {
                float inv = 1f / (float)Math.Sqrt(magSq);
                dirX *= inv;
                dirZ *= inv;
            }

            float actual = WorldCollision.ClampMoveDistance(
                state.PosX, state.PosZ, dirX, dirZ, distance);
            state.PosX += dirX * actual;
            state.PosZ += dirZ * actual;
            WorldCollision.ClampToWalkable(ref state.PosX, ref state.PosZ, WorldCollision.DefaultAgentRadius);
            return state;
        }

        /// <summary>落地起跳。</summary>
        public static MovementState ApplyJump(MovementState state, float force)
        {
            if (!state.IsGrounded) return state;
            state.VelY = force;
            state.IsGrounded = false;
            return state;
        }

        #endregion

        #region --- 距离 ---

        /// <summary>水平距离（忽略高度）。</summary>
        public static float DistanceHorizontal(MovementState a, float x, float z)
        {
            float dx = a.PosX - x;
            float dz = a.PosZ - z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>两点直线距离。</summary>
        public static float Distance3D(float ax, float ay, float az, float bx, float by, float bz)
        {
            float dx = ax - bx;
            float dy = ay - by;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        #endregion
    }
}
