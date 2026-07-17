using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hypocycloid.Core;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Hypocycloid.Editor
{
    public class SaveEditorWindow : EditorWindow
    {
        ConfigSettingsPersistence.SerializedConfigFile snapshot;
        string rawJson = string.Empty;
        string search = string.Empty;
        string status = string.Empty;
        bool showRawJson;
        bool entryEditsDirty;
        Vector2 entryScroll;
        Vector2 rawScroll;
        List<ConfigSettingsPersistence.SerializedConfigEntry> entries = new();
        ReorderableList entryList;

        [MenuItem(EditorCommons.CTX + "Data/Local Save Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<SaveEditorWindow>("Save Data");
            window.minSize = new Vector2(560, 420);
            window.RefreshData();
        }

        void OnEnable()
        {
            RefreshData();
        }

        void OnFocus()
        {
            if (!entryEditsDirty)
                RefreshData();
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawInfo();
            DrawSearch();

            if (showRawJson)
                DrawRawJson();
            else
                DrawEntries();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    RefreshData();

                using (new EditorGUI.DisabledScope(!entryEditsDirty))
                {
                    if (
                        GUILayout.Button(
                            "Apply Entry Edits",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(120)
                        )
                    )
                        ApplyEntryEdits();
                }

                if (
                    GUILayout.Button(
                        "Save Current Scene Values",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(165)
                    )
                )
                {
                    ConfigSettingsPersistence.SaveActiveSceneSettings();
                    AssetDatabase.Refresh();
                    RefreshData("Saved current scene config values.");
                }

                if (
                    GUILayout.Button(
                        "Delete Scene Entries",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(135)
                    )
                )
                {
                    if (
                        EditorUtility.DisplayDialog(
                            "Delete Scene Entries",
                            "Delete serialized config entries for the active scene?",
                            "Delete",
                            "Cancel"
                        )
                    )
                    {
                        ConfigSettingsPersistence.DeleteActiveSceneSettings();
                        AssetDatabase.Refresh();
                        RefreshData("Deleted active scene entries.");
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Reveal", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    RevealFile();

                if (GUILayout.Button("Open JSON", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    OpenFile();

                if (
                    GUILayout.Button("Delete File", EditorStyles.toolbarButton, GUILayout.Width(85))
                )
                    DeleteFile();
            }
        }

        void DrawInfo()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Path", ConfigSettingsPersistence.FilePath);

            int count = entries?.Count ?? 0;
            string exists = ConfigSettingsPersistence.FileExists ? "exists" : "missing";
            int version = snapshot?.version ?? 0;
            string dirty = entryEditsDirty ? ", unsaved editor edits" : string.Empty;
            EditorGUILayout.LabelField(
                "State",
                $"{exists}, version {version}, {count} differential entries{dirty}"
            );

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        void DrawSearch()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField("Search", search);
                showRawJson = GUILayout.Toggle(
                    showRawJson,
                    "Raw JSON",
                    EditorStyles.miniButton,
                    GUILayout.Width(80)
                );
            }
        }

        void DrawEntries()
        {
            IReadOnlyList<ConfigSettingsPersistence.SerializedConfigEntry> filteredEntries =
                FilterEntries().ToList();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                $"Differential Entries ({filteredEntries.Count})",
                EditorStyles.boldLabel
            );

            if (!string.IsNullOrWhiteSpace(search))
                EditorGUILayout.HelpBox(
                    "Clear Search to add, remove, or reorder entries.",
                    MessageType.Info
                );

            entryScroll = EditorGUILayout.BeginScrollView(entryScroll);
            if (!string.IsNullOrWhiteSpace(search))
            {
                DrawFilteredEntries(filteredEntries);
            }
            else
            {
                EnsureEntryList();
                entryList.DoLayoutList();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawFilteredEntries(
            IReadOnlyList<ConfigSettingsPersistence.SerializedConfigEntry> filteredEntries
        )
        {
            if (filteredEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No serialized config entries match the current filter.",
                    MessageType.None
                );
                return;
            }

            foreach (var entry in filteredEntries)
                DrawEntry(entry);
        }

        void EnsureEntryList()
        {
            if (entryList != null)
                return;

            entryList = new ReorderableList(
                entries,
                typeof(ConfigSettingsPersistence.SerializedConfigEntry),
                true,
                true,
                true,
                true
            )
            {
                elementHeight = (EditorGUIUtility.singleLineHeight + 2f) * 4f + 8f,
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Stored Overrides");
                },
                drawElementCallback = DrawEditableEntry,
                onAddCallback = list =>
                {
                    entries.Add(
                        new ConfigSettingsPersistence.SerializedConfigEntry
                        {
                            key = string.Empty,
                            i18nKey = string.Empty,
                            valueType = typeof(string).FullName,
                            value = string.Empty,
                        }
                    );
                    list.index = entries.Count - 1;
                    entryEditsDirty = true;
                },
                onRemoveCallback = list =>
                {
                    if (list.index < 0 || list.index >= entries.Count)
                        return;

                    entries.RemoveAt(list.index);
                    list.index = Mathf.Clamp(list.index, 0, entries.Count - 1);
                    entryEditsDirty = true;
                },
                onReorderCallback = _ => entryEditsDirty = true,
            };
        }

        void DrawEditableEntry(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= entries.Count)
                return;

            var entry = entries[index];
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 3f;

            EditorGUI.BeginChangeCheck();
            entry.key = EditorGUI.TextField(LineRect(rect, y), "Key", entry.key);
            y += lineHeight + 2f;
            entry.i18nKey = EditorGUI.TextField(LineRect(rect, y), "I18n Key", entry.i18nKey);
            y += lineHeight + 2f;
            entry.valueType = EditorGUI.TextField(LineRect(rect, y), "Value Type", entry.valueType);
            y += lineHeight + 2f;
            entry.value = EditorGUI.TextField(LineRect(rect, y), "Value", entry.value);

            if (EditorGUI.EndChangeCheck())
                entryEditsDirty = true;
        }

        static Rect LineRect(Rect rect, float y)
        {
            return new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight);
        }

        void DrawEntry(ConfigSettingsPersistence.SerializedConfigEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawReadonlyText("Key", entry.key);
                DrawReadonlyText("I18n Key", entry.i18nKey);
                DrawReadonlyText("Value Type", entry.valueType);
                DrawReadonlyText("Value", entry.value);
            }
        }

        static void DrawReadonlyText(string label, string value)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect labelRect = new(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            Rect valueRect =
                new(
                    rect.x + EditorGUIUtility.labelWidth,
                    rect.y,
                    rect.width - EditorGUIUtility.labelWidth,
                    rect.height
                );

            EditorGUI.LabelField(labelRect, label);
            EditorGUI.SelectableLabel(valueRect, value ?? string.Empty, EditorStyles.textField);
        }

        void DrawRawJson()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Raw JSON", EditorStyles.boldLabel);
            rawScroll = EditorGUILayout.BeginScrollView(rawScroll);
            EditorGUILayout.TextArea(rawJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        IEnumerable<ConfigSettingsPersistence.SerializedConfigEntry> FilterEntries()
        {
            string filter = search?.Trim();
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(filter) || Matches(entry, filter))
                    yield return entry;
            }
        }

        static bool Matches(ConfigSettingsPersistence.SerializedConfigEntry entry, string filter)
        {
            return Contains(entry.key, filter)
                || Contains(entry.i18nKey, filter)
                || Contains(entry.valueType, filter)
                || Contains(entry.value, filter);
        }

        static bool Contains(string text, string filter)
        {
            return text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void RefreshData(string message = null)
        {
            snapshot = ConfigSettingsPersistence.ReadSerializedFile();
            entries = CloneEntries(snapshot.entries);
            entryList = null;
            entryEditsDirty = false;
            rawJson = ConfigSettingsPersistence.ReadSerializedJson();
            status = message ?? string.Empty;
            Repaint();
        }

        void ApplyEntryEdits()
        {
            ConfigSettingsPersistence.WriteSerializedFile(
                new ConfigSettingsPersistence.SerializedConfigFile
                {
                    version = snapshot?.version ?? 1,
                    entries = CloneEntries(entries),
                }
            );
            AssetDatabase.Refresh();
            RefreshData("Applied serialized entry edits.");
        }

        static List<ConfigSettingsPersistence.SerializedConfigEntry> CloneEntries(
            IEnumerable<ConfigSettingsPersistence.SerializedConfigEntry> source
        )
        {
            return (source ?? Enumerable.Empty<ConfigSettingsPersistence.SerializedConfigEntry>())
                .Select(entry => new ConfigSettingsPersistence.SerializedConfigEntry
                {
                    key = entry.key,
                    i18nKey = entry.i18nKey,
                    valueType = entry.valueType,
                    value = entry.value,
                })
                .ToList();
        }

        static void RevealFile()
        {
            string path = ConfigSettingsPersistence.FilePath;
            if (File.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                EditorUtility.RevealInFinder(ConfigSettingsPersistence.DirectoryPath);
        }

        static void OpenFile()
        {
            string path = ConfigSettingsPersistence.FilePath;
            if (File.Exists(path))
                EditorUtility.OpenWithDefaultApp(path);
            else
                EditorUtility.DisplayDialog(
                    "Open JSON",
                    "Serialized config file does not exist yet.",
                    "OK"
                );
        }

        void DeleteFile()
        {
            if (!ConfigSettingsPersistence.FileExists)
            {
                RefreshData("Serialized config file does not exist.");
                return;
            }

            if (
                !EditorUtility.DisplayDialog(
                    "Delete Serialized Config",
                    "Delete the full serialized config JSON file?",
                    "Delete",
                    "Cancel"
                )
            )
            {
                return;
            }

            ConfigSettingsPersistence.DeleteSerializedFile();
            AssetDatabase.Refresh();
            RefreshData("Deleted serialized config file.");
        }
    }
}
