using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hypocycloid.Editor
{
    /// <summary>
    /// Keeps Sentis graphs out of Android builds.
    ///
    /// Unity packs everything under StreamingAssets into the player. Android obtains its
    /// selected CPU graph through LlmModelDownloader instead, so no .sentis file belongs in
    /// the APK regardless of its current size.
    ///
    /// The files are moved into Library for the duration of the build and moved back afterwards.
    /// A failed build leaves them stashed, so RestoreLeftovers also runs on domain reload.
    /// </summary>
    public sealed class AndroidStreamingAssetsFilter
        : IPreprocessBuildWithReport,
            IPostprocessBuildWithReport
    {
        const string StreamingAssetsPath = "Assets/StreamingAssets";
        const string SentisPath = StreamingAssetsPath + "/Sentis";
        const string StashFolderName = "AndroidExcludedStreamingAssets";

        public int callbackOrder => 0;

        static string StashRoot =>
            Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".",
                "Library",
                StashFolderName
            );

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            if (!Directory.Exists(SentisPath))
                return;

            foreach (
                string path in Directory.GetFiles(
                    SentisPath,
                    "*.sentis",
                    SearchOption.AllDirectories
                )
            )
            {
                Stash(path);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            RestoreAll();
        }

        [InitializeOnLoadMethod]
        static void RestoreLeftovers()
        {
            if (Directory.Exists(StashRoot))
                RestoreAll();
        }

        static void Stash(string assetPath)
        {
            string relative = assetPath.Substring(StreamingAssetsPath.Length).TrimStart('/', '\\');
            string destination = Path.Combine(StashRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));

            MoveWithMeta(assetPath, destination);
            Debug.Log($"[Android] Excluded from build: {relative}");
        }

        static void RestoreAll()
        {
            if (!Directory.Exists(StashRoot))
                return;

            foreach (string path in Directory.GetFiles(StashRoot, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".meta"))
                    continue;

                string relative = path.Substring(StashRoot.Length).TrimStart('/', '\\');
                string destination = Path.Combine(StreamingAssetsPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));

                MoveWithMeta(path, destination);
                Debug.Log($"[Android] Restored to StreamingAssets: {relative}");
            }

            Directory.Delete(StashRoot, true);
            AssetDatabase.Refresh();
        }

        // The .meta travels with the file so Unity does not reassign a GUID on restore.
        static void MoveWithMeta(string source, string destination)
        {
            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(source, destination);

            string sourceMeta = source + ".meta";
            if (!File.Exists(sourceMeta))
                return;

            string destinationMeta = destination + ".meta";
            if (File.Exists(destinationMeta))
                File.Delete(destinationMeta);
            File.Move(sourceMeta, destinationMeta);
        }
    }
}
