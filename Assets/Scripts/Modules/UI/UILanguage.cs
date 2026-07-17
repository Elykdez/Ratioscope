using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(Toggle))]
    public class UILanguage : MonoBehaviour
    {
        [field: Header("Entry")]
        [field: SerializeField]
        Toggle EntryToggle { get; set; }

        [field: SerializeField]
        Image SelectedIcon { get; set; }

        [field: SerializeField]
        Image SelectedLanguageImage { get; set; }

        [field: Header("Options")]
        [field: SerializeField]
        GameObject OptionsPanel { get; set; }

        [field: SerializeField]
        RectTransform OptionsParent { get; set; }

        [field: SerializeField]
        Toggle OptionTemplate { get; set; }

        [field: SerializeField]
        ToggleGroup ToggleGroup { get; set; }

        [field: SerializeField]
        bool CloseOnSelect { get; set; } = true;

        [field: SerializeField]
        bool OpenOnStart { get; set; }

        [field: SerializeField]
        List<LanguageOption> LanguageOptions { get; set; } = new();

        List<Toggle> OptionToggles { get; } = new();
        List<Locale> OptionLocales { get; } = new();
        List<LanguageOption> ActiveOptions { get; } = new();

        Coroutine InitializeRoutine { get; set; }
        bool SuppressToggleEvent { get; set; }

        void Awake()
        {
            ResolveReferences();

            // Edit mode is preview-only: don't mutate serialized active state.
            if (!Application.isPlaying)
                return;

            SetOptionsVisible(OpenOnStart);
            SetEntryToggleWithoutNotify(OpenOnStart);

            if (OptionTemplate != null)
                OptionTemplate.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            ResolveReferences();

            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

            // Edit-mode preview: mirror the locale chosen via the Localization
            // scene control without instantiating option toggles, so nothing is
            // serialized into the scene/prefab.
            if (!Application.isPlaying)
            {
                SyncEntryToLocale(LocalizationSettings.SelectedLocale);
                return;
            }

            if (EntryToggle != null)
                EntryToggle.onValueChanged.AddListener(OnEntryToggleChanged);

            InitializeRoutine ??= StartCoroutine(Initialize());
        }

        void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;

            if (!Application.isPlaying)
                return;

            if (EntryToggle != null)
                EntryToggle.onValueChanged.RemoveListener(OnEntryToggleChanged);

            if (InitializeRoutine != null)
            {
                StopCoroutine(InitializeRoutine);
                InitializeRoutine = null;
            }
        }

        void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;

            if (EntryToggle != null)
                EntryToggle.onValueChanged.RemoveListener(OnEntryToggleChanged);
        }

        IEnumerator Initialize()
        {
            yield return LocalizationSettings.InitializationOperation;

            RebuildOptions();
            SyncToSelectedLocale(LocalizationSettings.SelectedLocale);
            InitializeRoutine = null;
        }

        void OnEntryToggleChanged(bool isOn)
        {
            SetOptionsVisible(isOn);
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            if (Application.isPlaying)
                SyncToSelectedLocale(locale);
            else
                SyncEntryToLocale(locale);
        }

        // Updates only the collapsed entry view from the serialized option data,
        // so edit-mode preview never instantiates or saves anything.
        void SyncEntryToLocale(Locale locale)
        {
            if (locale == null)
                return;

            LanguageOption option = FindOptionForLocale(locale);
            UpdateSelectedView(option, locale);
        }

        LanguageOption FindOptionForLocale(Locale locale)
        {
            if (locale == null)
                return null;

            foreach (LanguageOption option in LanguageOptions)
            {
                if (LocaleCodeMatches(locale, option.LocaleCode))
                    return option;
            }

            return null;
        }

        void SelectLanguage(int index)
        {
            if (SuppressToggleEvent || index < 0 || index >= OptionLocales.Count)
                return;

            Locale locale = OptionLocales[index];
            if (locale == null)
                return;

            LocalizationSettings.SelectedLocale = locale;
            SyncToSelectedLocale(locale);

            if (CloseOnSelect)
            {
                SetOptionsVisible(false);
                SetEntryToggleWithoutNotify(false);
            }
        }

        void RebuildOptions()
        {
            ClearBuiltOptions();

            if (OptionTemplate == null || OptionsParent == null)
                return;

            OptionTemplate.gameObject.SetActive(false);

            foreach (LanguageOption option in GetOptions())
            {
                Locale locale = FindLocale(option.LocaleCode);
                if (locale == null)
                    continue;

                int index = OptionLocales.Count;
                Toggle toggle = Instantiate(OptionTemplate, OptionsParent);
                toggle.name = string.IsNullOrWhiteSpace(option.LocaleCode)
                    ? $"Language_{index}"
                    : $"Language_{option.LocaleCode}";
                toggle.group = ToggleGroup;
                toggle.SetIsOnWithoutNotify(false);
                toggle.gameObject.SetActive(true);
                SetOptionView(toggle.transform, option, locale);
                toggle.onValueChanged.AddListener(isOn =>
                {
                    if (isOn)
                        SelectLanguage(index);
                });

                OptionToggles.Add(toggle);
                OptionLocales.Add(locale);
                ActiveOptions.Add(option);
            }
        }

        void ClearBuiltOptions()
        {
            foreach (Toggle toggle in OptionToggles)
            {
                if (toggle != null)
                    Destroy(toggle.gameObject);
            }

            OptionToggles.Clear();
            OptionLocales.Clear();
            ActiveOptions.Clear();
        }

        IEnumerable<LanguageOption> GetOptions()
        {
            if (LanguageOptions.Count > 0)
                return LanguageOptions;

            List<LanguageOption> options = new();
            foreach (Locale locale in LocalizationSettings.AvailableLocales.Locales)
            {
                options.Add(new LanguageOption(locale.Identifier.Code));
            }

            return options;
        }

        void SyncToSelectedLocale(Locale selectedLocale)
        {
            int selectedIndex = FindOptionIndex(selectedLocale);

            SuppressToggleEvent = true;
            for (int i = 0; i < OptionToggles.Count; i++)
            {
                Toggle toggle = OptionToggles[i];
                if (toggle != null)
                    toggle.SetIsOnWithoutNotify(i == selectedIndex);
            }
            SuppressToggleEvent = false;

            if (selectedIndex >= 0)
            {
                UpdateSelectedView(ActiveOptions[selectedIndex], selectedLocale);
            }
            else if (selectedLocale != null)
            {
                UpdateSelectedView(null, selectedLocale);
            }
        }

        void UpdateSelectedView(LanguageOption option, Locale locale)
        {
            if (SelectedIcon != null)
            {
                Sprite flag = ResolveFlagIcon(option, locale);
                SelectedIcon.sprite = flag;
                SelectedIcon.enabled = flag != null;
            }

            if (SelectedLanguageImage != null)
            {
                Sprite image = ResolveLanguageImage(option, locale);
                SelectedLanguageImage.sprite = image;
                SelectedLanguageImage.enabled = image != null;
            }
        }

        int FindOptionIndex(Locale locale)
        {
            if (locale == null)
                return -1;

            for (int i = 0; i < OptionLocales.Count; i++)
            {
                if (
                    OptionLocales[i] == locale
                    || LocaleCodeMatches(locale, ActiveOptions[i].LocaleCode)
                )
                    return i;
            }

            return -1;
        }

        Locale FindLocale(string localeCode)
        {
            foreach (Locale locale in LocalizationSettings.AvailableLocales.Locales)
            {
                if (LocaleCodeMatches(locale, localeCode))
                    return locale;
            }

            return null;
        }

        void SetOptionsVisible(bool visible)
        {
            if (OptionsPanel != null && OptionsPanel.activeSelf != visible)
                OptionsPanel.SetActive(visible);
        }

        void SetEntryToggleWithoutNotify(bool isOn)
        {
            if (EntryToggle != null)
                EntryToggle.SetIsOnWithoutNotify(isOn);
        }

        void SetOptionView(Transform optionRoot, LanguageOption option, Locale locale)
        {
            Image flag = FindChildComponent<Image>(optionRoot, "Flag");
            if (flag == null)
                flag = FindChildComponent<Image>(optionRoot, "Icon");

            if (flag != null)
            {
                Sprite flagSprite = ResolveFlagIcon(option, locale);
                flag.sprite = flagSprite;
                flag.enabled = flagSprite != null;
            }

            Image language = FindChildComponent<Image>(optionRoot, "LanguageImage");
            if (language != null)
            {
                Sprite image = ResolveLanguageImage(option, locale);
                language.sprite = image;
                language.enabled = image != null;
            }
        }

        Sprite ResolveFlagIcon(LanguageOption option, Locale locale)
        {
            if (option?.FlagIcon != null)
                return option.FlagIcon;

            LanguageOption registered = FindOptionForLocale(locale);
            return registered?.FlagIcon;
        }

        Sprite ResolveLanguageImage(LanguageOption option, Locale locale)
        {
            if (option?.LanguageImage != null)
                return option.LanguageImage;

            LanguageOption registered = FindOptionForLocale(locale);
            if (registered?.LanguageImage != null)
                return registered.LanguageImage;

            return ResolveFlagIcon(option, locale);
        }

        void ResolveReferences()
        {
            EntryToggle ??= GetComponent<Toggle>();

            if (OptionsPanel == null)
            {
                Transform found = transform.Find("Options");
                if (found == null)
                    found = transform.Find("Popup");

                if (found != null)
                    OptionsPanel = found.gameObject;
            }

            if (OptionsParent == null && OptionsPanel != null)
                OptionsParent = OptionsPanel.GetComponentInChildren<RectTransform>(true);

            if (OptionTemplate == null && OptionsPanel != null)
                OptionTemplate = OptionsPanel.GetComponentInChildren<Toggle>(true);

            if (ToggleGroup == null && OptionsPanel != null)
                ToggleGroup = OptionsPanel.GetComponentInChildren<ToggleGroup>(true);
        }

        static bool LocaleCodeMatches(Locale locale, string localeCode)
        {
            if (locale == null || string.IsNullOrWhiteSpace(localeCode))
                return false;

            string code = locale.Identifier.Code;
            return string.Equals(code, localeCode, StringComparison.OrdinalIgnoreCase)
                || code.StartsWith(localeCode + "-", StringComparison.OrdinalIgnoreCase);
        }

        static T FindChildComponent<T>(Transform root, string childName)
            where T : Component
        {
            if (root == null)
                return null;

            if (root.name == childName && root.TryGetComponent(out T exactMatch))
                return exactMatch;

            for (int i = 0; i < root.childCount; i++)
            {
                T found = FindChildComponent<T>(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
