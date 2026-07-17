using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// First feature built on <see cref="GameData"/>: persists the selected
    /// localization locale across sessions. The saved locale is chosen during
    /// initialization through an <see cref="IStartupLocaleSelector"/> - never by
    /// reassigning <see cref="LocalizationSettings.SelectedLocale"/> at runtime,
    /// which would release the localized-asset handles that GameObjectLocalizer
    /// components are still awaiting and throw "invalid operation handle". Every
    /// subsequent locale change is then recorded. Fully automatic - no scene wiring.
    /// </summary>
    public static class LocalePrefs
    {
        // Persisted globally: language is an app-wide preference, not per-scene.
        public const string KEY_I18N = "i18n";

        static bool initialized;

        /// <summary>Index of the persisted locale, or -1 when none has been saved.</summary>
        public static int SavedLocaleId => GameData.GetInt(KEY_I18N, -1, true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (initialized)
                return;
            initialized = true;

            // Insert our selector at the front so the saved locale is picked while
            // localization initializes, before any GameObjectLocalizer resolves assets.
            IList<IStartupLocaleSelector> selectors = LocalizationSettings.StartupLocaleSelectors;
            if (selectors != null && !HasSelector(selectors))
                selectors.Insert(0, new GameDataLocaleSelector());

            // Save-only: this never switches the locale, so it cannot race localizers.
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        static bool HasSelector(IList<IStartupLocaleSelector> selectors)
        {
            for (int i = 0; i < selectors.Count; i++)
            {
                if (selectors[i] is GameDataLocaleSelector)
                    return true;
            }
            return false;
        }

        static void OnSelectedLocaleChanged(Locale locale)
        {
            var locales = LocalizationSettings.AvailableLocales?.Locales;
            if (locales == null || locale == null)
                return;

            int index = locales.IndexOf(locale);
            if (index < 0)
                return;

            GameData.SetInt(KEY_I18N, index, true);
            GameData.Save();
        }
    }

    /// <summary>
    /// Resolves the startup locale from the value persisted by <see cref="LocalePrefs"/>.
    /// Returning null defers to the next selector (system/default), so an unset or
    /// out-of-range save is harmless.
    /// </summary>
    [Serializable]
    public class GameDataLocaleSelector : IStartupLocaleSelector
    {
        public Locale GetStartupLocale(ILocalesProvider availableLocales)
        {
            int saved = GameData.GetInt(LocalePrefs.KEY_I18N, -1, true);
            if (saved < 0)
                return null;

            var locales = availableLocales?.Locales;
            if (locales == null || saved >= locales.Count)
                return null;

            return locales[saved];
        }
    }
}
