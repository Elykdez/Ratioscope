using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using System.IO;
#elif UNITY_WEBGL
using System.Runtime.InteropServices;
#endif

namespace Hypocycloid.Utils
{
    public static class LogHelper
    {
        const int MinJsonLogLength = 100;
        const int MaxJsonLogLength = 4096;
        const int JsonStringPreviewLength = 30;
        const string JsonStringTruncatedMarker = "... (truncated)";
        const string DataImagePrefix = "data:image/";

        static long start_time;
        static long now_time;
        public static int idx = 0;
        static readonly Dictionary<string, long> timesav = new();

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        public static extern void LogWebGL(string message, string style);
#endif

        /// <summary>
        /// Detects base64 and large array in json and truncate them.
        /// </summary>
        public static string TruncateJson(string payload)
        {
#if ENABLE_LOG || UNITY_EDITOR
            // Early exit for small payloads
            if (string.IsNullOrEmpty(payload) || payload.Length < MinJsonLogLength)
                return payload;

            bool isLongPayload = payload.Length > MaxJsonLogLength;
            int sourceLength = isLongPayload ? MaxJsonLogLength : payload.Length;
            StringBuilder result = new(sourceLength + 96);
            int i = 0;
            while (i < sourceLength)
            {
                // Fast path: copy until we hit a quote
                if (payload[i] != '"')
                {
                    result.Append(payload[i++]);
                    continue;
                }

                int startQuote = i++;
                int endQuote = FindJsonStringEnd(payload, i);
                if (endQuote < 0)
                {
                    // Broken JSON?
                    result.Append(payload, startQuote, sourceLength - startQuote);
                    break;
                }

                i = endQuote + 1;
                int strStart = startQuote + 1;
                int strLength = endQuote - strStart;

                // Look ahead to see if this is a key or a value (check for ':')
                bool isKey = IsJsonKey(payload, i);

                result.Append('"');
                if (
                    !isKey
                    && strLength > JsonStringPreviewLength
                    && IsTruncatableJsonString(payload, strStart, strLength)
                )
                {
                    result.Append(payload, strStart, JsonStringPreviewLength);
                    result.Append(JsonStringTruncatedMarker);
                }
                else
                {
                    int appendLength = Math.Min(strLength, Math.Max(0, sourceLength - strStart));
                    result.Append(payload, strStart, appendLength);
                    if (strStart + strLength > sourceLength)
                    {
                        result.Append(JsonStringTruncatedMarker);
                        result.Append('"');
                        break;
                    }
                }
                result.Append('"');
            }

            if (isLongPayload)
                result.Append($"... (truncated, {payload.Length} chars total)");

            return result.ToString();
#else
            return payload;
#endif
        }

        static int FindJsonStringEnd(string payload, int startIndex)
        {
            bool isEscaped = false;
            for (int i = startIndex; i < payload.Length; i++)
            {
                char c = payload[i];
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (c == '"')
                    return i;
            }

            return -1;
        }

        static bool IsJsonKey(string payload, int endQuoteNextIndex)
        {
            int i = endQuoteNextIndex;
            while (i < payload.Length && char.IsWhiteSpace(payload[i]))
                i++;

            return i < payload.Length && payload[i] == ':';
        }

        static bool IsTruncatableJsonString(string payload, int startIndex, int length)
        {
            return IsLikelyBase64(payload, startIndex, length)
                || IsLikelyDataArray(payload, startIndex, length);
        }

        static bool IsLikelyBase64(string payload, int startIndex, int length)
        {
            if (length < MinJsonLogLength)
                return false;

            if (StartsWithOrdinalIgnoreCase(payload, startIndex, length, DataImagePrefix))
                return true;

            int endIndex = startIndex + length;
            for (int i = startIndex; i < endIndex; i++)
            {
                char c = payload[i];
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='))
                    return false;
            }

            return true;
        }

        static bool IsLikelyDataArray(string payload, int startIndex, int length)
        {
            if (length < 10)
                return false;

            int commaCount = 0;
            int dotCount = 0;
            int digitCount = 0;
            int endIndex = startIndex + length;

            for (int i = startIndex; i < endIndex; i++)
            {
                char c = payload[i];
                if (c == ',')
                    commaCount++;
                else if (c == '.')
                    dotCount++;
                else if (char.IsDigit(c))
                    digitCount++;
            }

            return commaCount > 5 && dotCount > 5 && digitCount > 20;
        }

        static bool StartsWithOrdinalIgnoreCase(
            string payload,
            int startIndex,
            int length,
            string prefix
        )
        {
            return length >= prefix.Length
                && string.Compare(
                    payload,
                    startIndex,
                    prefix,
                    0,
                    prefix.Length,
                    StringComparison.OrdinalIgnoreCase
                ) == 0;
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void Log(object content)
        {
            Debug.Log($"[{SceneManager.GetActiveScene().name}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void Assert(bool condition)
        {
            Debug.Assert(condition);
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void Assert(bool condition, object content)
        {
            Debug.Assert(condition, $"[{SceneManager.GetActiveScene().name}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(object content)
        {
            Debug.LogWarning($"[{SceneManager.GetActiveScene().name}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void LogError(object content)
        {
            Debug.LogError($"[{SceneManager.GetActiveScene().name}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void Log(string scope, object content)
        {
            Debug.Log($"[{scope}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void Log(Color logColor, object content)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var color = ColorUtility.ToHtmlStringRGB(logColor);
            LogWebGL($"[{SceneManager.GetActiveScene().name}]: %c{content}", $"color: #{color}");
#else
            Log($"<color=#{ColorUtility.ToHtmlStringRGB(logColor)}>{content}</color>");
#endif
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void LogFrame(Color logColor, object content)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var color = ColorUtility.ToHtmlStringRGB(logColor);
            LogWebGL(
                $"[Frame {Time.frameCount}] [{SceneManager.GetActiveScene().name}]: %c{content}",
                $"color: #{color}"
            );
#else
            Log(
                $"<color=#{ColorUtility.ToHtmlStringRGB(logColor)}>[Frame {Time.frameCount}] {content}</color>"
            );
#endif
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string scope, object content)
        {
            Debug.LogWarning($"[{scope}]: {content}");
        }

        [Conditional("ENABLE_LOG")]
        [Conditional("UNITY_EDITOR")]
        public static void LogError(string scope, object content)
        {
            Debug.LogError($"[{scope}]: {content}");
        }

        /// <summary>
        /// Debug only
        /// </summary>
        /// <returns></returns>
        public static string GetCallingContextName(int iterations)
        {
#if UNITY_EDITOR
            var stackTrace = new StackTrace();
            var frame = stackTrace.GetFrame(iterations); // 1 = 调用者帧
            var method = frame.GetMethod();
            return $"{method.DeclaringType.Name}_{method.Name}";
#else
            return $"";
#endif
        }

        #region execute_timer

        public static void LogStart(string timetag)
        {
            Log("Timer", $"->> Record {timetag}...");
            start_time = GetTimeStamp();
            if (timesav.ContainsKey(timetag))
                timesav[timetag] = start_time;
            else
                timesav.Add(timetag, start_time);
        }

        public static void LogEnd(string timetag, bool record = false)
        {
            if (timesav.ContainsKey(timetag))
            {
                now_time = GetTimeStamp() - timesav[timetag];
                if (now_time > 0)
                {
                    Log("Timer", $"->> Ended {timetag} with {now_time * 0.001f}s.");
#if UNITY_EDITOR
                    if (record)
                    {
                        string directory = Path.Combine(Application.dataPath, "../Temp");
                        string tempPath = Path.Combine(directory, "CheckRuntime.csv");
                        Directory.CreateDirectory(directory);
                        using var fileStream = new StreamWriter(directory, true);
                        fileStream.Write("Executing, cost(ms)\n");
                        // var fileStream = new StreamWriter(path);
                        // fileStream.WriteLine(str);
                        fileStream.Write($"{timetag}, {now_time}\n");
                        fileStream.Close();
                        SystemHelper.RevealFile(tempPath);
                    }
#endif
                    // timesav.Remove(timetag);
                }
            }
            else
            {
                Log("Test", $"Record not found.");
            }
        }

        public static long GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
            long ret = Convert.ToInt64(ts.TotalMilliseconds);
            return ret;
        }

        public static void SetStartTime(string timetag)
        {
            start_time = GetTimeStamp();
            if (timesav.ContainsKey(timetag))
                timesav[timetag] = start_time;
            else
                timesav.Add(timetag, start_time);
        }

        public static long GetDisTime(string timetag, long aTime = 0)
        {
            if (timesav.ContainsKey(timetag))
            {
                now_time = GetTimeStamp() - timesav[timetag] + aTime;
            }
            return now_time;
        }

        public static void ClearTime(string timetag)
        {
            if (timesav.ContainsKey(timetag))
            {
                timesav.Remove(timetag);
            }
        }

        public static bool CheckTimeTag(string timetag)
        {
            return timesav.ContainsKey(timetag);
        }

        #endregion

        // ---------------- Private ----------------
    }
}
