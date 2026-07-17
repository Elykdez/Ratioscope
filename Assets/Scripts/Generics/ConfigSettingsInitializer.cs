using UnityEngine;

namespace Hypocycloid.Core
{
    [DefaultExecutionOrder(-32000)]
    public sealed class ConfigSettingsInitializer : MonoBehaviour
    {
        void Awake()
        {
            // Apply saved values before normal scene startup reads serialized inspector defaults.
            ConfigSettingsPersistence.LoadForActiveScene();
        }
    }
}
