using UnityEngine;

namespace Client.Battle
{
    /// <summary>手机：摇杆移动、技能拖方向、松手确认、点战场选人。</summary>
    public sealed class MobileBattleInput : IBattleInputSource
    {
        public bool ShowsMovePad => true;

        public void Collect(Camera cam, ref PlayerInputFrame frame)
        {
            if (frame.SkillPointerUp && !frame.Cancel)
                frame.Confirm = true;

            if (frame.HasAimDir)
            {
                // HUD 已把技能摇杆换成世界方向；落点由 InputPlayer 按技能射程截断
            }
            else if (frame.AimStickMag > 0.04f)
            {
                Vector3 dir = LocalPlayerMoveMath.ToCameraRelativeMove(
                    PlayerInput.SkillAimStick, cam);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    frame.AimDir = dir.normalized;
                    frame.HasAimDir = true;
                }
            }

            CollectWorldTap(ref frame);
        }

        static void CollectWorldTap(ref PlayerInputFrame frame)
        {
            if (frame.Confirm || PlayerInput.ActiveAimSkillId > 0)
                return;
            if (Input.touchCount <= 0) return;

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Ended) continue;
                if (PlayerInput.IsReservedFinger(t.fingerId)) continue;
                if (PlayerInput.WasUiPointerUp(t.fingerId)) continue;
                if (PlayerInput.IsPointerOverUi(t.fingerId)) continue;
                frame.WorldTap = true;
                frame.WorldTapScreen = t.position;
                return;
            }
        }
    }
}
