using TMPro;
using UnityEngine;

namespace Hypocycloid.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class UITip : MonoBehaviour
    {
        public Camera EventCamera { get; private set; }
        public RectTransform CanvasRect { get; private set; }
        public RectTransform TipRect { get; private set; }
        public TMP_Text TipText { get; private set; }

        public void Init()
        {
            TipRect = GetComponent<RectTransform>();
            TipText = GetComponentInChildren<TMP_Text>(true);
            Canvas canvas = GetComponentInParent<Canvas>();

            CanvasRect =
                canvas != null
                    ? canvas.transform as RectTransform
                    : TipRect.parent as RectTransform;

            EventCamera =
                canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : Camera.main;
        }
    }
}
