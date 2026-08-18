using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client.Battle
{
    /// <summary>左半屏动态摇杆：按下处弹出底盘，拖动摇杆头，松手复位。</summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        RectTransform _area;
        RectTransform _visual;
        RectTransform _handle;
        float _radius = 80f;
        Vector2 _restPos;
        Camera _uiCam;
        int _pointerId = int.MinValue;

        public void Bind(RectTransform area, RectTransform visual, RectTransform handle, float radius)
        {
            _area = area;
            _visual = visual;
            _handle = handle;
            _radius = radius;
            if (_visual != null)
                _restPos = _visual.anchoredPosition;
            EnsureRaycast();
            CacheCamera();
        }

        void EnsureRaycast()
        {
            if (_area == null) return;
            var image = _area.GetComponent<Image>();
            if (image == null) return;
            image.raycastTarget = true;
        }

        void CacheCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            _uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerId = eventData.pointerId;
            PlayerInput.SetJoystick(Vector2.zero, eventData.pointerId);
            MoveVisualTo(eventData.position);
            Apply(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_pointerId != eventData.pointerId) return;
            Apply(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pointerId != eventData.pointerId) return;
            PlayerInput.MarkUiPointerUp(eventData.pointerId);
            ResetKnob();
        }

        void OnDisable() => ResetKnob();

        void MoveVisualTo(Vector2 screenPos)
        {
            if (_area == null || _visual == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _area, screenPos, _uiCam, out Vector2 local))
                return;

            Vector2 pad = _area.rect.size;
            float margin = _radius + 8f;
            local.x = Mathf.Clamp(local.x, margin, Mathf.Max(margin, pad.x - margin));
            local.y = Mathf.Clamp(local.y, margin, Mathf.Max(margin, pad.y - margin));
            _visual.anchoredPosition = local;
        }

        void Apply(Vector2 screenPos)
        {
            if (_visual == null || _handle == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _visual, screenPos, _uiCam, out Vector2 local))
                return;

            Vector2 clamped = Vector2.ClampMagnitude(local, _radius);
            _handle.anchoredPosition = clamped;
            Vector2 axes = _radius > 0.01f ? clamped / _radius : Vector2.zero;
            PlayerInput.SetJoystick(axes, _pointerId);
        }

        void ResetKnob()
        {
            _pointerId = int.MinValue;
            PlayerInput.ClearJoystick();
            if (_handle != null)
                _handle.anchoredPosition = Vector2.zero;
            if (_visual != null)
                _visual.anchoredPosition = _restPos;
        }
    }
}
