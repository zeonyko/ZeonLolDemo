using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>本地移动意图：摇杆换成镜头相对方向，以及点地射线。</summary>
    public static class LocalPlayerMoveMath
    {
        /// <summary>将输入轴向换算为相机水平面上的世界方向（无相机时退化为轴向本身）。</summary>
        public static Vector3 ToCameraRelativeMove(Vector2 axes, Camera mainCam)
        {
            if (axes.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            float mag = axes.magnitude;
            float nx = axes.x / mag;
            float nz = axes.y / mag;
            float strength = Mathf.Clamp01(mag);

            if (mainCam == null)
                return new Vector3(nx, 0f, nz) * strength;

            Vector3 camForward = mainCam.transform.forward;
            Vector3 camRight = mainCam.transform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = camForward * nz + camRight * nx;
            if (moveDir.sqrMagnitude > 0.0001f)
                moveDir.Normalize();
            return moveDir * strength;
        }

        /// <summary>屏幕坐标投射到地面平面（GameConstants.GroundY），失败时 hit 为零向量。</summary>
        public static bool TryRaycastGround(Camera cam, Vector3 screenPos, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screenPos);
            var ground = new Plane(Vector3.up, new Vector3(0f, GameConstants.GroundY, 0f));
            if (!ground.Raycast(ray, out float enter))
                return false;
            hit = ray.GetPoint(enter);
            return true;
        }
    }
}
