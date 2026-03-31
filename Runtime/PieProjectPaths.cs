using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pie
{
    public static class PieProjectPaths
    {
        private const string DefaultSettingsAssetPath = "Assets/Pie/PieSettings.asset";
        private const string DefaultExtensionRelativePath = "Assets/Pie/Extensions";
        private const string DefaultSkillRelativePath = "Assets/Pie/Skills";
        private const string DefaultProjectMemoryRelativePath = "Assets/Pie/AGENTS.md";
        private const string RuntimeStateFolderName = "Pie";

        public static string GetExtensionSearchPathsJson(string projectRoot)
        {
            return BuildJsonArray(GetExtensionSearchPaths(projectRoot));
        }

        public static string GetExtensionSearchPathsJson(string projectRoot, PieSettings settingsOverride)
        {
            return BuildJsonArray(GetExtensionSearchPaths(projectRoot, settingsOverride));
        }

        public static string GetSkillSearchPathsJson(string projectRoot)
        {
            return BuildJsonArray(GetSkillSearchPaths(projectRoot));
        }

        public static string GetSkillSearchPathsJson(string projectRoot, PieSettings settingsOverride)
        {
            return BuildJsonArray(GetSkillSearchPaths(projectRoot, settingsOverride));
        }

        public static string GetPrimaryExtensionDirectory(string projectRoot)
        {
            return GetPrimaryPath(projectRoot, GetExtensionSearchPaths(projectRoot), DefaultExtensionRelativePath);
        }

        public static string GetPrimarySkillDirectory(string projectRoot)
        {
            return GetPrimaryPath(projectRoot, GetSkillSearchPaths(projectRoot), DefaultSkillRelativePath);
        }

        public static string GetProjectMemoryPath(string projectRoot)
        {
            return NormalizePath(Path.Combine(projectRoot ?? "", DefaultProjectMemoryRelativePath));
        }

        public static string GetRuntimeStateRoot()
        {
            return NormalizePath(Path.Combine(Application.persistentDataPath, RuntimeStateFolderName));
        }

        public static string GetSessionsDirectory()
        {
            return NormalizePath(Path.Combine(GetRuntimeStateRoot(), "sessions"));
        }

        public static string GetDefaultSettingsAssetPath()
        {
            return DefaultSettingsAssetPath;
        }

        public static IReadOnlyList<string> GetExtensionSearchPaths(string projectRoot)
        {
            return GetExtensionSearchPaths(projectRoot, null);
        }

        public static IReadOnlyList<string> GetExtensionSearchPaths(string projectRoot, PieSettings settingsOverride)
        {
            return ResolvePaths(projectRoot, LoadConfiguredPaths(settings => settings.ExtensionSearchPaths, settingsOverride), DefaultExtensionRelativePath);
        }

        public static IReadOnlyList<string> GetSkillSearchPaths(string projectRoot)
        {
            return GetSkillSearchPaths(projectRoot, null);
        }

        public static IReadOnlyList<string> GetSkillSearchPaths(string projectRoot, PieSettings settingsOverride)
        {
            return ResolvePaths(projectRoot, LoadConfiguredPaths(settings => settings.SkillSearchPaths, settingsOverride), DefaultSkillRelativePath);
        }

        private static string GetPrimaryPath(string projectRoot, IReadOnlyList<string> resolvedPaths, string fallbackRelativePath)
        {
            if (resolvedPaths != null && resolvedPaths.Count > 0)
                return resolvedPaths[0];

            return NormalizePath(Path.Combine(projectRoot ?? "", fallbackRelativePath));
        }

        private static List<string> ResolvePaths(string projectRoot, IReadOnlyList<string> configuredPaths, string fallbackRelativePath)
        {
            var resolved = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (configuredPaths != null)
            {
                for (var i = 0; i < configuredPaths.Count; i++)
                {
                    var path = ResolveProjectPath(projectRoot, configuredPaths[i]);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (seen.Add(path))
                        resolved.Add(path);
                }
            }

            if (resolved.Count == 0)
            {
                var fallback = ResolveProjectPath(projectRoot, fallbackRelativePath);
                if (!string.IsNullOrEmpty(fallback))
                    resolved.Add(fallback);
            }

            return resolved;
        }

        private static IReadOnlyList<string> LoadConfiguredPaths(Func<PieSettings, IReadOnlyList<string>> selector, PieSettings settingsOverride)
        {
            if (settingsOverride != null)
                return selector(settingsOverride);

#if UNITY_EDITOR
            var settings = LoadSettingsAsset();
            if (settings == null)
                return null;

            return selector(settings);
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        private static PieSettings LoadSettingsAsset()
        {
            var direct = AssetDatabase.LoadAssetAtPath<PieSettings>(DefaultSettingsAssetPath);
            if (direct != null)
                return direct;

            var guids = AssetDatabase.FindAssets("t:PieSettings");
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<PieSettings>(assetPath);
                if (asset != null)
                    return asset;
            }

            return null;
        }
#endif

        private static string ResolveProjectPath(string projectRoot, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            var normalized = configuredPath.Replace("\\", "/").Trim();
            if (Path.IsPathRooted(normalized))
                return NormalizePath(normalized);

            return NormalizePath(Path.Combine(projectRoot ?? "", normalized));
        }

        private static string NormalizePath(string value)
        {
            return string.IsNullOrEmpty(value)
                ? value
                : Path.GetFullPath(value).Replace("\\", "/");
        }

        private static string BuildJsonArray(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return "[]";

            var escaped = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
                escaped[i] = "\"" + EscapeJson(values[i] ?? "") + "\"";

            return "[" + string.Join(",", escaped) + "]";
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
