using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hypocycloid.Core
{
    /// <summary>
    /// JSON persistence for writable [ConfigSetting] values on [Config] scene targets.
    /// </summary>
    public static class ConfigSettingsPersistence
    {
        const string FileName = "user_config.json";
        const string ConfigFolderName = "config";
        const int FileVersion = 1;

        static readonly BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static readonly HashSet<int> LoadedSceneHandles = new();
        static readonly Dictionary<string, string> DefaultValues = new();

        public static string DirectoryPath =>
            Path.Combine(RuntimeDataPaths.WritableRoot, ConfigFolderName);
        public static string FilePath => Path.Combine(DirectoryPath, FileName);
        public static bool FileExists => File.Exists(FilePath);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            LoadedSceneHandles.Clear();
            DefaultValues.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LoadInitialScene()
        {
            LoadForActiveScene();
        }

        public static void LoadForActiveScene()
        {
            LoadForScene(SceneManager.GetActiveScene());
        }

        public static void SaveActiveSceneSettings()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            var file = ReadFile($"save scene '{scene.name}'");
            var entries = ToEntryMap(file);
            var bindings = GetPersistableBindings(scene).ToList();
            int savedCount = 0;
            int removedDefaultCount = 0;
            int unchangedDefaultCount = 0;
            int missingDefaultCount = 0;
            int skippedCount = 0;

            foreach (var binding in bindings)
            {
                if (!TrySerializeValue(binding.GetValue(), binding.DataType, out string value))
                {
                    skippedCount++;
                    continue;
                }

                if (DefaultValues.TryGetValue(binding.Key, out string defaultValue))
                {
                    if (string.Equals(value, defaultValue, StringComparison.Ordinal))
                    {
                        if (entries.Remove(binding.Key))
                            removedDefaultCount++;
                        else
                            unchangedDefaultCount++;
                        continue;
                    }
                }
                else
                {
                    missingDefaultCount++;
                }

                entries[binding.Key] = new SerializedConfigEntry
                {
                    key = binding.Key,
                    i18nKey = binding.I18nKey,
                    valueType = binding.DataType.FullName,
                    value = value,
                };
                savedCount++;
                LogHelper.Log($"[Config] Save {binding.Key} = {value}");
            }

            LogHelper.Log(
                "[Config] Saving scene "
                    + $"'{scene.name}' to {FilePath}: {savedCount} persisted, "
                    + $"{removedDefaultCount} removed defaults, "
                    + $"{unchangedDefaultCount} unchanged defaults, "
                    + $"{missingDefaultCount} missing defaults, {skippedCount} skipped."
            );

            WriteFile(
                new SerializedConfigFile
                {
                    version = FileVersion,
                    entries = entries.Values.ToList(),
                },
                $"save scene '{scene.name}'"
            );
        }

        public static void ResetActiveSceneToDefaults()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            LoadForScene(scene);

            var bindings = GetPersistableBindings(scene).ToList();
            foreach (var binding in bindings)
            {
                if (!DefaultValues.TryGetValue(binding.Key, out string defaultValue))
                    continue;

                if (!binding.SetSerializedValue(defaultValue, out string error))
                    LogHelper.LogWarning($"[Config] Failed to reset {binding.Key}: {error}");
            }

            var file = ReadFile($"reset scene '{scene.name}'");
            var entries = ToEntryMap(file);
            foreach (var binding in bindings)
                entries.Remove(binding.Key);

            WriteFile(
                new SerializedConfigFile
                {
                    version = FileVersion,
                    entries = entries.Values.ToList(),
                },
                $"reset scene '{scene.name}'"
            );
        }

        public static SerializedConfigFile ReadSerializedFile()
        {
            return ReadFile("editor preview");
        }

        public static void WriteSerializedFile(SerializedConfigFile file)
        {
            var entries = new Dictionary<string, SerializedConfigEntry>(StringComparer.Ordinal);
            foreach (var entry in file?.entries ?? Enumerable.Empty<SerializedConfigEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.key))
                    continue;

                entries[entry.key] = new SerializedConfigEntry
                {
                    key = entry.key,
                    i18nKey = entry.i18nKey ?? string.Empty,
                    valueType = entry.valueType ?? string.Empty,
                    value = entry.value ?? string.Empty,
                };
            }

            WriteFile(
                new SerializedConfigFile
                {
                    version = FileVersion,
                    entries = entries.Values.ToList(),
                },
                "editor write"
            );
        }

        public static string ReadSerializedJson()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                LogHelper.Log($"[Config] Read raw JSON skipped; file does not exist: {path}");
                return string.Empty;
            }

            try
            {
                string json = File.ReadAllText(path);
                LogHelper.Log($"[Config] Read raw JSON from {path} ({json.Length} chars).");
                return json;
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"[Config] Failed to read {path}: {ex.Message}");
                return string.Empty;
            }
        }

        public static void DeleteActiveSceneSettings()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !FileExists)
                return;

            var bindings = GetPersistableBindings(scene).ToList();
            var entries = ToEntryMap(ReadFile($"delete scene '{scene.name}'"));
            foreach (var binding in bindings)
                entries.Remove(binding.Key);

            WriteFile(
                new SerializedConfigFile
                {
                    version = FileVersion,
                    entries = entries.Values.ToList(),
                },
                $"delete scene '{scene.name}'"
            );
        }

        public static void DeleteSerializedFile()
        {
            string path = FilePath;
            if (!File.Exists(path))
                return;

            try
            {
                DeleteFileAndMeta(path);
                LogHelper.Log($"[Config] Deleted serialized config file: {path}");
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"[Config] Failed to delete {path}: {ex.Message}");
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LoadForScene(scene);
        }

        static void LoadForScene(Scene scene)
        {
            if (!scene.IsValid() || LoadedSceneHandles.Contains(scene.handle))
                return;

            var bindings = GetPersistableBindings(scene).ToList();
            if (bindings.Count == 0)
            {
                LogHelper.Log(
                    $"[Config] Load for scene '{scene.name}' found no persistable bindings; will retry later."
                );
                return;
            }

            int capturedDefaults = CaptureDefaults(bindings);
            LoadedSceneHandles.Add(scene.handle);

            var entries = ToEntryMap(ReadFile($"load scene '{scene.name}'"));
            int appliedCount = 0;
            int missingCount = 0;
            int failedCount = 0;
            foreach (var binding in bindings)
            {
                if (!entries.TryGetValue(binding.Key, out SerializedConfigEntry entry))
                {
                    missingCount++;
                    continue;
                }

                if (!binding.SetSerializedValue(entry.value, out string error))
                {
                    failedCount++;
                    LogHelper.LogWarning($"[Config] Failed to load {binding.Key}: {error}");
                }
                else
                {
                    appliedCount++;
                    LogHelper.Log($"[Config] Read {binding.Key} = {entry.value}");
                }
            }

            LogHelper.Log(
                "[Config] Loaded scene "
                    + $"'{scene.name}' from {FilePath}: {appliedCount} applied, "
                    + $"{missingCount} no stored entry, {failedCount} failed, "
                    + $"{capturedDefaults} defaults captured."
            );
        }

        static int CaptureDefaults(IEnumerable<PersistableBinding> bindings)
        {
            int captured = 0;
            foreach (var binding in bindings)
            {
                if (DefaultValues.ContainsKey(binding.Key))
                    continue;

                if (TrySerializeValue(binding.GetValue(), binding.DataType, out string value))
                {
                    DefaultValues.Add(binding.Key, value);
                    captured++;
                }
            }

            return captured;
        }

        static SerializedConfigFile ReadFile(string context = null)
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                LogHelper.Log($"[Config] Read {FormatContext(context)}found no file at {path}.");
                return new SerializedConfigFile();
            }

            try
            {
                var file = JsonUtility.FromJson<SerializedConfigFile>(File.ReadAllText(path));
                if (file == null)
                {
                    LogHelper.Log(
                        $"[Config] Read {FormatContext(context)}returned an empty config file from {path}."
                    );
                    return new SerializedConfigFile();
                }

                file.entries ??= new List<SerializedConfigEntry>();
                LogHelper.Log(
                    $"[Config] Read {FormatContext(context)}{file.entries.Count} entries from {path}."
                );
                return file;
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"[Config] Failed to read {path}: {ex.Message}");
                return new SerializedConfigFile();
            }
        }

        static void WriteFile(SerializedConfigFile file, string context = null)
        {
            string path = FilePath;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                file.version = FileVersion;
                file.entries ??= new List<SerializedConfigEntry>();
                file.entries = file
                    .entries.OrderBy(entry => entry.key, StringComparer.Ordinal)
                    .ToList();

                if (file.entries.Count == 0)
                {
                    DeleteFileAndMeta(path);
                    LogHelper.Log(
                        $"[Config] Wrote {FormatContext(context)}0 entries; deleted {path}."
                    );
                    return;
                }

                File.WriteAllText(path, JsonUtility.ToJson(file, true));
                LogHelper.Log(
                    $"[Config] Wrote {FormatContext(context)}{file.entries.Count} entries to {path}."
                );
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"[Config] Failed to write {path}: {ex.Message}");
            }
        }

        static string FormatContext(string context)
        {
            return string.IsNullOrWhiteSpace(context) ? string.Empty : $"{context}: ";
        }

        static Dictionary<string, SerializedConfigEntry> ToEntryMap(SerializedConfigFile file)
        {
            var result = new Dictionary<string, SerializedConfigEntry>(StringComparer.Ordinal);
            if (file?.entries == null)
                return result;

            foreach (var entry in file.entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.key))
                    result[entry.key] = entry;
            }

            return result;
        }

        static void DeleteFileAndMeta(string path)
        {
            if (File.Exists(path))
                File.Delete(path);

            string metaPath = path + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        static IEnumerable<PersistableBinding> GetPersistableBindings(Scene scene)
        {
            foreach (Type type in EnumerateTypes())
            {
                if (!IsConfigTargetType(type))
                    continue;

                foreach (object instance in FindSceneObjects(type, scene))
                {
                    foreach (var binding in GetPersistableBindings(type, instance, scene))
                        yield return binding;
                }
            }
        }

        static bool IsConfigTargetType(Type type)
        {
            return type.GetCustomAttribute<ConfigSettingsAttribute>() != null
                && !type.IsAbstract
                && typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        static IEnumerable<PersistableBinding> GetPersistableBindings(
            Type type,
            object instance,
            Scene scene
        )
        {
            foreach (var field in type.GetFields(MemberFlags))
            {
                var attr = field.GetCustomAttribute<ConfigSettingAttribute>();
                if (
                    attr == null
                    || field.IsInitOnly
                    || IsBackingFieldCoveredByProperty(field, type)
                    || !IsSupportedValueType(field.FieldType)
                )
                    continue;

                yield return new PersistableBinding(attr, instance, type, field, scene);
            }

            foreach (var property in type.GetProperties(MemberFlags))
            {
                var attr = property.GetCustomAttribute<ConfigSettingAttribute>();
                if (
                    attr == null
                    || !property.CanRead
                    || !property.CanWrite
                    || property.GetIndexParameters().Length > 0
                    || !IsSupportedValueType(property.PropertyType)
                )
                    continue;

                yield return new PersistableBinding(attr, instance, type, property, scene);
            }
        }

        static bool IsSupportedValueType(Type type)
        {
            return type == typeof(bool)
                || type == typeof(int)
                || type == typeof(float)
                || type == typeof(string)
                || type.IsEnum;
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
                    types = ex.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                    yield return type;
            }
        }

        static IEnumerable<object> FindSceneObjects(Type type, Scene scene)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                if (obj is Component component)
                {
                    if (component.gameObject.scene == scene)
                        yield return obj;
                }
                else if (obj is GameObject go && go.scene == scene)
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

        static bool TrySerializeValue(object rawValue, Type type, out string value)
        {
            value = null;
            if (type == typeof(string))
            {
                value = rawValue?.ToString() ?? string.Empty;
                return true;
            }

            if (rawValue == null)
                return false;

            if (type == typeof(bool))
                value = (bool)rawValue ? "true" : "false";
            else if (type == typeof(int))
                value = ((int)rawValue).ToString(Invariant);
            else if (type == typeof(float))
                value = ((float)rawValue).ToString("R", Invariant);
            else if (type.IsEnum)
                value = rawValue.ToString();
            else
                return false;

            return true;
        }

        static bool TryDeserializeValue(
            string rawValue,
            Type targetType,
            out object converted,
            out string error
        )
        {
            converted = null;
            error = null;

            if (targetType == typeof(string))
            {
                converted = rawValue ?? string.Empty;
                return true;
            }

            string text = rawValue?.Trim();
            if (targetType == typeof(bool))
            {
                if (bool.TryParse(text, out bool boolValue))
                {
                    converted = boolValue;
                    return true;
                }

                if (text == "1")
                {
                    converted = true;
                    return true;
                }

                if (text == "0")
                {
                    converted = false;
                    return true;
                }

                error = "expected true or false";
                return false;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(text, NumberStyles.Integer, Invariant, out int intValue))
                {
                    converted = intValue;
                    return true;
                }

                error = "expected an integer";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(text, NumberStyles.Float, Invariant, out float floatValue))
                {
                    converted = floatValue;
                    return true;
                }

                error = "expected a decimal number";
                return false;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    converted = int.TryParse(
                        text,
                        NumberStyles.Integer,
                        Invariant,
                        out int intValue
                    )
                        ? Enum.ToObject(targetType, intValue)
                        : Enum.Parse(targetType, text, true);
                    return true;
                }
                catch
                {
                    error = $"expected one of: {string.Join(", ", Enum.GetNames(targetType))}";
                    return false;
                }
            }

            error = $"unsupported type {targetType.Name}";
            return false;
        }

        [Serializable]
        public sealed class SerializedConfigFile
        {
            public int version = FileVersion;
            public List<SerializedConfigEntry> entries = new();
        }

        [Serializable]
        public sealed class SerializedConfigEntry
        {
            public string key;
            public string i18nKey;
            public string valueType;
            public string value;
        }

        sealed class PersistableBinding
        {
            readonly object instance;
            readonly FieldInfo field;
            readonly PropertyInfo property;
            readonly RangeAttribute range;
            readonly MinAttribute min;

            public string Key { get; }
            public string I18nKey { get; }
            public Type DataType { get; }

            public PersistableBinding(
                ConfigSettingAttribute attr,
                object instance,
                Type ownerType,
                FieldInfo field,
                Scene scene
            )
            {
                this.instance = instance;
                this.field = field;
                I18nKey = attr.I18nKey;
                DataType = field.FieldType;
                Key = MakeKey(scene, instance, ownerType, field.Name);
                range = field.GetCustomAttribute<RangeAttribute>();
                min = field.GetCustomAttribute<MinAttribute>();
            }

            public PersistableBinding(
                ConfigSettingAttribute attr,
                object instance,
                Type ownerType,
                PropertyInfo property,
                Scene scene
            )
            {
                this.instance = instance;
                this.property = property;
                I18nKey = attr.I18nKey;
                DataType = property.PropertyType;
                Key = MakeKey(scene, instance, ownerType, property.Name);
                range = property.GetCustomAttribute<RangeAttribute>();
                min = property.GetCustomAttribute<MinAttribute>();
            }

            public object GetValue()
            {
                return field != null ? field.GetValue(instance) : property.GetValue(instance);
            }

            public bool SetSerializedValue(string rawValue, out string error)
            {
                if (!TryDeserializeValue(rawValue, DataType, out object converted, out error))
                    return false;

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
                    error = ex.Message;
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

            static string MakeKey(Scene scene, object instance, Type ownerType, string memberName)
            {
                return $"{scene.name}.{GetObjectPath(instance)}.{ownerType.FullName}.{memberName}";
            }

            static string GetObjectPath(object instance)
            {
                Transform transform =
                    instance is Component component ? component.transform
                    : instance is GameObject go ? go.transform
                    : null;
                if (transform == null)
                    return instance.GetType().Name;

                var names = new Stack<string>();
                while (transform != null)
                {
                    names.Push(transform.name);
                    transform = transform.parent;
                }

                return string.Join("/", names);
            }
        }
    }
}
