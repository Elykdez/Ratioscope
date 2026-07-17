using System;

namespace Hypocycloid.Core
{
    // Applied to the Singleton Class
    [AttributeUsage(AttributeTargets.Class)]
    public class ConfigSettingsAttribute : Attribute
    {
        public string I18nKey { get; }

        // Higher priority categories appear first in UIConfigPanel. Ties keep discovery order.
        public int Priority { get; }

        public ConfigSettingsAttribute(string i18nKey, int priority = 0)
        {
            I18nKey = i18nKey;
            Priority = priority;
        }
    }

    /// <summary>
    /// Optional hook a ConfigSettings target can implement to customize how an enum-typed
    /// setting's dropdown options render in UIConfigPanel: per-value display label and whether
    /// the value can be selected. Used to show models that are not downloaded yet as locked +
    /// annotated entries. Return false to fall back to the default (nicified) label, selectable.
    /// </summary>
    public interface IConfigEnumOptionProvider
    {
        bool TryGetEnumOption(string i18nKey, object enumValue, out string label, out bool enabled);
    }

    // Applied within the class
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ConfigSettingAttribute : Attribute
    {
        public string I18nKey { get; }

        // Higher priority settings appear first within their category. Ties keep declaration order.
        public int Priority { get; }

        // Re-syncs its visual state from the underlying property when something external changes it.
        public bool Resync { get; }

        public string TipKey { get; }

        public ConfigSettingAttribute(
            string i18nKey,
            string tipKey = null,
            int priority = 0,
            bool resync = false
        )
        {
            I18nKey = i18nKey;
            TipKey = tipKey;
            Priority = priority;
            Resync = resync;
        }
    }
}
