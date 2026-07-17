using System.Collections;
using System.IO;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace Hypocycloid.Core
{
    /// <summary>
    /// Resolves the runtime locations of shipped data files.
    ///
    /// On desktop, StreamingAssets is a real directory and plain file IO works. On Android it
    /// lives inside the APK, so it is neither enumerable nor readable through System.IO and is
    /// never writable. This extracts the few files the runtime opens directly into
    /// persistentDataPath once per build, after which the existing synchronous file IO works
    /// unchanged on both platforms.
    /// </summary>
    public static class RuntimeDataPaths
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // StreamingAssets entries opened through System.IO rather than UnityWebRequest.
        // Models are downloaded directly to persistentDataPath and are never extracted
        // from the APK. Docs and update.json already use UnityWebRequest.
        static readonly string[] ExtractedFiles =
        {
            "config/models.json",
            "Llm/llm_tokenizer.bin",
        };

        const string ExtractedMarkerName = ".extracted";

        public static string StreamingRoot =>
            Path.Combine(Application.persistentDataPath, "Streaming");

        public static string WritableRoot => Application.persistentDataPath;

        // Version-stamped so an app update re-extracts rather than reusing the previous
        // build's files.
        public static bool IsExtractionComplete
        {
            get
            {
                string marker = Path.Combine(StreamingRoot, ExtractedMarkerName);
                return File.Exists(marker) && File.ReadAllText(marker) == Application.version;
            }
        }

        /// <summary>
        /// Copies the directly-opened StreamingAssets entries out of the APK. Runs once per
        /// installed version.
        /// </summary>
        public static IEnumerator ExtractIfNeeded()
        {
            if (IsExtractionComplete)
                yield break;

            string root = StreamingRoot;
            Directory.CreateDirectory(root);

            foreach (string relativePath in ExtractedFiles)
            {
                string destination = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));

                string source = $"{Application.streamingAssetsPath}/{relativePath}";
                using UnityWebRequest request = UnityWebRequest.Get(source);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    LogHelper.LogError(
                        $"[RuntimeDataPaths] Failed to extract '{relativePath}': {request.error}"
                    );
                    yield break;
                }

                File.WriteAllBytes(destination, request.downloadHandler.data);
            }

            // Written last so a failed or interrupted extraction is retried next launch.
            File.WriteAllText(Path.Combine(root, ExtractedMarkerName), Application.version);
            LogHelper.Log($"[RuntimeDataPaths] Extracted {ExtractedFiles.Length} files to {root}");
        }
#else
        /// <summary>
        /// Root to read shipped data from. Mirrors the StreamingAssets layout on every platform.
        /// </summary>
        public static string StreamingRoot => Application.streamingAssetsPath;

        /// <summary>
        /// Root for files the app writes at runtime. StreamingAssets is read-only on Android.
        /// </summary>
        public static string WritableRoot => Application.streamingAssetsPath;

        public static bool IsExtractionComplete => true;

        public static IEnumerator ExtractIfNeeded()
        {
            yield break;
        }
#endif
    }
}
