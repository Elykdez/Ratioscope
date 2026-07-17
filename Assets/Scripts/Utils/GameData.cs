using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// Lightweight persistent key/value store over <see cref="PlayerPrefs"/>.
    /// Keys are namespaced and scoped to either the whole app (global) or the
    /// active scene, so the same logical key can hold distinct per-scene values.
    /// Mirrors the storage design used by ContextManager in sibling projects.
    /// </summary>
    public static class GameData
    {
        const string Scope = "Hypocycloid";
        const string GlobalScope = "g";

        public static void SetInt(string key, int value, bool global = false) =>
            PlayerPrefs.SetInt(Resolve(global, key), value);

        public static int GetInt(string key, int defaultValue = 0, bool global = false) =>
            PlayerPrefs.GetInt(Resolve(global, key), defaultValue);

        public static void SetFloat(string key, float value, bool global = false) =>
            PlayerPrefs.SetFloat(Resolve(global, key), value);

        public static float GetFloat(string key, float defaultValue = 0f, bool global = false) =>
            PlayerPrefs.GetFloat(Resolve(global, key), defaultValue);

        public static void SetString(string key, string value, bool global = false) =>
            PlayerPrefs.SetString(Resolve(global, key), value);

        public static string GetString(string key, string defaultValue = "", bool global = false) =>
            PlayerPrefs.GetString(Resolve(global, key), defaultValue);

        // PlayerPrefs has no native bool; stored as 0/1.
        public static void SetBool(string key, bool value, bool global = false) =>
            PlayerPrefs.SetInt(Resolve(global, key), value ? 1 : 0);

        public static bool GetBool(string key, bool defaultValue = false, bool global = false) =>
            PlayerPrefs.GetInt(Resolve(global, key), defaultValue ? 1 : 0) != 0;

        // Enums are persisted by their underlying int value. Boxing here is
        // negligible unless called hundreds of thousands of times.
        public static void SetEnum<T>(string key, T value, bool global = false)
            where T : Enum => PlayerPrefs.SetInt(Resolve(global, key), (int)(object)value);

        public static T GetEnum<T>(string key, T defaultValue = default, bool global = false)
            where T : Enum =>
            (T)(object)PlayerPrefs.GetInt(Resolve(global, key), (int)(object)defaultValue);

        public static bool Has(string key, bool global = false) =>
            PlayerPrefs.HasKey(Resolve(global, key));

        public static void Delete(string key, bool global = false) =>
            PlayerPrefs.DeleteKey(Resolve(global, key));

        /// <summary>
        /// Flush pending writes to disk. PlayerPrefs autosaves on a clean quit;
        /// call this after writes you cannot afford to lose to a crash.
        /// </summary>
        public static void Save() => PlayerPrefs.Save();

        static string Resolve(bool global, string key) =>
            $"{Scope}_{(global ? GlobalScope : SceneManager.GetActiveScene().name)}_{key}";
    }
}
