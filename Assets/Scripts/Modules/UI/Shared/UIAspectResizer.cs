using UnityEngine;

namespace Hypocycloid.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UIAspectResizer : MonoBehaviour
    {
        // Stage.unity relies on this component to size the preview RawImage.
        // If the script is missing during a Unity save, the serialized value can be
        // stripped and the RectTransform can stay at 0x0, making the UI appear gone.
        [SerializeField]
        float initialAspect = 1.6f;

        RectTransform rectTransform;

#if UNITY_EDITOR
        DrivenRectTransformTracker tracker;
#endif

        RectTransform Trans
        {
            get
            {
                if (rectTransform == null)
                    rectTransform = GetComponent<RectTransform>();
                return rectTransform;
            }
        }

        void OnEnable()
        {
            EnsureInitialAspect();
            Resize();
        }

        void Start()
        {
            EnsureInitialAspect();
            Resize();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            tracker.Clear();
#endif
        }

        void OnRectTransformDimensionsChange()
        {
            if (!Application.isPlaying)
            {
                EnsureInitialAspect();
                Resize();
            }
        }

        void LateUpdate()
        {
            Resize();
        }

        void EnsureInitialAspect()
        {
            if (initialAspect > 0f)
                return;

            Rect rect = Trans.rect;
            if (rect.width > 0f && rect.height > 0f)
                initialAspect = rect.width / rect.height;
            else
                initialAspect = 1.6f;
        }

        void Resize()
        {
            EnsureInitialAspect();

            float screenHeight = Mathf.Max(1f, Screen.height);
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenAspectRatio = screenWidth / screenHeight;
            float width;
            float height;

            if (initialAspect > screenAspectRatio)
            {
                width = screenWidth;
                height = width / initialAspect;
            }
            else
            {
                height = screenHeight;
                width = height * initialAspect;
            }

            RectTransform trans = Trans;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                tracker.Clear();
                tracker.Add(this, trans, DrivenTransformProperties.SizeDelta);
            }
#endif

            trans.sizeDelta = new Vector2(width, height);
        }
    }
}
