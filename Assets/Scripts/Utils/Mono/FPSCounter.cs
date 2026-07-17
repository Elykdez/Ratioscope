using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Hypocycloid.Utils
{
    public class FPSCounter : MonoBehaviour
    {
        [SerializeField, Range(0, 60)]
        int lowFPSThreshold;

        [SerializeField, Range(1, 10)]
        int lowFPSTimer;

        float deltaTime = 0.0f;
        float lowFpsTimer = 0f;
        bool lowFpsTriggered = false;

        /// <summary>
        /// Whether FPS counting is currently active.
        /// </summary>
        public bool IsPaused { get; private set; } = false;

        /// <summary>
        /// When true, low-FPS detection is skipped (the FPS readout keeps updating). Set this while
        /// the frame rate is intentionally capped — e.g. during a generation throttle — so the
        /// deliberate low fps doesn't raise a false low-performance warning.
        /// </summary>
        public bool SuppressLowPerf { get; set; }

        public event Action<string> OnFpsUpdate;
        public event Action OnLowPerf;

        public float CurrentFps { get; private set; }

        void Update()
        {
            if (IsPaused)
                return;

            // Ignore abnormal long frames (e.g., file dialog)
            if (Time.unscaledDeltaTime > 0.5f)
                return;

            // --- FPS calculation ---
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;
            CurrentFps = fps;
            OnFpsUpdate?.Invoke($"FPS: {Mathf.RoundToInt(fps)}");

            // --- Low FPS detection ---
            if (!SuppressLowPerf && fps < lowFPSThreshold)
            {
                lowFpsTimer += Time.unscaledDeltaTime;

                if (!lowFpsTriggered && lowFpsTimer >= lowFPSTimer)
                {
                    lowFpsTriggered = true;
                    LogHelper.LogWarning(
                        $"FPS has stayed below {lowFPSThreshold} for {lowFPSTimer} seconds. This may indicate degraded GPU performance or unavailability."
                    );
                    OnLowPerf?.Invoke();
                }
            }
            else
            {
                lowFpsTimer = 0f;
                lowFpsTriggered = false;
            }
        }

        // --- Auto pause/resume when focus or app state changes ---
        void OnApplicationFocus(bool hasFocus)
        {
            IsPaused = !hasFocus;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            IsPaused = pauseStatus;
        }

        public void Toggle(bool isOn)
        {
            ResetState();
            IsPaused = !isOn;
        }

        void ResetState()
        {
            deltaTime = 0;
            lowFpsTimer = 0;
            lowFpsTriggered = false;
        }
    }

    public sealed class PerformanceStatsSampler
    {
        readonly FrameTiming[] frameTimings = new FrameTiming[1];
        string gpuModel;
        float smoothedDeltaTime;
        float cpuFrameMs = -1f;
        float gpuFrameMs = -1f;

        public PerformanceStats Sample()
        {
            gpuModel ??= RuntimeGraphicsInfo.CurrentGpuModel;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime > 0f && deltaTime < 0.5f)
            {
                smoothedDeltaTime =
                    smoothedDeltaTime <= 0f
                        ? deltaTime
                        : smoothedDeltaTime + (deltaTime - smoothedDeltaTime) * 0.1f;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1u, frameTimings) > 0)
            {
                cpuFrameMs = SanitizeMs((float)frameTimings[0].cpuFrameTime);
                gpuFrameMs = SanitizeMs((float)frameTimings[0].gpuFrameTime);
            }

            return new PerformanceStats(
                smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f,
                smoothedDeltaTime * 1000f,
                cpuFrameMs,
                gpuFrameMs,
                Profiler.GetAllocatedMemoryForGraphicsDriver(),
                gpuModel
            );
        }

        static float SanitizeMs(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : -1f;
    }

    public static class RuntimeGraphicsInfo
    {
        const string UnknownGpuModel = "Unknown GPU";
        static string cachedGpuModel;

        public static string CurrentGpuModel
        {
            get
            {
                if (!Application.isPlaying)
                    return UnknownGpuModel;

                cachedGpuModel ??= NormalizeGpuModel(SystemInfo.graphicsDeviceName);
                return cachedGpuModel;
            }
        }

        public static string NormalizeGpuModel(string graphicsDeviceName)
        {
            if (string.IsNullOrWhiteSpace(graphicsDeviceName))
                return UnknownGpuModel;

            string model = graphicsDeviceName.Trim();
            model = StringHelper.RemoveToken(model, "(R)");
            model = StringHelper.RemoveToken(model, "(TM)");
            model = StringHelper.RemoveWord(model, "NVIDIA");
            model = StringHelper.RemoveWord(model, "GeForce");
            model = StringHelper.RemoveWord(model, "AMD");
            model = StringHelper.RemoveWord(model, "Radeon");
            model = StringHelper.RemoveWord(model, "Intel");
            model = StringHelper.RemoveWord(model, "Graphics");
            model = StringHelper.CollapseSpaces(model).Trim('-', ' ');

            return string.IsNullOrWhiteSpace(model) ? graphicsDeviceName.Trim() : model;
        }
    }

    public readonly struct PerformanceStats
    {
        public PerformanceStats(
            float fps,
            float frameLatencyMs,
            float cpuFrameMs,
            float gpuFrameMs,
            long graphicsMemoryBytes,
            string gpuModel
        )
        {
            Fps = fps;
            FrameLatencyMs = frameLatencyMs;
            CpuFrameMs = cpuFrameMs;
            GpuFrameMs = gpuFrameMs;
            GraphicsMemoryBytes = graphicsMemoryBytes;
            GpuModel = gpuModel;
        }

        public float Fps { get; }
        public float FrameLatencyMs { get; }
        public float CpuFrameMs { get; }
        public float GpuFrameMs { get; }
        public long GraphicsMemoryBytes { get; }
        public string GpuModel { get; }
    }
}
