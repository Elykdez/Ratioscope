using System;
using System.Collections;
using Hypocycloid.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    [DisallowMultipleComponent]
    public sealed class UIHelpPanel : MonoBehaviour
    {
        const string DefaultDocFolder = "doc";
        const string DefaultFilenamePrefix = "desc_";
        const string DefaultFallbackLocaleCode = "en";

        [field: Header("Document")]
        [field: SerializeField]
        public string DocFolder { get; private set; } = DefaultDocFolder;

        [field: SerializeField]
        public string FilenamePrefix { get; private set; } = DefaultFilenamePrefix;

        [field: SerializeField]
        public string FallbackLocaleCode { get; private set; } = DefaultFallbackLocaleCode;

        [field: Header("UI")]
        [field: SerializeField]
        public GameObject PanelRoot { get; private set; }

        [field: SerializeField]
        public UIMarkdownRenderer ContentRenderer { get; private set; }

        [field: SerializeField]
        public TMP_Text VersionLabel { get; private set; }

        [field: SerializeField]
        public Button CloseButton { get; private set; }

        [field: SerializeField]
        public bool HideOnStart { get; private set; } = true;

        Coroutine loadRoutine;
        bool visibilityRequested;

        public event Action<bool> VisibilityChanged;

        public bool Visible => PanelRoot != null && PanelRoot.activeInHierarchy;

        void Awake()
        {
            ApplyVersionLabel();
            BindCloseButton();

            // PanelRoot is this same GameObject and it starts inactive in the scene, so Awake
            // first runs when an open request activates it. Don't let HideOnStart cancel that
            // request (otherwise the first click re-hides and it takes two clicks to open).
            if (HideOnStart && !visibilityRequested)
                SetVisibleCore(false);
        }

        void OnEnable()
        {
            ApplyVersionLabel();
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            BindCloseButton();
        }

        void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            StopLoadRoutine();
        }

        void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (CloseButton != null)
                CloseButton.onClick.RemoveListener(Close);
        }

        public void ToggleVisible()
        {
            SetVisible(!Visible);
        }

        public void Close()
        {
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (visible)
                visibilityRequested = true;

            SetVisibleCore(visible);

            if (visible)
                Reload();
            else
                StopLoadRoutine();
        }

        public void Reload()
        {
            if (!isActiveAndEnabled)
                return;

            StopLoadRoutine();
            loadRoutine = StartCoroutine(LoadHelpContent());
        }

        void OnSelectedLocaleChanged(UnityEngine.Localization.Locale locale)
        {
            if (Visible)
                Reload();
        }

        void SetVisibleCore(bool visible)
        {
            if (PanelRoot == null || PanelRoot.activeSelf == visible)
                return;

            PanelRoot.SetActive(visible);
            VisibilityChanged?.Invoke(visible);
        }

        void StopLoadRoutine()
        {
            if (loadRoutine == null)
                return;

            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }

        void ApplyVersionLabel()
        {
            if (VersionLabel != null)
                VersionLabel.text = $"v{Application.version}";
        }

        IEnumerator LoadHelpContent()
        {
            if (ContentRenderer != null)
                ContentRenderer.Source = "Loading...";

            yield return LocalizationSettings.InitializationOperation;

            string localeCode = ResolveLocaleCode();
            string text = null;
            yield return FetchDoc(GetDocFilename(localeCode), result => text = result);

            string fallbackCode = NormalizeLocaleCode(FallbackLocaleCode);
            if (string.IsNullOrEmpty(text) && localeCode != fallbackCode)
                yield return FetchDoc(GetDocFilename(fallbackCode), result => text = result);

            if (ContentRenderer != null)
            {
                ContentRenderer.Source = string.IsNullOrEmpty(text)
                    ? $"Help document not found in StreamingAssets/{DocFolder}."
                    : text;
            }

            loadRoutine = null;
        }

        IEnumerator FetchDoc(string filename, Action<string> completed)
        {
            string url = BuildDocUrl(filename);
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            completed(
                req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null
            );
        }

        string BuildDocUrl(string filename)
        {
            string folder = (DocFolder ?? string.Empty).Trim('/', '\\');
            return string.IsNullOrEmpty(folder)
                ? $"{Application.streamingAssetsPath}/{filename}"
                : $"{Application.streamingAssetsPath}/{folder}/{filename}";
        }

        string GetDocFilename(string localeCode)
        {
            string code = string.IsNullOrWhiteSpace(localeCode)
                ? DefaultFallbackLocaleCode
                : localeCode;
            string prefix = string.IsNullOrWhiteSpace(FilenamePrefix)
                ? DefaultFilenamePrefix
                : FilenamePrefix;
            return $"{prefix}{code}.md";
        }

        static string ResolveLocaleCode()
        {
            string code =
                LocalizationSettings.SelectedLocale != null
                    ? LocalizationSettings.SelectedLocale.Identifier.Code
                    : DefaultFallbackLocaleCode;
            return NormalizeLocaleCode(code);
        }

        static string NormalizeLocaleCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return DefaultFallbackLocaleCode;

            int separatorIndex = code.IndexOf('-', StringComparison.Ordinal);
            if (separatorIndex > 0)
                code = code[..separatorIndex];

            return code.ToLowerInvariant();
        }

        void BindCloseButton()
        {
            if (CloseButton == null)
                return;

            CloseButton.onClick.RemoveListener(Close);
            CloseButton.onClick.AddListener(Close);
        }
    }
}
