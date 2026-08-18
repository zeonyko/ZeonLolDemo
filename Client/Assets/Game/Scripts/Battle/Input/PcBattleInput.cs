using Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Client.Battle
{
    /// <summary>PC：热键瞄准、鼠标点地方向、右键点地移动、中键拖镜头、空格复位。</summary>
    public sealed class PcBattleInput : IBattleInputSource
    {
        public bool ShowsMovePad => false;

        public void Collect(Camera cam, ref PlayerInputFrame frame)
        {
            CollectClickMove(cam, ref frame);
            CollectHotkeys(ref frame);
            CollectMouseAim(cam, ref frame);
            CollectConfirmAndTap(ref frame);
        }

        static void CollectClickMove(Camera cam, ref PlayerInputFrame frame)
        {
            bool overUi = EventSystem.current != null
                          && EventSystem.current.IsPointerOverGameObject(-1);
            bool held = Input.GetMouseButton(1);
            bool down = Input.GetMouseButtonDown(1);

            if (down && PlayerInput.ActiveAimSkillId > 0)
                frame.Cancel = true;

            if (!overUi && (down || held)
                && LocalPlayerMoveMath.TryRaycastGround(cam, Input.mousePosition, out Vector3 hit))
            {
                frame.HasClickMove = true;
                frame.ClickMovePoint = hit;
                frame.ClickMoveHeld = held;
            }
            else if (held)
            {
                frame.ClickMoveHeld = true;
            }
        }

        static void CollectHotkeys(ref PlayerInputFrame frame)
        {
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Escape))
                frame.Cancel = true;
            if (Input.GetKeyDown(KeyCode.Space))
                frame.CameraLock = true;

            var entries = SkillHotkeyTable.All;
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (!e.IsBound || e.Hotkey == KeyCode.S) continue;
                if (!Input.GetKeyDown(e.Hotkey)) continue;
                frame.BeginAimSkillId = e.SkillId;

                // 方向技：进入瞄准，等左键确认
                if (SkillRules.NeedsAimDirection(e.SkillId))
                    return;

                // 普攻 / 有范围预览的锁敌冲刺等：按住显示圈，松手再放（对齐点 UI 的手感）
                if (IsAutoAttack(e.SkillId) || SkillAimRangeView.HasPreview(e.SkillId))
                    return;

                // 无预览的瞬发（如纯自身）同帧出手
                frame.Confirm = true;
                return;
            }

            TryConfirmHeldSkill(entries, ref frame);
        }

        /// <summary>按住预览中的技能：对应热键抬起时确认。</summary>
        static void TryConfirmHeldSkill(SkillHotkeyTable.Entry[] entries, ref PlayerInputFrame frame)
        {
            int aimId = PlayerInput.ActiveAimSkillId;
            if (aimId <= 0 || SkillRules.NeedsAimDirection(aimId))
                return;

            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.SkillId != aimId) continue;
                if (Input.GetKeyUp(e.Hotkey))
                    frame.Confirm = true;
                return;
            }
        }

        static bool IsAutoAttack(int skillId)
        {
            var def = SkillCatalog.Get(skillId);
            return def != null && def.IsAutoAttack;
        }

        static void CollectMouseAim(Camera cam, ref PlayerInputFrame frame)
        {
            if (!LocalPlayerMoveMath.TryRaycastGround(cam, Input.mousePosition, out Vector3 hit))
                return;

            var session = BattleSystem.Instance;
            var local = session != null
                ? EntityManager.Instance?.GetEntity(session.LocalPlayerId)
                : null;
            Vector3 from = local?.GetComponent<TransformComponent>()?.Position ?? hit;
            Vector3 dir = hit - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            frame.AimDir = dir.normalized;
            frame.HasAimDir = true;
            hit.y = GameConstants.GroundY;
            frame.AimPoint = hit;
            frame.HasAimPoint = true;
        }

        static void CollectConfirmAndTap(ref PlayerInputFrame frame)
        {
            int aimId = frame.BeginAimSkillId > 0
                ? frame.BeginAimSkillId
                : PlayerInput.ActiveAimSkillId;

            if (frame.SkillPointerUp && aimId > 0 && !SkillRules.NeedsAimDirection(aimId))
                frame.Confirm = true;

            bool lmbDown = Input.GetMouseButtonDown(0);
            bool lmbUp = Input.GetMouseButtonUp(0);
            bool overUi = EventSystem.current != null
                          && EventSystem.current.IsPointerOverGameObject();

            if (aimId > 0 && lmbDown && !overUi)
            {
                frame.Confirm = true;
                _suppressWorldTap = true;
                return;
            }

            if (_suppressWorldTap)
            {
                if (lmbUp)
                    _suppressWorldTap = false;
                return;
            }

            if (aimId > 0) return;
            if (!lmbUp || overUi) return;
            if (PlayerInput.WasUiPointerUp(-1) || PlayerInput.IsReservedFinger(-1))
                return;

            frame.WorldTap = true;
            frame.WorldTapScreen = Input.mousePosition;
        }

        static bool _suppressWorldTap;
    }
}
