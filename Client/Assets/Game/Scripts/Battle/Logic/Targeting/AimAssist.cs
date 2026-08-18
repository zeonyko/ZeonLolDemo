using UnityEngine;

namespace Client.Battle
{
    /// <summary>瞄准方向：本帧 PlayerInput 的 AimDir，否则移动粘滞 / 面朝 / 镜头。</summary>
    public static class AimAssist
    {
        const float PreferMoveSqr = 0.04f;

        static Vector3 _stickyDir = Vector3.forward;

        public static void Tick(Vector3 moveDir)
        {
            if (moveDir.sqrMagnitude <= PreferMoveSqr) return;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude > 0.0001f)
                _stickyDir = moveDir.normalized;
        }

        public static Vector3 Resolve(Vector3 preferDir, Vector3 facingDir, Camera cam = null)
        {
            if (PlayerInput.ActiveAimSkillId > 0 && PlayerInput.Frame.HasAimDir)
            {
                _stickyDir = PlayerInput.Frame.AimDir;
                return _stickyDir;
            }

            if (preferDir.sqrMagnitude > PreferMoveSqr)
            {
                preferDir.y = 0f;
                if (preferDir.sqrMagnitude > 0.0001f)
                    return preferDir.normalized;
            }

            if (_stickyDir.sqrMagnitude > 0.0001f)
                return _stickyDir;

            facingDir.y = 0f;
            if (facingDir.sqrMagnitude > 0.0001f)
                return facingDir.normalized;

            if (cam != null)
            {
                Vector3 d = cam.transform.forward;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f)
                    return d.normalized;
            }

            return Vector3.forward;
        }
    }
}
