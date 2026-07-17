using IngameDebugConsole;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Opens the in-game debug console when the device is shaken. The console popup is set to Never,
    /// so this is the only way to summon it on a touch device. Bootstraps itself into a persistent GameObject at startup;
    /// no scene wiring required.
    /// </summary>
    public sealed class ShakeToOpenConsole : MonoBehaviour
    {
        // Acceleration magnitude (in g) that counts as a shake. Resting is ~1g (gravity).
        const float ShakeThreshold = 2.5f;

        // Minimum seconds between triggers so one shake does not fire repeatedly.
        const float Cooldown = 1f;

        float nextAllowedTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Shake-to-open only makes sense on a touch device with no keyboard to toggle the
            // console. A hardware keyboard means the user has a better input path already.
            if (!Application.isMobilePlatform || Keyboard.current != null)
                return;

            GameObject host = new GameObject(nameof(ShakeToOpenConsole));
            host.AddComponent<ShakeToOpenConsole>();
            DontDestroyOnLoad(host);
        }

        void OnEnable()
        {
            // Sensors are disabled by default under the Input System; enable the accelerometer.
            if (Accelerometer.current != null && !Accelerometer.current.enabled)
                InputSystem.EnableDevice(Accelerometer.current);
        }

        void Update()
        {
            Accelerometer accelerometer = Accelerometer.current;
            if (accelerometer == null || Time.unscaledTime < nextAllowedTime)
                return;

            float magnitude = accelerometer.acceleration.ReadValue().magnitude;
            if (magnitude < ShakeThreshold)
                return;

            nextAllowedTime = Time.unscaledTime + Cooldown;
            if (DebugLogManager.Instance != null)
                DebugLogManager.Instance.ShowLogWindow();
        }
    }
}
