using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UIAutoResize : MonoBehaviour
    {
        public bool adjustHeight = true;
        public bool adjustWidth = false;
        public float padding = 0f;

        RectTransform rectTransform;
        Canvas rootCanvas;

        void OnEnable()
        {
            rectTransform = GetComponent<RectTransform>();
            rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            if (rootCanvas != null)
            {
                Canvas.willRenderCanvases += HandleResizing;
            }
        }

        void OnDisable()
        {
            if (rootCanvas != null)
            {
                Canvas.willRenderCanvases -= HandleResizing;
            }
        }

        void OnDestroy()
        {
            if (rootCanvas != null)
            {
                Canvas.willRenderCanvases -= HandleResizing;
            }
        }

        void HandleResizing()
        {
            if (!isActiveAndEnabled)
                return;
            Resize();
        }

        void Resize()
        {
            float totalHeight = 0f;
            float maxWidth = 0f;

            foreach (RectTransform child in transform)
            {
                if (!child.gameObject.activeSelf)
                    continue;

                LayoutElement le = child.GetComponent<LayoutElement>();
                if (le != null && le.ignoreLayout)
                    continue;

                // Get ContentSizeFitter if it exists
                ContentSizeFitter fitter = child.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(child);
                }

                Vector2 childSize = child.rect.size;
                totalHeight += childSize.y;
                maxWidth = Mathf.Max(maxWidth, childSize.x);
            }

            if (adjustHeight)
                rectTransform.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    totalHeight + padding
                );

            if (adjustWidth)
                rectTransform.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    maxWidth + padding
                );
        }
    }
}
