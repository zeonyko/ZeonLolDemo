using UnityEngine;

namespace Client.Battle
{
    /// <summary>逻辑位置（服务器为准 / 本地先动后的坐标）。画面慢慢跟上，不在这里做。</summary>
    public class TransformComponent : Component
    {
        public Vector3 Position { get; private set; }
        public float VelY { get; set; }
        public bool IsGrounded { get; set; } = true;

        /// <summary>写入逻辑坐标（本地先动 / 跟服务器对齐 / 普通同步）。</summary>
        public void SetPosition(Vector3 pos)
        {
            Position = pos;
            Owner?.DispatchEvent("OnPositionChanged", Position);
        }

        /// <summary>逻辑瞬移，画面立刻贴齐（技能落点 / 服务器硬拉）。</summary>
        public void SnapPosition(Vector3 targetPos)
        {
            Position = targetPos;
            Owner?.DispatchEvent("OnPositionSnap", Position);
        }

        /// <summary>一次写入位置、竖直速度、是否着地。</summary>
        public void ApplyMovementState(float x, float y, float z, float velY, bool grounded)
        {
            VelY = velY;
            IsGrounded = grounded;
            SetPosition(new Vector3(x, y, z));
        }
    }
}
