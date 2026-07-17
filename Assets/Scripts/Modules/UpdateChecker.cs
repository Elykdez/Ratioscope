using System;
using System.Collections;
using System.Text;
using Hypocycloid.Core;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Notify-only "update available" check. On Start it reads the running build version
    /// (<see cref="Application.version"/>, overridable from config) and fetches the latest
    /// published version from a configurable endpoint, then raises <see cref="onUpdateAvailable"/>
    /// when the remote version is newer. It never downloads or installs anything.
    ///
    /// The endpoint and release page URL live in a manifest at StreamingAssets/update.json
    /// (see <see cref="UpdateConfig"/>), so the host/repo can change without a rebuild - the same
    /// pattern as the model download manifest. Defaults expect a GitHub Releases "latest" response
    /// (<c>tag_name</c> + <c>html_url</c>); pointing <c>latestApiUrl</c> at any JSON that exposes a
    /// <c>tag_name</c> or <c>version</c> field works too (e.g. a raw version.json on GitLab).
    ///
    /// Wire <see cref="onUpdateAvailable"/> in the inspector to the notification surface (enable a
    /// banner, show a button, etc.); use <see cref="OpenReleasePage"/> on a button and
    /// <see cref="Dismiss"/> so a given version is not re-announced on later launches.
    /// </summary>
    [ConfigSettings("ui_cat_update", priority: 10)]
    public sealed class UpdateChecker : MonoBehaviour
    {
        // Persisted (global) so a dismissed version stays dismissed across launches/scenes.
        const string DismissedVersionKey = "update_dismissed_version";

        [SerializeField]
        [Tooltip("Config path relative to StreamingAssets: endpoint + release page URL.")]
        string configRelativePath = "update.json";

        [SerializeField]
        [Tooltip("Run the check automatically on Start. Disable to trigger CheckNow() yourself.")]
        bool checkOnStart = true;

        [SerializeField]
        [Tooltip(
            "Raised once when a newer version is available and it has not been dismissed. "
                + "Wire this to your notification UI (enable a banner, show a button, etc.)."
        )]
        UnityEvent onUpdateAvailable;

        public bool UpdateAvailable { get; private set; }
        public string CurrentVersion { get; private set; } = "";
        public string LatestVersion { get; private set; } = "";

        // Page to open for the user; the remote html_url when present, else config.releasePageUrl.
        public string ReleaseUrl { get; private set; } = "";

        // Code-level hook mirroring onUpdateAvailable, for non-inspector listeners.
        public event Action OnUpdateAvailable;

        // True once a check pass has finished (success or failure), so the status label can
        // distinguish "still checking" from "checked, nothing newer / unreachable".
        bool checkCompleted;

        // Live status label in the config panel (resynced). Mirrors MediaCompositor's
        // ModelDownloadStatus: dynamic English text shown next to a localized row label.
        [ConfigSetting("ui_update_status", priority: 30, resync: true)]
        public string UpdateStatus
        {
            get
            {
                if (UpdateAvailable && !string.IsNullOrEmpty(LatestVersion))
                    return $"Update available: {CurrentVersion} -> {LatestVersion}";
                if (!checkCompleted)
                    return "Checking for updates...";
                if (string.IsNullOrEmpty(LatestVersion))
                    return "Update check unavailable.";
                return $"Up to date ({CurrentVersion})";
            }
        }

        void Start()
        {
            if (checkOnStart)
                CheckNow();
        }

        // Starts a check pass. Safe to call manually (e.g. from a "Check for updates" button).
        [ConfigSetting("ui_check_update", priority: 29)]
        public void CheckNow()
        {
            StartCoroutine(CheckRoutine());
        }

        // Opens the release page in the default browser. No-op if no URL was resolved.
        [ConfigSetting("ui_open_release", priority: 28)]
        public void OpenReleasePage()
        {
            if (!string.IsNullOrWhiteSpace(ReleaseUrl))
                Application.OpenURL(ReleaseUrl);
        }

        // Suppresses re-notification for the currently announced version on future launches.
        public void Dismiss()
        {
            if (string.IsNullOrWhiteSpace(LatestVersion))
                return;
            GameData.SetString(DismissedVersionKey, LatestVersion, true);
            GameData.Save();
            UpdateAvailable = false;
        }

        IEnumerator CheckRoutine()
        {
            // checkCompleted flips in the finally so the status label stops showing "Checking..."
            // regardless of which exit path (config/network/parse failure or success) is taken.
            try
            {
                UpdateConfig config = null;
                yield return LoadConfig(result => config = result);

                if (config == null || !config.enabled)
                    yield break;

                ReleaseUrl = config.releasePageUrl?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(config.latestApiUrl))
                {
                    LogHelper.LogError("[UpdateChecker] No latestApiUrl configured.");
                    yield break;
                }

                CurrentVersion = !string.IsNullOrWhiteSpace(config.currentVersionOverride)
                    ? config.currentVersionOverride.Trim()
                    : Application.version;

                RemoteRelease release = null;
                yield return FetchLatest(config.latestApiUrl, result => release = result);

                if (release == null)
                    yield break;

                string latest = !string.IsNullOrWhiteSpace(release.tag_name)
                    ? release.tag_name
                    : release.version;
                if (string.IsNullOrWhiteSpace(latest))
                {
                    LogHelper.LogError("[UpdateChecker] Remote response had no tag_name/version.");
                    yield break;
                }

                LatestVersion = latest.Trim();
                if (!string.IsNullOrWhiteSpace(release.html_url))
                    ReleaseUrl = release.html_url.Trim();

                if (!IsNewer(LatestVersion, CurrentVersion))
                    yield break;

                // Honor a prior Dismiss for this exact version.
                if (GameData.GetString(DismissedVersionKey, "", true) == LatestVersion)
                    yield break;

                UpdateAvailable = true;
                LogHelper.Log(
                    $"[UpdateChecker] Update available: {CurrentVersion} -> {LatestVersion} ({ReleaseUrl})"
                );
                onUpdateAvailable?.Invoke();
                OnUpdateAvailable?.Invoke();
            }
            finally
            {
                checkCompleted = true;
            }
        }

        IEnumerator LoadConfig(Action<UpdateConfig> onLoaded)
        {
            string url = Application.streamingAssetsPath + "/" + configRelativePath;
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogHelper.LogError($"[UpdateChecker] Failed to load config '{url}': {req.error}");
                onLoaded(null);
                yield break;
            }

            UpdateConfig config = null;
            try
            {
                config = JsonUtility.FromJson<UpdateConfig>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[UpdateChecker] Config parse failed: {ex.Message}");
            }
            onLoaded(config);
        }

        IEnumerator FetchLatest(string apiUrl, Action<RemoteRelease> onLoaded)
        {
            using UnityWebRequest req = UnityWebRequest.Get(apiUrl);
            // GitHub rejects requests without a User-Agent; harmless for other hosts.
            req.SetRequestHeader("User-Agent", Application.productName);
            req.SetRequestHeader("Accept", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Offline / rate-limited / not found: stay silent, just log.
                LogHelper.LogWarning($"[UpdateChecker] Version check skipped: {req.error}");
                onLoaded(null);
                yield break;
            }

            RemoteRelease release = null;
            try
            {
                release = JsonUtility.FromJson<RemoteRelease>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[UpdateChecker] Version parse failed: {ex.Message}");
            }
            onLoaded(release);
        }

        // True when latest is strictly newer than current. Tolerant of a leading 'v' and any
        // pre-release/build suffix (only the leading dotted-numeric part is compared). On a parse
        // failure it returns false, so a malformed version never produces a false notification.
        static bool IsNewer(string latest, string current)
        {
            int[] a = ParseVersion(latest);
            int[] b = ParseVersion(current);
            if (a == null || b == null)
                return false;

            int len = Mathf.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int av = i < a.Length ? a[i] : 0;
                int bv = i < b.Length ? b[i] : 0;
                if (av != bv)
                    return av > bv;
            }
            return false;
        }

        // Extracts the leading dotted-numeric components, e.g. "v1.10.0-beta" -> {1,10,0}.
        static int[] ParseVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            int i = 0;
            while (i < raw.Length && !char.IsDigit(raw[i]))
                i++;

            var sb = new StringBuilder();
            for (; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsDigit(c) || c == '.')
                    sb.Append(c);
                else
                    break;
            }

            string[] parts = sb.ToString()
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            var nums = new int[parts.Length];
            for (int p = 0; p < parts.Length; p++)
                if (!int.TryParse(parts[p], out nums[p]))
                    return null;
            return nums;
        }
    }

    /// <summary>
    /// Update manifest shipped at StreamingAssets/update.json. Parsed with JsonUtility, so all
    /// fields are plain public fields.
    /// </summary>
    [Serializable]
    public class UpdateConfig
    {
        public bool enabled = true;

        // When non-empty, used instead of Application.version (handy for testing the flow).
        public string currentVersionOverride;

        // Endpoint returning JSON with a tag_name or version field (GitHub Releases "latest" by
        // default). The host/repo can be changed here without rebuilding the player.
        public string latestApiUrl;

        // Fallback page opened by OpenReleasePage when the response has no html_url.
        public string releasePageUrl;
    }

    /// <summary>
    /// Subset of a release payload parsed with JsonUtility; unknown fields are ignored. Field names
    /// match GitHub's Releases API (<c>tag_name</c>, <c>html_url</c>); <c>version</c> supports a
    /// plain custom version.json.
    /// </summary>
    [Serializable]
    public class RemoteRelease
    {
        public string tag_name;
        public string version;
        public string html_url;
    }
}
