using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hypocycloid.Core;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace Hypocycloid.Ratioscope
{
    [Serializable]
    public sealed class LlmModelManifest
    {
        public string version;
        public string baseUrl;
        public LlmModelManifestEntry[] entries;

        public LlmModelManifestEntry Find(string fileName)
        {
            if (entries == null)
                return null;
            foreach (LlmModelManifestEntry entry in entries)
            {
                if (
                    entry != null
                    && string.Equals(entry.fileName, fileName, StringComparison.OrdinalIgnoreCase)
                )
                    return entry;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class LlmModelManifestEntry
    {
        public string fileName;
        public long byteSize;
        public string sha256;
        public string relativeUrl;
    }

    public static class LlmModelCatalog
    {
        public static string LocalDirectory =>
            Path.Combine(Application.persistentDataPath, "Models");

        public static string LocalPath(string fileName) => Path.Combine(LocalDirectory, fileName);

        public static string ResolvePath(string bundledDirectory, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            string localPath = LocalPath(fileName);
            return File.Exists(localPath) ? localPath : Path.Combine(bundledDirectory, fileName);
        }

        public static void EnsureLocalDirectory()
        {
            Directory.CreateDirectory(LocalDirectory);
        }
    }

    /// <summary>
    /// Downloads LLM artifacts declared by StreamingAssets/config/models.json. Downloads use a
    /// temporary file and are verified before replacing the persistent model cache.
    /// </summary>
    [DisallowMultipleComponent]
    [ConfigSettings("ui_ai_model")]
    public sealed class LlmModelDownloader : MonoBehaviour
    {
        [SerializeField]
        LlmSystemSettings systemSettings;

        [SerializeField]
        CortexCore cortexCore;

        [SerializeField]
        [Tooltip("Overrides models.json baseUrl when non-empty.")]
        string baseUrlOverride = "";

        public bool IsDownloading { get; private set; }
        public float Progress { get; private set; }
        public string StatusText { get; private set; } = "Model status unavailable";

        public event Action DownloadStarted;
        public event Action<string, float> DownloadStatusChanged;
        public event Action DownloadFinished;
        public event Action ModelSetChanged;

        void Awake()
        {
            if (RuntimeDataPaths.IsExtractionComplete)
                RefreshIdleStatus();
            else
                StatusText = "Preparing model configuration";
        }

        [ConfigSetting("ui_download_models", priority: 10)]
        public void DownloadMissingModels()
        {
            if (!IsDownloading)
                StartCoroutine(DownloadMissingRoutine());
        }

        [ConfigSetting("ui_ai_model", priority: 9, resync: true)]
        public string ModelStatus => StatusText;

        [ConfigSetting("ui_open_model_config", priority: 8)]
        public void OpenModelConfig()
        {
            if (systemSettings == null)
            {
                StatusText = "LLM System Settings is not assigned";
                return;
            }

            string path = systemSettings.ModelManifestPath;

#if UNITY_EDITOR || UNITY_STANDALONE
            // Editor and desktop expose a real filesystem: reveal models.json in the OS browser.
            if (!File.Exists(path))
            {
                StatusText = "Model config not found";
                LogHelper.LogWarning($"[LlmModelDownloader] Model config not found at {path}");
                return;
            }

            SystemHelper.RevealFile(path);
#else
            // Mobile has no user-facing browser for app storage, so report where the config
            // lives instead of attempting an external open that cannot work here.
            StatusText = "Model config path shown in log";
            LogHelper.Log(
                $"[LlmModelDownloader] Model config: {path}\n{DescribeMobileConfigAccess()}"
            );
#endif
        }

#if !UNITY_EDITOR && !UNITY_STANDALONE
        static string DescribeMobileConfigAccess()
        {
#if UNITY_ANDROID
            return "On Android it sits in the app's private storage under "
                + "Android/data/<package>/files/Streaming/config/models.json. Open it with a file "
                + "manager that can browse app data; edits apply on next launch and are reset by "
                + "an app update.";
#elif UNITY_IOS
            return "On iOS the config ships read-only inside the app bundle and cannot be edited "
                + "on the device. Change models.json in the project and reinstall the build.";
#else
            return "This platform has no user-accessible location for the model config.";
#endif
        }
#endif

        public void RefreshIdleStatus()
        {
            if (IsDownloading || systemSettings == null)
                return;

            try
            {
                LlmModelManifest manifest = systemSettings.LoadManifest();
                LlmModelManifestEntry entry = GetSelectedEntry(manifest);
                StatusText = systemSettings.IsModelAvailable(entry.fileName)
                    ? $"Model ready: {entry.fileName}"
                    : $"Download {entry.fileName}{FormatSize(entry.byteSize)}";
            }
            catch (Exception exception)
            {
                StatusText = "Model manifest unavailable";
                LogHelper.LogError($"[LlmModelDownloader] {exception.Message}");
            }
        }

        IEnumerator DownloadMissingRoutine()
        {
            if (systemSettings == null)
            {
                StatusText = "LLM System Settings is not assigned";
                yield break;
            }

            IsDownloading = true;
            Progress = 0f;
            DownloadStarted?.Invoke();
            bool anyError = false;
            try
            {
                LlmModelManifest manifest;
                try
                {
                    manifest = systemSettings.LoadManifest();
                }
                catch (Exception exception)
                {
                    StatusText = "Model manifest unavailable";
                    LogHelper.LogError($"[LlmModelDownloader] {exception.Message}");
                    yield break;
                }

                LlmModelManifestEntry entry;
                try
                {
                    entry = GetSelectedEntry(manifest);
                }
                catch (Exception exception)
                {
                    StatusText = "Selected model is not configured";
                    LogHelper.LogError($"[LlmModelDownloader] {exception.Message}");
                    yield break;
                }

                LlmModelCatalog.EnsureLocalDirectory();
                string baseUrl = string.IsNullOrWhiteSpace(baseUrlOverride)
                    ? manifest.baseUrl
                    : baseUrlOverride;

                if (!systemSettings.IsModelAvailable(entry.fileName))
                {
                    string url = ResolveUrl(baseUrl, entry.relativeUrl);
                    if (url == null)
                    {
                        anyError = true;
                        StatusText = $"No download URL configured for {entry.fileName}";
                        LogHelper.LogError(
                            $"[LlmModelDownloader] No URL configured for {entry.fileName}."
                        );
                        yield break;
                    }

                    bool downloaded = false;
                    yield return DownloadOne(entry, url, result => downloaded = result);
                    if (downloaded)
                        ModelSetChanged?.Invoke();
                    else
                        anyError = true;
                }

                StatusText = anyError
                    ? $"{entry.fileName} download failed"
                    : $"Model ready: {entry.fileName}";
                PublishDownloadStatus();
            }
            finally
            {
                IsDownloading = false;
                Progress = 0f;
                DownloadFinished?.Invoke();
                ModelSetChanged?.Invoke();
            }
        }

        IEnumerator DownloadOne(LlmModelManifestEntry entry, string url, Action<bool> completed)
        {
            string finalPath = LlmModelCatalog.LocalPath(entry.fileName);
            string temporaryPath = finalPath + ".part";
            DeleteIfExists(temporaryPath);
            StatusText = $"Downloading {entry.fileName} 0%";
            PublishDownloadStatus();

            using (UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(temporaryPath)
                {
                    removeFileOnAbort = true,
                };
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    Progress = request.downloadProgress;
                    StatusText =
                        $"Downloading {entry.fileName} {Mathf.RoundToInt(Progress * 100f)}%";
                    PublishDownloadStatus();
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    StatusText = $"{entry.fileName} download failed: {request.error}";
                    PublishDownloadStatus();
                    LogHelper.LogError(
                        $"[LlmModelDownloader] {entry.fileName} <{url}>: {request.error}"
                    );
                    DeleteIfExists(temporaryPath);
                    completed(false);
                    yield break;
                }
            }

            Progress = 0f;
            StatusText = $"Verifying {entry.fileName}...";
            PublishDownloadStatus();
            Task<bool> verification = Task.Run(
                () => VerifyFile(temporaryPath, entry.sha256, entry.byteSize)
            );
            while (!verification.IsCompleted)
                yield return null;

            if (verification.IsFaulted || !verification.Result)
            {
                StatusText = $"{entry.fileName} verification failed";
                PublishDownloadStatus();
                LogHelper.LogError(
                    $"[LlmModelDownloader] {entry.fileName} checksum/size mismatch; discarded."
                );
                DeleteIfExists(temporaryPath);
                completed(false);
                yield break;
            }

            try
            {
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(temporaryPath, finalPath);
                completed(true);
            }
            catch (Exception exception)
            {
                StatusText = $"{entry.fileName} could not be finalized";
                PublishDownloadStatus();
                LogHelper.LogError(
                    $"[LlmModelDownloader] {entry.fileName} finalize failed: {exception.Message}"
                );
                DeleteIfExists(temporaryPath);
                completed(false);
            }
        }

        void PublishDownloadStatus() => DownloadStatusChanged?.Invoke(StatusText, Progress);

        LlmModelManifestEntry GetSelectedEntry(LlmModelManifest manifest)
        {
            LlmSystemOption option =
                cortexCore != null
                    ? cortexCore.SystemOption
                    : systemSettings.SelectForCurrentSystem();
            string fileName = systemSettings.GetPreferredArtifactFileName(option);
            return manifest.Find(fileName)
                ?? throw new InvalidDataException(
                    $"The manifest has no entry for {option} artifact '{fileName}'."
                );
        }

        static string FormatSize(long bytes) =>
            bytes > 0 ? $" ({bytes / (1024d * 1024d * 1024d):0.0} GiB)" : "";

        static bool VerifyFile(string path, string expectedSha256, long expectedSize)
        {
            try
            {
                FileInfo info = new(path);
                if (!info.Exists || (expectedSize > 0 && info.Length != expectedSize))
                    return false;
                if (string.IsNullOrWhiteSpace(expectedSha256))
                    return true;

                using FileStream stream = File.OpenRead(path);
                using SHA256 sha = SHA256.Create();
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                return string.Equals(
                    actual,
                    expectedSha256.Trim(),
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        static string ResolveUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return null;
            if (
                relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            )
                return relativeUrl;
            return string.IsNullOrWhiteSpace(baseUrl)
                ? null
                : baseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
        }

        static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                LogHelper.LogError(
                    $"[LlmModelDownloader] Could not delete '{path}': {exception.Message}"
                );
            }
        }
    }
}
