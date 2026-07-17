using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UIResponsiveCanvasScaler : MonoBehaviour
    {
        [SerializeField]
        Vector2 desktopReferenceResolution = new(1280f, 720f);

        [SerializeField]
        Vector2 mobileLandscapeReferenceResolution = new(960f, 540f);

        [SerializeField]
        Vector2 mobilePortraitReferenceResolution = new(540f, 960f);

        [SerializeField, Range(0f, 1f)]
        float matchWidthOrHeight = 0.5f;

        CanvasScaler canvasScaler;
        Vector2Int lastScreenSize = new(-1, -1);
        bool lastMobileLayout;

        void Awake()
        {
            canvasScaler = GetComponent<CanvasScaler>();
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
            bool mobileLayout = UsesMobileLayout();
            Vector2Int screenSize = new(screenWidth, screenHeight);

            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            canvasScaler ??= GetComponent<CanvasScaler>();
            Vector2 targetResolution = GetReferenceResolution(screenWidth, screenHeight, mobileLayout);
            if (
                screenSize == lastScreenSize
                && mobileLayout == lastMobileLayout
                && canvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                && canvasScaler.referenceResolution == targetResolution
                && Mathf.Approximately(canvasScaler.matchWidthOrHeight, matchWidthOrHeight)
            )
                return;

            Apply(screenWidth, screenHeight, mobileLayout);
        }

        void Apply(int screenWidth, int screenHeight, bool mobileLayout)
        {
            lastScreenSize = new Vector2Int(screenWidth, screenHeight);
            lastMobileLayout = mobileLayout;

            canvasScaler ??= GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = matchWidthOrHeight;
            canvasScaler.referenceResolution = GetReferenceResolution(
                screenWidth,
                screenHeight,
                mobileLayout
            );
        }

        Vector2 GetReferenceResolution(int screenWidth, int screenHeight, bool mobileLayout)
        {
            if (!mobileLayout)
                return desktopReferenceResolution;

            return screenHeight > screenWidth
                ? mobilePortraitReferenceResolution
                : mobileLandscapeReferenceResolution;
        }

        static bool UsesMobileLayout()
        {
            if (Application.isMobilePlatform)
                return true;

#if UNITY_EDITOR
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget
                is UnityEditor.BuildTarget.Android or UnityEditor.BuildTarget.iOS;
#else
            return false;
#endif
        }
    }
}
