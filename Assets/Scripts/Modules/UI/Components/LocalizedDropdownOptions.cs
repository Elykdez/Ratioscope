using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Hypocycloid.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Dropdown))]
    public sealed class LocalizedDropdownOptions : MonoBehaviour
    {
        [SerializeField]
        TMP_Dropdown dropdown;

        [SerializeField]
        List<LocalizedString> localizedOptions = new();

        readonly List<string> fallbackTexts = new();
        readonly List<string> localizedTexts = new();
        readonly List<bool> localizedTextAvailable = new();
        readonly List<LocalizedString.ChangeHandler> callbacks = new();
        bool subscribed;

        void Awake()
        {
            ResolveDropdown();
        }

        void OnEnable()
        {
            ResolveDropdown();
            Subscribe();
            RefreshStrings();
            ApplyOptions();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            ResolveDropdown();
        }
#endif

        // Runtime callers pass fallback labels here. The actual localized text comes
        // from the inspector-assigned LocalizedString list and replaces these by index.
        public void SetOptions(IReadOnlyList<string> fallbackOptions)
        {
            fallbackTexts.Clear();
            if (fallbackOptions != null)
                fallbackTexts.AddRange(fallbackOptions);

            EnsureLocalizedOptionSlots(fallbackTexts.Count);

            if (isActiveAndEnabled)
            {
                Subscribe();
                RefreshStrings();
            }

            ApplyOptions();
        }

        // Use this when a caller may receive either a localized dropdown or a plain
        // TMP dropdown. Localized dropdowns update fallback labels; plain ones are set directly.
        public static void SetDropdownOptions(
            TMP_Dropdown dropdown,
            IReadOnlyList<string> fallbackOptions
        )
        {
            if (dropdown == null)
                return;

            fallbackOptions ??= Array.Empty<string>();

            var localizer = dropdown.GetComponent<LocalizedDropdownOptions>();
            if (localizer != null)
            {
                localizer.SetOptions(fallbackOptions);
                return;
            }

            var options = new List<string>(fallbackOptions.Count);
            for (int i = 0; i < fallbackOptions.Count; i++)
                options.Add(fallbackOptions[i]);

            dropdown.ClearOptions();
            dropdown.AddOptions(options);
        }

        void ResolveDropdown()
        {
            if (dropdown == null)
                dropdown = GetComponent<TMP_Dropdown>();
        }

        void EnsureLocalizedOptionSlots(int count)
        {
            while (localizedOptions.Count < count)
                localizedOptions.Add(new LocalizedString());
        }

        void Subscribe()
        {
            Unsubscribe();

            int count = ResolveOptionCount();
            subscribed = true;
            for (int i = 0; i < count; i++)
            {
                localizedTexts.Add(null);
                localizedTextAvailable.Add(false);

                LocalizedString localizedOption =
                    i < localizedOptions.Count ? localizedOptions[i] : null;
                if (localizedOption == null || localizedOption.IsEmpty)
                {
                    callbacks.Add(null);
                    continue;
                }

                int index = i;
                LocalizedString.ChangeHandler callback = value => OnOptionLocalized(index, value);
                callbacks.Add(callback);
                localizedOption.StringChanged += callback;
            }
        }

        void Unsubscribe()
        {
            if (!subscribed)
                return;

            for (int i = 0; i < callbacks.Count && i < localizedOptions.Count; i++)
            {
                LocalizedString.ChangeHandler callback = callbacks[i];
                LocalizedString localizedOption = localizedOptions[i];
                if (callback != null && localizedOption != null)
                    localizedOption.StringChanged -= callback;
            }

            callbacks.Clear();
            localizedTexts.Clear();
            localizedTextAvailable.Clear();
            subscribed = false;
        }

        void RefreshStrings()
        {
            int count = ResolveOptionCount();
            for (int i = 0; i < count && i < localizedOptions.Count; i++)
            {
                LocalizedString localizedOption = localizedOptions[i];
                if (localizedOption != null && !localizedOption.IsEmpty)
                    localizedOption.RefreshString();
            }
        }

        void OnOptionLocalized(int index, string value)
        {
            if (index < 0 || index >= localizedOptions.Count || index >= localizedTexts.Count)
                return;

            LocalizedString localizedOption = localizedOptions[index];
            var operation = localizedOption?.CurrentLoadingOperationHandle;
            bool found =
                operation.HasValue
                && operation.Value.IsValid()
                && operation.Value.IsDone
                && operation.Value.Result.Entry != null;

            localizedTexts[index] = value;
            localizedTextAvailable[index] = found;
            ApplyOptions();
        }

        void ApplyOptions()
        {
            int count = ResolveOptionCount();
            if (dropdown == null || count == 0)
                return;

            int selected = Mathf.Clamp(dropdown.value, 0, count - 1);
            var options = new List<TMP_Dropdown.OptionData>(count);
            for (int i = 0; i < count; i++)
                options.Add(new TMP_Dropdown.OptionData(GetOptionText(i)));

            dropdown.options.Clear();
            dropdown.options.AddRange(options);
            dropdown.SetValueWithoutNotify(selected);
            dropdown.RefreshShownValue();
        }

        int ResolveOptionCount()
        {
            return fallbackTexts.Count > 0 ? fallbackTexts.Count : localizedOptions.Count;
        }

        string GetOptionText(int index)
        {
            if (
                index >= 0
                && index < localizedTexts.Count
                && index < localizedTextAvailable.Count
                && localizedTextAvailable[index]
            )
                return localizedTexts[index] ?? string.Empty;

            return index >= 0 && index < fallbackTexts.Count ? fallbackTexts[index] : string.Empty;
        }
    }
}
