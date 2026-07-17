using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Hypocycloid.Editor
{
    public class SpriteAtlasBuilderWindow : EditorWindow
    {
        const string AtlasFolder = "Assets/Bundles/Texture/Atlas";
        const string DefaultSourceFolder = "Assets/Bundles/Texture/Sprites";
        const string PREF_KEY = "SpriteAtlasBuilder_Folders";
        const string PREF_KEY_AUTOBUILD = "SpriteAtlasBuilder_AutoBuildBeforeBuild";

        readonly List<DefaultAsset> folderAssets = new();
        Vector2 scrollPos;
        bool autoBuildOnBuild = false;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            var existingHandler = EditorHelper.GetBuildPlayerHandler(
                out var success,
                out var buildPlayerHandlerField
            );

            if (
                existingHandler != null
                && existingHandler.Method.DeclaringType == typeof(SpriteAtlasBuilderWindow)
            )
            {
                existingHandler = null;
            }

            Action<BuildPlayerOptions> handler = options => OnBuildPlayer(options, existingHandler);

            if (success && buildPlayerHandlerField != null)
            {
                buildPlayerHandlerField.SetValue(null, handler);
            }
            else
            {
                BuildPlayerWindow.RegisterBuildPlayerHandler(handler);
            }
        }

        [MenuItem(EditorCommons.CTX + "Assets/Atlas Packer")]
        public static void ShowWindow()
        {
            var window = GetWindow<SpriteAtlasBuilderWindow>("Atlas Packer");
            window.minSize = new Vector2(400, 300);
            window.LoadFolders();
            window.autoBuildOnBuild = EditorPrefs.GetBool(PREF_KEY_AUTOBUILD, false);
        }

        void OnGUI()
        {
            bool newValue = EditorGUILayout.ToggleLeft(
                "Pack atlases before build",
                autoBuildOnBuild
            );
            if (newValue != autoBuildOnBuild)
            {
                autoBuildOnBuild = newValue;
                EditorPrefs.SetBool(PREF_KEY_AUTOBUILD, autoBuildOnBuild);
            }
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Generated sprite atlas assets are written to " + AtlasFolder + ".",
                MessageType.Info
            );
            GUILayout.Label("Source Folders", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            for (int i = 0; i < folderAssets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                folderAssets[i] = (DefaultAsset)
                    EditorGUILayout.ObjectField(folderAssets[i], typeof(DefaultAsset), false);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    folderAssets.RemoveAt(i);
                    SaveFolders();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("+ Add Folder"))
            {
                folderAssets.Add(null);
            }

            EditorGUILayout.Space(5);
            if (GUILayout.Button("Pack Atlases Now"))
            {
                BuildAllAtlases(true);
            }
            EditorGUILayout.Space(10);
        }

        void BuildAllAtlases(bool showCompletionDialog)
        {
            EnsureAtlasFolder();

            var builtAtlases = new List<SpriteAtlas>();
            int skippedCount = 0;
            foreach (var asset in folderAssets.ToList())
            {
                if (asset == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(asset);
                if (!AssetDatabase.IsValidFolder(path))
                {
                    Debug.LogWarning($"Skipping invalid atlas source folder: {path}");
                    skippedCount++;
                    continue;
                }

                if (TryBuildAtlas(path, builtAtlases, out _))
                    continue;

                skippedCount++;
            }

            if (builtAtlases.Count > 0)
            {
                SpriteAtlasUtility.PackAtlases(
                    builtAtlases.ToArray(),
                    EditorUserBuildSettings.activeBuildTarget,
                    false
                );
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Atlas packing finished. Built {builtAtlases.Count} atlas asset(s) in {AtlasFolder}; skipped {skippedCount} source folder(s)."
            );

            if (showCompletionDialog)
            {
                EditorUtility.DisplayDialog(
                    "Atlas Packing Complete",
                    $"Built {builtAtlases.Count} atlas asset(s) in:\n{AtlasFolder}\n\nSkipped: {skippedCount}",
                    "OK"
                );
            }
        }

        bool TryBuildAtlas(
            string sourceFolderPath,
            List<SpriteAtlas> builtAtlases,
            out int spriteCount
        )
        {
            spriteCount = 0;

            string folderName = new DirectoryInfo(sourceFolderPath).Name;
            string atlasName = folderName + ".spriteatlas";
            string atlasPath = Path.Combine(AtlasFolder, atlasName).Replace('\\', '/');
            var packables = CollectSpritePackables(sourceFolderPath);

            if (packables.Count == 0)
            {
                Debug.LogWarning($"No sprites found in atlas source folder: {sourceFolderPath}");
                return false;
            }

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, atlasPath);
            }
            else
            {
                Object[] oldPackables = SpriteAtlasExtensions.GetPackables(atlas);
                if (oldPackables.Length > 0)
                    atlas.Remove(oldPackables);
            }

            atlas.SetIncludeInBuild(true);
            atlas.SetPackingSettings(
                new SpriteAtlasPackingSettings
                {
                    blockOffset = 1,
                    padding = 4,
                    enableRotation = false,
                    enableTightPacking = false,
                    enableAlphaDilation = true,
                }
            );

            atlas.SetTextureSettings(
                new SpriteAtlasTextureSettings
                {
                    readable = false,
                    generateMipMaps = false,
                    sRGB = true,
                    filterMode = FilterMode.Bilinear,
                }
            );

            SpriteAtlasExtensions.Add(atlas, packables.ToArray());
            EditorUtility.SetDirty(atlas);
            builtAtlases.Add(atlas);
            spriteCount = packables.Count;
            Debug.Log(
                $"Packed atlas: {atlasPath}. Sprites: {spriteCount}. Source: {sourceFolderPath}"
            );
            return true;
        }

        static List<Object> CollectSpritePackables(string sourceFolderPath)
        {
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { sourceFolderPath });
            var sprites = new List<Object>();
            var seen = new HashSet<int>();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsPathUnderFolder(assetPath, sourceFolderPath))
                    continue;

                foreach (
                    Sprite sprite in AssetDatabase
                        .LoadAllAssetRepresentationsAtPath(assetPath)
                        .OfType<Sprite>()
                )
                {
                    if (seen.Add(sprite.GetInstanceID()))
                        sprites.Add(sprite);
                }

                Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (mainSprite != null && seen.Add(mainSprite.GetInstanceID()))
                    sprites.Add(mainSprite);
            }

            return sprites;
        }

        static bool IsPathUnderFolder(string assetPath, string folderPath)
        {
            string normalizedAssetPath = assetPath.Replace('\\', '/');
            string normalizedFolderPath = folderPath.TrimEnd('/', '\\').Replace('\\', '/');
            return normalizedAssetPath.Equals(
                    normalizedFolderPath,
                    StringComparison.OrdinalIgnoreCase
                )
                || normalizedAssetPath.StartsWith(
                    normalizedFolderPath + "/",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        static void EnsureAtlasFolder()
        {
            if (AssetDatabase.IsValidFolder(AtlasFolder))
                return;

            string[] parts = AtlasFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        void SetAddressable(string assetPath, string label)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables is not enabled. Create Addressables settings first.");
                return;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = Path.GetFileNameWithoutExtension(assetPath);
            entry.SetLabel(label, true);
        }

        void OnDisable()
        {
            SaveFolders();
            EditorPrefs.SetBool(PREF_KEY_AUTOBUILD, autoBuildOnBuild);
        }

        void SaveFolders()
        {
            var paths = folderAssets
                .Where(asset => asset != null)
                .Select(asset => AssetDatabase.GetAssetPath(asset))
                .ToArray();

            string data = string.Join(";", paths);
            EditorPrefs.SetString(PREF_KEY, data);
        }

        void LoadFolders()
        {
            folderAssets.Clear();
            if (!EditorPrefs.HasKey(PREF_KEY))
            {
                AddSourceFolderIfValid(DefaultSourceFolder);
                return;
            }

            string data = EditorPrefs.GetString(PREF_KEY);
            string[] paths = data.Split(';');
            foreach (var path in paths)
            {
                AddSourceFolderIfValid(path);
            }
        }

        void AddSourceFolderIfValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !AssetDatabase.IsValidFolder(path))
                return;

            var asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            if (asset != null && !folderAssets.Contains(asset))
                folderAssets.Add(asset);
        }

        static void OnBuildPlayer(
            BuildPlayerOptions options,
            Action<BuildPlayerOptions> buildHandler
        )
        {
            bool autoBuild = EditorPrefs.GetBool(PREF_KEY_AUTOBUILD, false);
            if (autoBuild)
            {
                if (
                    !EditorUtility.DisplayDialog(
                        "Pack Atlases",
                        "Pack sprite atlases before starting the build?",
                        "Pack and Continue",
                        "Cancel Build"
                    )
                )
                {
                    Debug.LogWarning("Build aborted: atlas packing was canceled.");
                    return;
                }

                var window = CreateInstance<SpriteAtlasBuilderWindow>();
                window.LoadFolders();
                window.BuildAllAtlases(false);
            }

            (buildHandler ?? BuildPlayerWindow.DefaultBuildMethods.BuildPlayer).Invoke(options);
        }
    }
}
