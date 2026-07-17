using UnityEditor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Hypocycloid.Editor
{
    public class EditorCommons
    {
        /// <summary>
        /// Root menu path for Hypocycloid editor tools.
        /// </summary>
        public const string CTX = "Tools/Hypocycloid/";

        // Also can be used in build setting check.
        [MenuItem(CTX + "Locate Intro Scene", false, 0)]
        static void LocateDefaultScene()
        {
            if (EditorBuildSettings.scenes.Length == 0)
            {
                Debug.LogWarning("No scenes found in the Build Settings, Please add one!");
                return;
            }

            string scenePath = EditorBuildSettings.scenes[0].path;
            if (AssetDatabase.LoadAssetAtPath<Object>(scenePath) is Object sceneAsset)
            {
                EditorGUIUtility.PingObject(sceneAsset);
                Debug.Log("Intro Scene at " + scenePath);
            }
            else
            {
                Debug.LogError("Failed to locate intro scene!");
            }
        }
    }
}
