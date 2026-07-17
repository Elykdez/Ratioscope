using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    public static class GlobalSetups
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnableExitOnBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Input.backButtonLeavesApp = true;
#endif
        }
    }
}
