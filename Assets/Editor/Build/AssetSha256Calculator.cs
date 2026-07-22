using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Hypocycloid.Editor
{
    public static class AssetSha256Calculator
    {
        const string AssetMenu = "Assets/Compute SHA-256";
        const int BufferSize = 8 * 1024 * 1024;

        [MenuItem(AssetMenu, false, 2000)]
        [MenuItem(EditorCommons.CTX + "Build/Compute SHA-256")]
        static void ComputeSelectedAssets()
        {
            List<string> files = CollectSelectedFiles();
            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Compute SHA-256",
                    "Select one or more files or folders in the Project window.",
                    "OK"
                );
                return;
            }

            long totalBytes = files.Sum(path => new FileInfo(path).Length);
            long completedBytes = 0;
            var report = new List<string>(files.Count);

            try
            {
                foreach (string file in files)
                {
                    string assetPath = FileUtil.GetProjectRelativePath(file).Replace('\\', '/');
                    string hash = ComputeHash(file, assetPath, totalBytes, completedBytes);
                    report.Add($"{hash}  {assetPath}");
                    completedBytes += new FileInfo(file).Length;
                }

                string result = string.Join("\n", report);
                EditorGUIUtility.systemCopyBuffer = result;
                Debug.Log($"SHA-256 ({report.Count} asset(s)):\n{result}");
                EditorUtility.DisplayDialog(
                    "Compute SHA-256",
                    $"Computed {report.Count} SHA-256 hash(es). The report was copied to the clipboard.",
                    "OK"
                );
            }
            catch (OperationCanceledException)
            {
                Debug.Log("SHA-256 computation cancelled.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "SHA-256 computation failed",
                    exception.GetBaseException().Message,
                    "OK"
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(AssetMenu, true)]
        static bool ValidateAssetMenu()
        {
            return Selection.assetGUIDs.Length > 0;
        }

        static List<string> CollectSelectedFiles()
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in Selection.assetGUIDs)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fullPath = Path.GetFullPath(assetPath);
                if (File.Exists(fullPath))
                {
                    files.Add(fullPath);
                    continue;
                }

                if (!Directory.Exists(fullPath))
                    continue;

                foreach (
                    string file in Directory.EnumerateFiles(
                        fullPath,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        files.Add(Path.GetFullPath(file));
                }
            }

            return files
                .OrderBy(FileUtil.GetProjectRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string ComputeHash(
            string file,
            string assetPath,
            long totalBytes,
            long completedBytes
        )
        {
            byte[] buffer = new byte[BufferSize];
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan
            );

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                float progress =
                    totalBytes == 0 ? 1f : (float)(completedBytes + stream.Position) / totalBytes;
                if (
                    EditorUtility.DisplayCancelableProgressBar(
                        "Compute SHA-256",
                        assetPath,
                        progress
                    )
                )
                {
                    throw new OperationCanceledException();
                }
            }

            return BitConverter
                .ToString(hash.GetHashAndReset())
                .Replace("-", "")
                .ToLowerInvariant();
        }
    }
}
