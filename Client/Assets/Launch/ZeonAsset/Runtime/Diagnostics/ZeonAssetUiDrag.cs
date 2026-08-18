using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.ZeonAsset
{
    /// <summary>拖标题栏移动调试窗口，接近原先 IMGUI 的手感。</summary>
    public sealed class ZeonAssetUiDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Target;

        private Vector2 _grab;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target == null || Target.parent == null)
                return;
            var parent = Target.parent as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var local))
                return;
            _grab = Target.anchoredPosition - local;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null || Target.parent == null)
                return;
            var parent = Target.parent as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var local))
                return;
            Target.anchoredPosition = local + _grab;
        }
    }
}
