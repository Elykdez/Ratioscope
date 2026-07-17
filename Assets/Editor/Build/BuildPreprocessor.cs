using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hypocycloid.Editor
{
    public class BuildPreprocessor : IPreprocessBuildWithReport
    {
        const string PATH_BUILD_CONFIG = "Assets/Editor/BuildSettings.asset";
        const string VERSION_DATE_FORMAT = "yyyyMMdd";
        const string ExtendedSemanticVersionPattern =
            @"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<prerelease>(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+(?<buildmetadata>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$";

        public int callbackOrder => 0;

        public static BuildConfig Config
        {
            get
            {
                BuildConfig config = AssetDatabase.LoadAssetAtPath<BuildConfig>(PATH_BUILD_CONFIG);
                if (config == null)
                    Debug.LogWarning($"[Build] Build config not found: {PATH_BUILD_CONFIG}");
                return config;
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            SetVersion();
        }

        static void SetVersion()
        {
            BuildConfig buildConfig = Config;
            if (buildConfig == null)
                throw new FileNotFoundException(
                    $"Build config not found: {PATH_BUILD_CONFIG}",
                    PATH_BUILD_CONFIG
                );

            string today = DateTime.Now.ToString(VERSION_DATE_FORMAT, CultureInfo.InvariantCulture);
            string versionDate = today;
            string prefix = buildConfig.prefix;
            int revision = 0;

            string oldVersion = PlayerSettings.bundleVersion?.Trim() ?? string.Empty;
            if (
                TryReadUnityVersion(
                    oldVersion,
                    out string oldPrefix,
                    out string oldDate,
                    out int oldRevision
                )
            )
            {
                prefix = oldPrefix;
                if (
                    !string.IsNullOrEmpty(oldDate)
                    && (oldDate == today || !buildConfig.isRecordDate)
                )
                    versionDate = oldDate;

                if (oldDate == versionDate)
                    revision = oldRevision + 1;
            }

            string branch = SanitizeSemVerIdentifier(BuildConfig.GetGitBranchName());
            string version = string.Format(
                CultureInfo.InvariantCulture,
                buildConfig.format,
                prefix,
                versionDate,
                branch,
                revision
            );

            if (!Regex.IsMatch(version, ExtendedSemanticVersionPattern))
                throw new InvalidOperationException(
                    $"Generated build version is not SemVer: {version}"
                );

            PlayerSettings.bundleVersion = version;
            Debug.LogWarning($"[Build] build version = {PlayerSettings.bundleVersion}");
        }

        static bool TryReadUnityVersion(
            string version,
            out string prefix,
            out string date,
            out int revision
        )
        {
            prefix = string.Empty;
            date = string.Empty;
            revision = 0;

            Match semanticMatch = Regex.Match(version, ExtendedSemanticVersionPattern);
            if (semanticMatch.Success)
            {
                prefix =
                    $"{semanticMatch.Groups["major"].Value}.{semanticMatch.Groups["minor"].Value}";
                string patch = semanticMatch.Groups["patch"].Value;
                if (IsBuildDate(patch))
                    date = patch;

                string prerelease = semanticMatch.Groups["prerelease"].Value;
                if (!string.IsNullOrEmpty(prerelease))
                {
                    string[] identifiers = prerelease.Split('.');
                    string lastIdentifier = identifiers[^1];
                    int.TryParse(
                        lastIdentifier,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out revision
                    );
                }

                return true;
            }

            return false;
        }

        static bool IsBuildDate(string value)
        {
            return value.Length == 8
                && DateTime.TryParseExact(
                    value,
                    VERSION_DATE_FORMAT,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                );
        }

        static string SanitizeSemVerIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "local";

            var builder = new StringBuilder(value.Length);
            bool previousWasHyphen = false;
            foreach (char c in value.Trim())
            {
                if (IsAsciiAlphaNumeric(c) || c == '-')
                {
                    builder.Append(c);
                    previousWasHyphen = c == '-';
                    continue;
                }

                if (previousWasHyphen)
                    continue;

                builder.Append('-');
                previousWasHyphen = true;
            }

            string identifier = builder.ToString().Trim('-');
            if (string.IsNullOrEmpty(identifier))
                return "local";

            return IsNumericWithLeadingZero(identifier) ? $"branch-{identifier}" : identifier;
        }

        static bool IsAsciiAlphaNumeric(char c)
        {
            return c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        static bool IsNumericWithLeadingZero(string value)
        {
            if (value.Length < 2 || value[0] != '0')
                return false;

            foreach (char c in value)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }
    }
}
