using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hypocycloid.Editor
{
    [CreateAssetMenu(fileName = "BuildConfig", menuName = "Application/Context/BuildConfig")]
    public class BuildConfig : ScriptableObject
    {
        const string cmd_args_git = "rev-parse --abbrev-ref HEAD";

        [Header("Generics")]
        public string prefix = "0.1";
        public string format = "{0}.{1}-{2}.{3}";
        public bool isRecordDate;

        public static string GetGitBranchName()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = cmd_args_git,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                    },
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                Debug.Log($"[Build] Current Git branch: {output}");
                return output;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build] Failed to get Git branch: {e.Message}");
                return "void";
            }
        }
    }
}
