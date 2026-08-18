using UnityEngine;
using UnityEngine.EventSystems;

namespace Client.Battle
{
    /// <summary>小眼睛：按住拖动平移镜头，松手回到跟随主角。</summary>
    public sealed class CameraLookButton : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            PlayerInput.SetLookFinger(eventData.pointerId);
            BattlePanel.LocalFollow?.BeginLook(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != PlayerInput.LookFingerId) return;
            BattlePanel.LocalFollow?.DragLook(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != PlayerInput.LookFingerId) return;
            PlayerInput.MarkUiPointerUp(eventData.pointerId);
            PlayerInput.ClearLookFinger();
            BattlePanel.LocalFollow?.EndLook();
        }

        void OnDisable()
        {
            if (PlayerInput.LookFingerId == int.MinValue) return;
            PlayerInput.ClearLookFinger();
            BattlePanel.LocalFollow?.EndLook();
        }
    }
}
