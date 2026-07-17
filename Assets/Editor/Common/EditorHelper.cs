using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hypocycloid.Editor
{
    /// <summary>
    /// Shared helpers for Hypocycloid Unity editor tooling.
    /// </summary>
    public static class EditorHelper
    {
        /// <summary>
        /// Gets the absolute project root path that contains the Assets folder.
        /// </summary>
        public static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// Escapes backslashes and double quotes for safe string embedding.
        /// </summary>
        public static string EscapeString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Gets Unity's internal build player handler and reports whether the lookup succeeded.
        /// </summary>
        public static Action<BuildPlayerOptions> GetBuildPlayerHandler(
            out bool success,
            out FieldInfo buildPlayerHandlerField
        )
        {
            var buildPlayerWindowType = typeof(BuildPlayerWindow);
            buildPlayerHandlerField = buildPlayerWindowType.GetField(
                "buildPlayerHandler",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            if (buildPlayerHandlerField != null)
            {
                success = true;
                return buildPlayerHandlerField.GetValue(null) as Action<BuildPlayerOptions>;
            }
            success = false;
            return null;
        }

        /// <summary>
        /// Reads scalar values from a minimal indented YAML map into dotted key paths.
        /// </summary>
        public static Dictionary<string, string> ReadYamlPairs(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stack = new List<(int indent, string key)>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.TrimStart().StartsWith("#"))
                    continue;
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0)
                    continue;

                int separator = trimmed.IndexOf(':');
                if (separator <= 0)
                    continue;

                int indent = line.Length - trimmed.Length;
                string key = trimmed[..separator].Trim();
                string value = StripInlineYamlComment(trimmed[(separator + 1)..])
                    .Trim()
                    .Trim('"', '\'');

                while (stack.Count > 0 && stack[^1].indent >= indent)
                    stack.RemoveAt(stack.Count - 1);

                string fullKey =
                    stack.Count == 0
                        ? key
                        : string.Join(".", stack.ConvertAll(s => s.key)) + "." + key;

                if (value.Length == 0)
                    stack.Add((indent, key));
                else
                    values[fullKey] = value;
            }

            return values;
        }

        /// <summary>
        /// Updates existing scalar leaf values in a nested YAML file by dotted key path.
        /// </summary>
        public static void Save(string path, Dictionary<string, string> updates)
        {
            string[] lines = File.ReadAllLines(path);
            var pending = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
            var stack = new List<(int indent, string key)>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;

                int separator = trimmed.IndexOf(':');
                if (separator <= 0)
                    continue;

                int indent = line.Length - trimmed.Length;
                string key = trimmed[..separator].Trim();
                int colonPos = indent + separator;
                int valueStart = colonPos + 1;
                while (valueStart < line.Length && line[valueStart] == ' ')
                    valueStart++;
                bool isHeader = valueStart >= line.Length;

                while (stack.Count > 0 && stack[^1].indent >= indent)
                    stack.RemoveAt(stack.Count - 1);

                string fullKey =
                    stack.Count == 0
                        ? key
                        : string.Join(".", stack.ConvertAll(s => s.key)) + "." + key;

                if (isHeader)
                {
                    stack.Add((indent, key));
                    continue;
                }

                if (!pending.TryGetValue(fullKey, out string newValue))
                    continue;

                int padding = Math.Max(1, valueStart - (colonPos + 1));
                lines[i] = line[..(colonPos + 1)] + new string(' ', padding) + newValue;
                pending.Remove(fullKey);
            }

            if (pending.Count > 0)
                throw new InvalidOperationException(
                    $"Installer config keys not found in {path}: {string.Join(", ", pending.Keys)}"
                );

            File.WriteAllLines(path, lines);
        }

        /// <summary>
        /// Runs dotnet with the provided arguments and logs captured output in the Unity editor.
        /// </summary>
        public static int RunDotNet(string workingDirectory, params string[] arguments)
        {
            return RunDotNet(workingDirectory, null, arguments);
        }

        /// <summary>
        /// Runs dotnet, streaming the child process output to the Unity console as it arrives
        /// and invoking <paramref name="onPump"/> on the main thread roughly every
        /// <see cref="PumpIntervalMs"/> ms so callers can drive a live progress bar. The child's
        /// stdout/stderr is flushed in batches rather than dumped once at exit, so long-running
        /// scripts (e.g. publish.cs git push / uploads) report progress instead of hanging silently.
        /// </summary>
        public static int RunDotNet(
            string workingDirectory,
            Action onPump,
            params string[] arguments
        )
        {
            var queued = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var errors = new StringBuilder();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            foreach (string argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                    queued.Enqueue(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                    return;
                queued.Enqueue(args.Data);
                lock (errors)
                    errors.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Pump on the main thread: WaitForExit(timeout) yields control every interval so we
            // can flush buffered output and repaint the progress bar while the child runs.
            while (!process.WaitForExit(PumpIntervalMs))
            {
                FlushQueued(queued);
                onPump?.Invoke();
            }

            // Parameterless WaitForExit ensures the async stdout/stderr readers have drained.
            process.WaitForExit();
            FlushQueued(queued);
            onPump?.Invoke();

            // Output already streamed live above; only surface stderr as an error summary when the
            // run failed (git/vpk write plenty of benign status to stderr on success).
            if (process.ExitCode != 0 && errors.Length > 0)
                Debug.LogError(errors.ToString());

            return process.ExitCode;
        }

        const int PumpIntervalMs = 150;

        static void FlushQueued(System.Collections.Concurrent.ConcurrentQueue<string> queued)
        {
            var batch = new StringBuilder();
            while (queued.TryDequeue(out string line))
                batch.AppendLine(line);
            if (batch.Length > 0)
                Debug.Log(batch.ToString());
        }

        /// <summary>
        /// Displays a cancelable progress bar with estimated time remaining.
        /// </summary>
        /// <param name="title">The base title of the progress bar (estimated time will be appended).</param>
        /// <param name="info">Current item information (e.g., asset path).</param>
        /// <param name="currentIndex">Current item index (0-based).</param>
        /// <param name="totalCount">Total number of items.</param>
        /// <param name="stopwatch">Stopwatch for timing the operation.</param>
        /// <returns>True if the user canceled the operation, false otherwise.</returns>
        public static bool DisplayProgress(
            string title,
            string info,
            int currentIndex,
            int totalCount,
            Stopwatch stopwatch
        )
        {
            string estRemaining = "calculating...";
            if (currentIndex > 0)
            {
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double avgSecPerItem = elapsedSec / currentIndex;
                int remaining = totalCount - currentIndex;
                double estSec = avgSecPerItem * remaining;
                TimeSpan estTime = TimeSpan.FromSeconds(estSec);
                if (estTime.TotalMinutes >= 1)
                {
                    estRemaining = $"{(int)estTime.TotalMinutes} min {estTime.Seconds} sec";
                }
                else
                {
                    estRemaining = $"{estTime.Seconds} sec";
                }
            }

            string progressTitle = $"{title} - Est. remaining: {estRemaining}";
            float progress = (float)currentIndex / totalCount;
            return EditorUtility.DisplayCancelableProgressBar(
                progressTitle,
                $"[{currentIndex + 1}/{totalCount}]: {info}",
                progress
            );
        }

        // -------- Private helpers --------

        static string StripInlineYamlComment(string value)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\'' && !inDoubleQuote)
                    inSingleQuote = !inSingleQuote;
                else if (c == '"' && !inSingleQuote)
                    inDoubleQuote = !inDoubleQuote;
                else if (
                    c == '#'
                    && !inSingleQuote
                    && !inDoubleQuote
                    && (i == 0 || char.IsWhiteSpace(value[i - 1]))
                )
                    return value[..i];
            }

            return value;
        }
    }
}
