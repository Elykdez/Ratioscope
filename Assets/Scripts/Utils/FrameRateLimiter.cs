using UnityEngine;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// Caps the render loop at a fixed frame rate. vSync is off in the project quality
    /// settings, so Unity would otherwise render uncapped - wasting GPU and starving the
    /// single WMF video-decode / AI-inference budget the compositor depends on. Applied
    /// once at startup; targetFrameRate then persists across scene loads.
    /// </summary>
    public static class FrameRateLimiter
    {
        public const int MaxFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            // targetFrameRate is only honored when vSync is off; assert it so the cap holds
            // even if a quality level enables vSync.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = MaxFrameRate;
        }
    }
}
