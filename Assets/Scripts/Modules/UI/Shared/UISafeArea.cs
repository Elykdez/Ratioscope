using UnityEngine;

namespace Hypocycloid.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UISafeArea : MonoBehaviour
    {
        RectTransform rectTransform;
        Rect lastSafeArea;
        Vector2Int lastScreenSize = new(-1, -1);

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        void OnEnable()
        {
            if (Application.isPlaying)
                Refresh();
        }

        void Update()
        {
            if (Application.isPlaying)
                Refresh();
        }

        void OnRectTransformDimensionsChange()
        {
            if (Application.isPlaying)
                Refresh();
        }

        void Refresh()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new(screenWidth, screenHeight);

            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            if (screenSize == lastScreenSize && safeArea == lastSafeArea)
                return;

            Apply(safeArea, screenWidth, screenHeight);
        }

        void Apply(Rect safeArea, int screenWidth, int screenHeight)
        {
            lastScreenSize = new Vector2Int(screenWidth, screenHeight);
            lastSafeArea = safeArea;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x = Mathf.Clamp01(anchorMin.x / screenWidth);
            anchorMin.y = Mathf.Clamp01(anchorMin.y / screenHeight);
            anchorMax.x = Mathf.Clamp01(anchorMax.x / screenWidth);
            anchorMax.y = Mathf.Clamp01(anchorMax.y / screenHeight);

            rectTransform ??= GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
        }
    }
}
