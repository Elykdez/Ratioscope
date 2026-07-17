using UnityEngine;
using UnityEngine.EventSystems;

namespace Hypocycloid.UI
{
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Range(0f, 1f)]
        public float alphaFade = 0.6f;

        RectTransform rectTransform;
        CanvasGroup canvasGroup;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Makes the item transparent-ish and ignore raycasts so it doesn't block "Drop" targets
            canvasGroup.alpha = alphaFade;
            canvasGroup.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Updates the position based on the mouse/touch delta
            rectTransform.anchoredPosition +=
                eventData.delta / GetComponentInParent<Canvas>().scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            canvasGroup.alpha = 1.0f;
            canvasGroup.blocksRaycasts = true;
        }
    }
}
