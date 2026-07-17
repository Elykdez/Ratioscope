using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hypocycloid.Editor
{
    /// <summary>
    /// Applies the Android player settings this project expects. Written as a script rather
    /// than hand-edited into ProjectSettings.asset so it stays reproducible and can run from
    /// CI through -executeMethod.
    ///
    /// Keystore secrets are read from the environment so they never land in the repository.
    /// Leave them unset to fill the passwords in via the Editor's publishing settings instead.
    /// </summary>
    public static class AndroidPlayerSetup
    {
        const string ApplicationIdentifier = "com.elykdez.ratioscope";
        const string KeystorePath = @"C:\Users\Elykdez\hypozykloid.keystore";

        const string EnvKeystorePass = "RATIOSCOPE_KEYSTORE_PASS";
        const string EnvKeyaliasName = "RATIOSCOPE_KEYALIAS_NAME";
        const string EnvKeyaliasPass = "RATIOSCOPE_KEYALIAS_PASS";

        [MenuItem("Application/Android/Apply Player Settings")]
        public static void Apply()
        {
            ApplyOrientation();
            ApplyIdentity();
            ApplyScripting();
            ApplyGraphics();
            ApplyKeystore();

            AssetDatabase.SaveAssets();
            Debug.Log("[Android] Player settings applied.");
        }

        static void ApplyOrientation()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
        }

        static void ApplyIdentity()
        {
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                ApplicationIdentifier
            );

            // The app checks for updates over the network; without this the permission is only
            // added when Unity's static analysis happens to spot the usage.
            PlayerSettings.Android.forceInternetPermission = true;
        }

        static void ApplyScripting()
        {
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Mirror the desktop build so platform-conditional code compiles the same way
            // rather than silently diverging.
            PlayerSettings.SetApiCompatibilityLevel(
                NamedBuildTarget.Android,
                PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone)
            );
            PlayerSettings.SetScriptingDefineSymbols(
                NamedBuildTarget.Android,
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)
            );

            // android-34 is the only SDK platform installed; Play Store submission needs a
            // newer one installed and this raised to match.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        }

        static void ApplyGraphics()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 }
            );
        }

        static void ApplyKeystore()
        {
            if (!File.Exists(KeystorePath))
            {
                Debug.LogWarning(
                    $"[Android] Keystore not found at {KeystorePath}; signing left unconfigured."
                );
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = KeystorePath;

            string aliasName = Environment.GetEnvironmentVariable(EnvKeyaliasName);
            string keystorePass = Environment.GetEnvironmentVariable(EnvKeystorePass);
            string aliasPass = Environment.GetEnvironmentVariable(EnvKeyaliasPass);

            if (string.IsNullOrEmpty(aliasName) || string.IsNullOrEmpty(keystorePass))
            {
                Debug.LogWarning(
                    $"[Android] Keystore path set. Provide {EnvKeyaliasName}, {EnvKeystorePass} "
                        + $"and {EnvKeyaliasPass}, or enter them under Player Settings > "
                        + "Publishing Settings before building a signed APK."
                );
                return;
            }

            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = aliasName;
            PlayerSettings.Android.keyaliasPass = string.IsNullOrEmpty(aliasPass)
                ? keystorePass
                : aliasPass;
        }
    }
}
