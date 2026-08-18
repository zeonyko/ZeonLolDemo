using Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Client.Battle
{
    /// <summary>技能键：按下瞄准；普攻按住吸附锁定，拖动能换目标，松手确认。</summary>
    public sealed class SkillHudAimButton : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        const float StickRadius = 90f;

        public int SkillId;

        RectTransform _rt;
        Camera _uiCam;

        void Awake()
        {
            _rt = transform as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            _uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (SkillId <= 0) return;
            PlayerInput.HudBeginAim(SkillId, eventData.pointerId);
            WriteStick(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (SkillId <= 0) return;
            if (eventData.pointerId != PlayerInput.AimFingerId) return;
            WriteStick(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (SkillId <= 0) return;
            bool cancel = SkillRules.NeedsAimDirection(SkillId)
                          && BattlePanel.IsOverAimCancel(eventData.position);
            PlayerInput.HudSkillPointerUp(eventData.pointerId, cancel);
        }

        void WriteStick(Vector2 screenPos)
        {
            if (_rt == null || !UsesAimStick())
            {
                PlayerInput.HudSetStick(Vector2.zero);
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rt, screenPos, _uiCam, out Vector2 local))
                return;

            Vector2 stick = StickRadius > 0.01f
                ? Vector2.ClampMagnitude(local / StickRadius, 1f)
                : Vector2.zero;
            PlayerInput.HudSetStick(stick);
        }

        bool UsesAimStick()
        {
            var def = SkillCatalog.Get(SkillId);
            return def != null && (def.IsAutoAttack || SkillRules.NeedsAimDirection(def));
        }
    }
}
