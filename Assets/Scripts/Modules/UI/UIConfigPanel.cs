using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Hypocycloid.Core;
using Hypocycloid.UI;
using Hypocycloid.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Reflection-driven settings surface for scene processors.
    /// Mark a MonoBehaviour with ConfigSettingsAttribute, then mark fields, properties,
    /// or no-argument methods with ConfigSettingAttribute.
    /// </summary>
    public class UIConfigPanel : MonoBehaviour
    {
        static BindingFlags MemberFlags { get; } =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static CultureInfo Invariant { get; } = CultureInfo.InvariantCulture;
        const string LocalizationTable = "StringTable";
        const string PerformanceWarningKey = "ui_tip_lowperf";
        const string UserConfigCategoryKey = "ui_cat_user_config";
        const string ResetUserConfigKey = "ui_reset_user_config";
        const string SubtitleName = "Subtitle";
        const int MinimumGraphicsMemoryMb = 8192;

        // Fallback sizing when a multi-line setting reuses the single-line input prefab.
        const float TextAreaLineHeight = 18f;
        const float TextAreaVerticalPadding = 20f;

        // Named anchors the panel looks up inside each instantiated row template.
        const string LabelAnchor = "{{Label}}";
        const string ValueAnchor = "{{Value}}";

        [field: Header("UI Containers")]
        [field: SerializeField]
        public GameObject PanelRoot { get; private set; }

        [field: SerializeField]
        public Transform ContainerTrans { get; private set; }

        [field: SerializeField]
        public Button CloseButton { get; private set; }

        [field: SerializeField]
        public TMP_Text SubtitleLabel { get; private set; }

        [field: Header("UI Prefabs")]
        [field: SerializeField]
        public GameObject CategoryHeaderPrefab { get; private set; }

        [field: SerializeField]
        public GameObject InputFieldPrefab { get; private set; }

        [field: SerializeField]
        [field: Tooltip(
            "Multi-line variant used for string settings marked [TextArea]/[Multiline]."
        )]
        public GameObject TextAreaPrefab { get; private set; }

        [field: SerializeField]
        public GameObject TogglePrefab { get; private set; }

        [field: SerializeField]
        public GameObject DropdownPrefab { get; private set; }

        [field: SerializeField]
        public GameObject SliderPrefab { get; private set; }

        [field: SerializeField]
        public GameObject ButtonPrefab { get; private set; }

        [field: SerializeField]
        public GameObject UnsupportedPrefab { get; private set; }

        [field: Header("Auto Layout")]
        [field: SerializeField]
        public bool HideOnStart { get; private set; } = true;

        [field: SerializeField]
        public bool RebuildOnOpen { get; private set; } = true;

        List<ControlBinding> ResyncBindings { get; } = new();
        bool Built { get; set; }
        bool Started { get; set; }
        bool VisibilityRequestedBeforeStart { get; set; }
        float NextResyncTime { get; set; }

        public event Action<bool> VisibilityChanged;
        public bool Visible => IsVisible();

        void Awake()
        {
            ConfigSettingsPersistence.LoadForActiveScene();
            EnsurePanel();
            ApplyPerformanceWarning();

            if (CloseButton != null)
                CloseButton.onClick.AddListener(Close);
        }

        void Start()
        {
            EnsurePanel();
            Rebuild();

            // If this root starts inactive, the first explicit open runs before Start.
            // Do not let the startup hide path immediately close that user request.
            bool shouldHideOnStart = HideOnStart && !VisibilityRequestedBeforeStart;
            Started = true;

            if (shouldHideOnStart)
                SetVisibleCore(false);
        }

        void Update()
        {
            if (!IsVisible() || ResyncBindings.Count == 0 || Time.unscaledTime < NextResyncTime)
                return;

            NextResyncTime = Time.unscaledTime + 0.25f;
            RefreshResyncBindings();
        }

        public void ToggleVisible()
        {
            SetVisible(!IsVisible());
        }

        public void Close()
        {
            bool wasVisible = IsVisible();

            if (PanelRoot != null)
                PanelRoot.SetActive(false);

            if (gameObject.activeSelf)
                gameObject.SetActive(false);

            if (wasVisible)
                VisibilityChanged?.Invoke(false);
        }

        public void SetVisible(bool visible)
        {
            if (!Started && visible)
                VisibilityRequestedBeforeStart = true;

            SetVisibleCore(visible);
        }

        void SetVisibleCore(bool visible)
        {
            if (!visible)
            {
                Close();
                return;
            }

            bool wasVisible = IsVisible();

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            EnsurePanel();

            if (RebuildOnOpen || !Built)
                Rebuild();

            if (PanelRoot != null && !PanelRoot.activeSelf)
                PanelRoot.SetActive(true);

            bool isVisible = IsVisible();
            if (wasVisible != isVisible)
                VisibilityChanged?.Invoke(isVisible);
        }

        public void Rebuild()
        {
            ConfigSettingsPersistence.LoadForActiveScene();
            EnsurePanel();
            if (ContainerTrans == null)
            {
                LogHelper.LogError(
                    "[ConfigPanel] ContainerTrans is not assigned; cannot build config panel."
                );
                return;
            }

            ClearContainer();
            ResyncBindings.Clear();

            foreach (var target in GetConfigTargets().OrderByDescending(t => t.Attribute.Priority))
            {
                var bindings = GetConfigBindings(target.Type, target.Instance);
                if (bindings.Count == 0)
                    continue;

                CreateCategoryHeader(target.Attribute.I18nKey);
                foreach (var binding in bindings)
                    CreateUIElement(binding);
            }

            CreateUserConfigSection();
            Built = true;
            RefreshResyncBindings();
        }

        bool IsVisible() => PanelRoot != null && PanelRoot.activeInHierarchy;

        void EnsurePanel()
        {
            if (PanelRoot == null && ContainerTrans != null)
                PanelRoot = ContainerTrans.gameObject;
        }

        void ApplyPerformanceWarning()
        {
            if (!ShouldWarnAboutGraphicsMemory())
                return;

            TMP_Text subtitle = GetSubtitleLabel();
            if (subtitle == null)
                return;

            SetLocalizedText(
                subtitle,
                PerformanceWarningKey,
                arguments: new object[] { MinimumGraphicsMemoryMb }
            );
        }

        TMP_Text GetSubtitleLabel()
        {
            if (SubtitleLabel != null)
                return SubtitleLabel;

            RectTransform subtitleRect = UIHelper.FindChildRect(transform, SubtitleName);
            return subtitleRect != null ? subtitleRect.GetComponent<TMP_Text>() : null;
        }

        static bool ShouldWarnAboutGraphicsMemory()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || SystemInfo.graphicsMemorySize <= 0
                || SystemInfo.graphicsMemorySize < MinimumGraphicsMemoryMb;
        }

        void ClearContainer()
        {
            for (int i = ContainerTrans.childCount - 1; i >= 0; i--)
                Destroy(ContainerTrans.GetChild(i).gameObject);
        }

        void CreateCategoryHeader(string i18nKey)
        {
            if (CategoryHeaderPrefab == null)
            {
                LogHelper.LogError("[ConfigPanel] CategoryHeaderPrefab is not assigned.");
                return;
            }

            var go = Instantiate(CategoryHeaderPrefab, ContainerTrans);
            BindLabel(go, i18nKey, null, "- ");
        }

        void CreateUIElement(ConfigBinding binding)
        {
            if (binding.IsMethod)
            {
                CreateButtonElement(binding);
                return;
            }

            // Getter-only value -> non-interactive status label (e.g. model download status).
            if (!binding.CanWrite)
            {
                CreateLabelElement(binding);
                return;
            }

            Type type = binding.DataType;
            if (type == typeof(bool))
                CreateToggleElement(binding);
            else if (type.IsEnum)
                CreateDropdownElement(binding);
            else if ((type == typeof(int) || type == typeof(float)) && binding.HasRange)
                CreateSliderElement(binding);
            else if (type == typeof(string) && binding.IsMultiline)
                CreateTextAreaElement(binding);
            else if (type == typeof(int) || type == typeof(float) || type == typeof(string))
                CreateInputElement(binding);
            else
                CreateUnsupportedElement(binding);
        }

        void CreateInputElement(ConfigBinding binding)
        {
            if (InputFieldPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] InputFieldPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(InputFieldPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var input = FindControl<TMP_InputField>(go);
            if (input == null)
                return;

            input.contentType =
                binding.DataType == typeof(int) ? TMP_InputField.ContentType.IntegerNumber
                : binding.DataType == typeof(float) ? TMP_InputField.ContentType.DecimalNumber
                : TMP_InputField.ContentType.Standard;

            input.SetTextWithoutNotify(FormatValue(binding.GetValue()));
            input.onEndEdit.AddListener(value =>
            {
                if (!binding.SetValue(value, out string error))
                    LogHelper.LogWarning(error);
                else
                    ConfigSettingsPersistence.SaveActiveSceneSettings();

                input.SetTextWithoutNotify(FormatValue(binding.GetValue()));
            });

            TrackResync(input, binding);
        }

        // Multi-line string setting (member marked [TextArea]/[Multiline]). Uses TextAreaPrefab
        // when assigned; otherwise reuses the single-line input prefab and grows it to fit.
        void CreateTextAreaElement(ConfigBinding binding)
        {
            bool usingFallback = TextAreaPrefab == null;
            GameObject prefab = usingFallback ? InputFieldPrefab : TextAreaPrefab;
            if (prefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] No text area or input prefab assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(prefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var input = FindControl<TMP_InputField>(go);
            if (input == null)
                return;

            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            AlignTextAreaTop(input);
            if (usingFallback)
                GrowRowToFit(go, binding.TextAreaLines);

            input.SetTextWithoutNotify(FormatValue(binding.GetValue()));
            input.onEndEdit.AddListener(value =>
            {
                if (!binding.SetValue(value, out string error))
                    LogHelper.LogWarning(error);
                else
                    ConfigSettingsPersistence.SaveActiveSceneSettings();

                input.SetTextWithoutNotify(FormatValue(binding.GetValue()));
            });

            TrackResync(input, binding);
        }

        // Aligns the input's text and placeholder to the top-left so multi-line content fills
        // downward instead of centering.
        static void AlignTextAreaTop(TMP_InputField input)
        {
            if (input.textComponent != null)
                input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            if (input.placeholder is TMP_Text placeholder)
                placeholder.alignment = TextAlignmentOptions.TopLeft;
        }

        // Grows the row's LayoutElement to fit the requested line count when reusing the
        // single-line input prefab as a fallback.
        static void GrowRowToFit(GameObject row, int lines)
        {
            float height = Mathf.Max(1, lines) * TextAreaLineHeight + TextAreaVerticalPadding;
            var layout = row.GetComponent<LayoutElement>();
            if (layout == null)
                layout = row.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        // Ranged numeric bindings (int/float with a [Range]) render as a slider whose
        // min/max come from the attribute.
        void CreateSliderElement(ConfigBinding binding)
        {
            if (SliderPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] SliderPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(SliderPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var slider = FindControl<Slider>(go);
            if (slider == null)
                return;

            bool isInt = binding.DataType == typeof(int);
            slider.wholeNumbers = isInt;
            slider.minValue = binding.RangeMin;
            slider.maxValue = binding.RangeMax;
            slider.SetValueWithoutNotify(ToFloat(binding.GetValue()));

            slider.onValueChanged.AddListener(value =>
            {
                object converted = isInt ? Mathf.RoundToInt(value) : value;
                if (!binding.SetValue(converted, out string error))
                    LogHelper.LogWarning(error);
                else
                    ConfigSettingsPersistence.SaveActiveSceneSettings();
            });

            TrackResync(slider, binding);
        }

        // Read-only status display for getter-only properties. Reuses the input prefab so it
        // matches panel styling, but locks it (no editing) and lets the existing Resync path
        // refresh its text in place - used for the live model download status.
        void CreateLabelElement(ConfigBinding binding)
        {
            if (InputFieldPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] InputFieldPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(InputFieldPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var input = FindControl<TMP_InputField>(go);
            if (input == null)
                return;

            input.readOnly = true;
            input.interactable = false;
            input.richText = false;
            input.SetTextWithoutNotify(FormatValue(binding.GetValue()));

            // A locked input is never focused, so the existing resync branch refreshes its text.
            TrackResync(input, binding);
        }

        void CreateToggleElement(ConfigBinding binding)
        {
            if (TogglePrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] TogglePrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(TogglePrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var toggle = FindControl<Toggle>(go);
            if (toggle == null)
                return;

            toggle.SetIsOnWithoutNotify(binding.GetBoolValue());
            toggle.onValueChanged.AddListener(value =>
            {
                if (!binding.SetValue(value, out string error))
                    LogHelper.LogWarning(error);
                else
                    ConfigSettingsPersistence.SaveActiveSceneSettings();
            });

            TrackResync(toggle, binding);
        }

        void CreateDropdownElement(ConfigBinding binding)
        {
            if (DropdownPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] DropdownPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(DropdownPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var dropdown = FindControl<TMP_Dropdown>(go);
            if (dropdown == null)
                return;

            Array values = Enum.GetValues(binding.DataType);

            // A target can annotate/lock individual enum options (e.g. models not downloaded yet).
            var optionProvider = binding.Instance as IConfigEnumOptionProvider;
            var labels = new List<string>(values.Length);
            var enabledFlags = new bool[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                object value = values.GetValue(i);
                string label = StringHelper.NicifyVariableName(value.ToString());
                bool enabled = true;
                if (
                    optionProvider != null
                    && optionProvider.TryGetEnumOption(
                        binding.I18nKey,
                        value,
                        out string customLabel,
                        out bool customEnabled
                    )
                )
                {
                    if (!string.IsNullOrEmpty(customLabel))
                        label = customLabel;
                    enabled = customEnabled;
                }
                labels.Add(label);
                enabledFlags[i] = enabled;
            }

            LocalizedDropdownOptions.SetDropdownOptions(dropdown, labels);
            SetDropdownValue(dropdown, binding, values);
            dropdown.onValueChanged.AddListener(index =>
            {
                int clamped = Mathf.Clamp(index, 0, values.Length - 1);
                // Reject a locked option (revert to the current value) instead of selecting it.
                if (clamped < enabledFlags.Length && !enabledFlags[clamped])
                {
                    SetDropdownValue(dropdown, binding, values);
                    return;
                }
                if (!binding.SetValue(values.GetValue(clamped), out string error))
                    LogHelper.LogWarning(error);
                else
                    ConfigSettingsPersistence.SaveActiveSceneSettings();
            });

            TrackResync(dropdown, binding, values);
        }

        void CreateUserConfigSection()
        {
            CreateCategoryHeader(UserConfigCategoryKey);
            CreateResetUserConfigButton();
        }

        void CreateResetUserConfigButton()
        {
            if (ButtonPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] ButtonPrefab is not assigned; skipping '{ResetUserConfigKey}'."
                );
                return;
            }

            var go = Instantiate(ButtonPrefab, ContainerTrans);
            BindLabel(go, ResetUserConfigKey, null);
            var button = FindControl<Button>(go);
            if (button == null)
                return;

            button.onClick.AddListener(() =>
            {
                ConfigSettingsPersistence.ResetActiveSceneToDefaults();
                Rebuild();
            });
        }

        void CreateButtonElement(ConfigBinding binding)
        {
            if (ButtonPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] ButtonPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            var go = Instantiate(ButtonPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey);
            var button = FindControl<Button>(go);
            if (button == null)
                return;

            button.onClick.AddListener(() =>
            {
                if (!binding.Invoke(out string error))
                    LogHelper.LogWarning(error);
            });
        }

        void CreateUnsupportedElement(ConfigBinding binding)
        {
            if (UnsupportedPrefab == null)
            {
                LogHelper.LogError(
                    $"[ConfigPanel] UnsupportedPrefab is not assigned; skipping '{binding.I18nKey}'."
                );
                return;
            }

            string suffix = $": [Unsupported] {FormatValue(binding.GetValue())}";
            var go = Instantiate(UnsupportedPrefab, ContainerTrans);
            BindLabel(go, binding.I18nKey, binding.TooltipI18nKey, suffix: suffix);
        }

        // Locates the row control by the {{Value}} anchor authored in the template prefab.
        static T FindControl<T>(GameObject row)
            where T : Component
        {
            var rect = UIHelper.FindChildRect(row.transform, ValueAnchor);
            return rect != null ? rect.GetComponent<T>() : null;
        }

        // Binds the row's {{Label}} anchor: localized caption plus its tooltip key.
        static void BindLabel(
            GameObject row,
            string i18nKey,
            string tipKey,
            string prefix = "",
            string suffix = ""
        )
        {
            var labelRect = UIHelper.FindChildRect(row.transform, LabelAnchor);
            if (labelRect == null)
                return;

            var text = labelRect.GetComponent<TMP_Text>();
            if (text != null)
                SetLocalizedText(text, i18nKey, prefix, suffix);

            if (string.IsNullOrWhiteSpace(tipKey))
                return;

            var tooltip = labelRect.GetComponent<TipsTrigger>();
            if (tooltip != null)
                tooltip.SetLocalizationKey(LocalizationTable, tipKey);
        }

        static void SetLocalizedText(
            TMP_Text label,
            string i18nKey,
            string prefix = "",
            string suffix = "",
            object[] arguments = null
        )
        {
            var localizers = label
                .GetComponents<LocalizeStringEvent>()
                .Where(localizer => localizer is not TipsTrigger)
                .ToArray();

            foreach (var localizer in localizers)
                localizer.enabled = false;

            label.text = prefix + i18nKey + suffix;
            if (string.IsNullOrWhiteSpace(i18nKey))
                return;

            var activeLocalizer = localizers.FirstOrDefault();
            if (activeLocalizer == null)
                activeLocalizer = label.gameObject.AddComponent<LocalizeStringEvent>();

            activeLocalizer.enabled = false;
            activeLocalizer.OnUpdateString = new UnityEventString();
            activeLocalizer.StringReference.Arguments = arguments;
            activeLocalizer.OnUpdateString.AddListener(value =>
            {
                var operation = activeLocalizer.StringReference.CurrentLoadingOperationHandle;
                bool found =
                    operation.IsValid() && operation.IsDone && operation.Result.Entry != null;
                label.text = prefix + (found ? value : i18nKey) + suffix;
            });
            activeLocalizer.StringReference.SetReference(LocalizationTable, i18nKey);
            activeLocalizer.enabled = true;
            activeLocalizer.RefreshString();
        }

        void TrackResync(Selectable control, ConfigBinding binding, Array enumValues = null)
        {
            if (binding.Resync)
                ResyncBindings.Add(new ControlBinding(control, binding, enumValues));
        }

        void RefreshResyncBindings()
        {
            foreach (var entry in ResyncBindings)
            {
                if (entry.Control == null)
                    continue;

                if (entry.Control is Toggle toggle)
                    toggle.SetIsOnWithoutNotify(entry.Binding.GetBoolValue());
                else if (entry.Control is TMP_InputField input && !input.isFocused)
                    input.SetTextWithoutNotify(FormatValue(entry.Binding.GetValue()));
                else if (entry.Control is Slider slider)
                    slider.SetValueWithoutNotify(ToFloat(entry.Binding.GetValue()));
                else if (entry.Control is TMP_Dropdown dropdown)
                    SetDropdownValue(dropdown, entry.Binding, entry.EnumValues);
            }
        }

        static void SetDropdownValue(TMP_Dropdown dropdown, ConfigBinding binding, Array values)
        {
            if (values == null || values.Length == 0)
                return;

            object current = binding.GetValue();
            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (Equals(values.GetValue(i), current))
                {
                    selected = i;
                    break;
                }
            }

            dropdown.SetValueWithoutNotify(selected);
            dropdown.RefreshShownValue();
        }

        static List<ConfigBinding> GetConfigBindings(Type type, object instance)
        {
            var bindings = new List<ConfigBinding>();

            foreach (var field in type.GetFields(MemberFlags))
            {
                var attr = field.GetCustomAttribute<ConfigSettingAttribute>();
                if (
                    attr == null
                    || field.IsInitOnly
                    || IsBackingFieldCoveredByProperty(field, type)
                )
                    continue;

                bindings.Add(new ConfigBinding(attr, instance, field));
            }

            foreach (var prop in type.GetProperties(MemberFlags))
            {
                var attr = prop.GetCustomAttribute<ConfigSettingAttribute>();
                if (attr == null || !prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;

                // A getter-only property renders as a read-only status label (see CreateLabelElement).
                bindings.Add(new ConfigBinding(attr, instance, prop));
            }

            foreach (var method in type.GetMethods(MemberFlags))
            {
                var attr = method.GetCustomAttribute<ConfigSettingAttribute>();
                if (attr == null || method.GetParameters().Length > 0)
                    continue;

                bindings.Add(new ConfigBinding(attr, instance, method));
            }

            return bindings
                .OrderByDescending(b => b.Priority)
                .ThenBy(b => b.MetadataToken)
                .ToList();
        }

        static IEnumerable<ConfigTarget> GetConfigTargets()
        {
            foreach (var type in EnumerateTypes())
            {
                var attr = type.GetCustomAttribute<ConfigSettingsAttribute>();
                if (
                    attr == null
                    || type.IsAbstract
                    || !typeof(UnityEngine.Object).IsAssignableFrom(type)
                )
                    continue;

                foreach (var instance in FindSceneObjects(type))
                    yield return new ConfigTarget(type, instance, attr);
            }
        }

        static IEnumerable<Type> EnumerateTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                    yield return type;
            }
        }

        static IEnumerable<object> FindSceneObjects(Type type)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                if (obj is Component component)
                {
                    if (component.gameObject.scene.IsValid())
                        yield return obj;
                }
                else if (obj is GameObject go && go.scene.IsValid())
                {
                    yield return obj;
                }
            }
        }

        static bool IsBackingFieldCoveredByProperty(FieldInfo field, Type type)
        {
            if (
                !field.Name.StartsWith("<", StringComparison.Ordinal)
                || !field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)
            )
                return false;

            string propName = field.Name[1..^">k__BackingField".Length];
            var prop = type.GetProperty(propName, MemberFlags);
            return prop?.GetCustomAttribute<ConfigSettingAttribute>() != null;
        }

        static float ToFloat(object value)
        {
            return value switch
            {
                int i => i,
                float f => f,
                double d => (float)d,
                _ => 0f,
            };
        }

        static string FormatValue(object value)
        {
            return value switch
            {
                null => string.Empty,
                float f => f.ToString("0.###", Invariant),
                double d => d.ToString("0.###", Invariant),
                IFormattable formattable => formattable.ToString(null, Invariant),
                _ => value.ToString(),
            };
        }

        readonly struct ConfigTarget
        {
            public readonly Type Type;
            public readonly object Instance;
            public readonly ConfigSettingsAttribute Attribute;

            public ConfigTarget(Type type, object instance, ConfigSettingsAttribute attribute)
            {
                Type = type;
                Instance = instance;
                Attribute = attribute;
            }
        }

        readonly struct ControlBinding
        {
            public readonly Selectable Control;
            public readonly ConfigBinding Binding;
            public readonly Array EnumValues;

            public ControlBinding(Selectable control, ConfigBinding binding, Array enumValues)
            {
                Control = control;
                Binding = binding;
                EnumValues = enumValues;
            }
        }

        sealed class ConfigBinding
        {
            readonly object instance;
            readonly FieldInfo field;
            readonly PropertyInfo property;
            readonly MethodInfo method;
            readonly RangeAttribute range;
            readonly MinAttribute min;

            public string I18nKey { get; }
            public string TooltipI18nKey { get; }
            public Type DataType { get; }
            public bool Resync { get; }
            public int Priority { get; }
            public int MetadataToken { get; }
            public bool IsMethod => method != null;

            // A [Range] on the member drives slider min/max instead of a free-form input.
            public bool HasRange => range != null;
            public float RangeMin => range != null ? range.min : 0f;
            public float RangeMax => range != null ? range.max : 0f;

            // A [TextArea]/[Multiline] on a string member renders a multi-line text area.
            public bool IsMultiline { get; }
            public int TextAreaLines { get; }

            // The owning ConfigSettings instance (used to query IConfigEnumOptionProvider).
            public object Instance => instance;

            // Whether the binding can be written. Getter-only properties render as read-only
            // status labels; methods are buttons.
            public bool CanWrite { get; }

            public ConfigBinding(ConfigSettingAttribute attr, object instance, FieldInfo field)
            {
                this.instance = instance;
                this.field = field;
                I18nKey = attr.I18nKey;
                TooltipI18nKey = attr.TipKey;
                DataType = field.FieldType;
                Resync = attr.Resync;
                Priority = attr.Priority;
                MetadataToken = field.MetadataToken;
                CanWrite = true;
                range = field.GetCustomAttribute<RangeAttribute>();
                min = field.GetCustomAttribute<MinAttribute>();
                var (multiline, lines) = ReadMultiline(field);
                IsMultiline = multiline;
                TextAreaLines = lines;
            }

            public ConfigBinding(
                ConfigSettingAttribute attr,
                object instance,
                PropertyInfo property
            )
            {
                this.instance = instance;
                this.property = property;
                I18nKey = attr.I18nKey;
                TooltipI18nKey = attr.TipKey;
                DataType = property.PropertyType;
                Resync = attr.Resync;
                Priority = attr.Priority;
                MetadataToken = property.MetadataToken;
                CanWrite = property.CanWrite;
                range = property.GetCustomAttribute<RangeAttribute>();
                min = property.GetCustomAttribute<MinAttribute>();
                var (multiline, lines) = ReadMultiline(property);
                IsMultiline = multiline;
                TextAreaLines = lines;
            }

            static (bool multiline, int lines) ReadMultiline(MemberInfo member)
            {
                var textArea = member.GetCustomAttribute<TextAreaAttribute>();
                if (textArea != null)
                {
                    int lines = textArea.maxLines > 0 ? textArea.maxLines : textArea.minLines;
                    return (true, Mathf.Max(1, lines));
                }

                var multiline = member.GetCustomAttribute<MultilineAttribute>();
                if (multiline != null)
                    return (true, Mathf.Max(1, multiline.lines));

                return (false, 0);
            }

            public ConfigBinding(ConfigSettingAttribute attr, object instance, MethodInfo method)
            {
                this.instance = instance;
                this.method = method;
                I18nKey = attr.I18nKey;
                TooltipI18nKey = attr.TipKey;
                DataType = typeof(void);
                Resync = attr.Resync;
                Priority = attr.Priority;
                MetadataToken = method.MetadataToken;
                CanWrite = false;
            }

            public object GetValue()
            {
                if (field != null)
                    return field.GetValue(instance);
                return property != null ? property.GetValue(instance) : null;
            }

            public bool GetBoolValue()
            {
                object value = GetValue();
                return value is bool boolValue && boolValue;
            }

            public bool SetValue(object rawValue, out string error)
            {
                error = null;
                if (IsMethod)
                {
                    error = $"Cannot set method binding '{I18nKey}'.";
                    return false;
                }

                if (!TryConvert(rawValue, DataType, out object converted, out error))
                {
                    error = $"[ConfigPanel] {I18nKey}: {error}";
                    return false;
                }

                converted = ClampValue(converted);

                try
                {
                    if (field != null)
                        field.SetValue(instance, converted);
                    else
                        property.SetValue(instance, converted);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"[ConfigPanel] Failed to set {I18nKey}: {ex.Message}";
                    return false;
                }
            }

            public bool Invoke(out string error)
            {
                error = null;
                if (!IsMethod)
                {
                    error = $"Cannot invoke value binding '{I18nKey}'.";
                    return false;
                }

                try
                {
                    method.Invoke(instance, null);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"[ConfigPanel] Failed to invoke {I18nKey}: {ex.Message}";
                    return false;
                }
            }

            object ClampValue(object value)
            {
                if (value == null)
                    return null;

                if (DataType == typeof(int))
                {
                    int intValue = (int)value;
                    if (range != null)
                        intValue = Mathf.Clamp(
                            intValue,
                            Mathf.CeilToInt(range.min),
                            Mathf.FloorToInt(range.max)
                        );
                    if (min != null)
                        intValue = Mathf.Max(intValue, Mathf.CeilToInt(min.min));
                    return intValue;
                }

                if (DataType == typeof(float))
                {
                    float floatValue = (float)value;
                    if (range != null)
                        floatValue = Mathf.Clamp(floatValue, range.min, range.max);
                    if (min != null)
                        floatValue = Mathf.Max(floatValue, min.min);
                    return floatValue;
                }

                return value;
            }

            static bool TryConvert(
                object rawValue,
                Type targetType,
                out object converted,
                out string error
            )
            {
                converted = null;
                error = null;

                if (targetType.IsInstanceOfType(rawValue))
                {
                    converted = rawValue;
                    return true;
                }

                string text = rawValue as string;
                if (targetType == typeof(string))
                {
                    converted = rawValue?.ToString() ?? string.Empty;
                    return true;
                }

                if (targetType == typeof(int))
                {
                    if (
                        int.TryParse(
                            text ?? rawValue?.ToString(),
                            NumberStyles.Integer,
                            Invariant,
                            out int intValue
                        )
                    )
                    {
                        converted = intValue;
                        return true;
                    }
                    error = "expected an integer";
                    return false;
                }

                if (targetType == typeof(float))
                {
                    if (
                        float.TryParse(
                            text ?? rawValue?.ToString(),
                            NumberStyles.Float,
                            Invariant,
                            out float floatValue
                        )
                    )
                    {
                        converted = floatValue;
                        return true;
                    }
                    error = "expected a decimal number";
                    return false;
                }

                if (targetType == typeof(bool))
                {
                    if (rawValue is bool boolValue)
                    {
                        converted = boolValue;
                        return true;
                    }

                    string normalized = (text ?? rawValue?.ToString())?.Trim();
                    if (bool.TryParse(normalized, out bool parsedBool))
                    {
                        converted = parsedBool;
                        return true;
                    }

                    if (
                        normalized == "1"
                        || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        converted = true;
                        return true;
                    }

                    if (
                        normalized == "0"
                        || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        converted = false;
                        return true;
                    }

                    error = "expected true or false";
                    return false;
                }

                if (targetType.IsEnum)
                {
                    try
                    {
                        if (rawValue is int intValue)
                        {
                            converted = Enum.ToObject(targetType, intValue);
                            return true;
                        }

                        converted = Enum.Parse(targetType, rawValue.ToString(), true);
                        return true;
                    }
                    catch
                    {
                        error = $"expected one of: {string.Join(", ", Enum.GetNames(targetType))}";
                        return false;
                    }
                }

                try
                {
                    converted = Convert.ChangeType(rawValue, targetType, Invariant);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }
    }
}
