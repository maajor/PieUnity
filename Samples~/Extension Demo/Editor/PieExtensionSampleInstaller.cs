using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pie.Samples.Editor
{
    [InitializeOnLoad]
    public static class PieExtensionSampleInstaller
    {
        private const string ExtensionFileName = "demo-host-tools.js";
        private const string InstallerScriptName = "PieExtensionSampleInstaller";

        static PieExtensionSampleInstaller()
        {
            EditorApplication.delayCall += EnsureInstalled;
        }

        public static void EnsureInstalled()
        {
            try
            {
                PieExtensionSampleHost.EnsureRegistered();

                var sourcePath = FindSampleExtensionSource();
                if (string.IsNullOrEmpty(sourcePath))
                {
                    Debug.LogWarning("[Pie Sample] Could not locate demo-host-tools.js.txt from the imported sample.");
                    return;
                }

                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[Pie Sample] Sample extension source does not exist: {sourcePath}");
                    return;
                }

                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    return;

                var targetDir = PieProjectPaths.GetPrimaryExtensionDirectory(projectRoot);
                Directory.CreateDirectory(targetDir);

                var targetPath = Path.Combine(targetDir, ExtensionFileName);
                var sourceText = File.ReadAllText(sourcePath);
                var targetText = File.Exists(targetPath) ? File.ReadAllText(targetPath) : null;

                if (targetText != null)
                {
                    if (string.Equals(sourceText, targetText, StringComparison.Ordinal))
                        return;

                    Debug.LogWarning($"[Pie Sample] Skipped overwriting existing extension at {targetPath}. Delete it or replace it manually if you want the packaged sample version.");
                    return;
                }

                File.WriteAllText(targetPath, sourceText);
                Debug.Log($"[Pie Sample] Installed extension demo to {targetPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pie Sample] Failed to install extension demo: {ex.Message}");
            }
        }

        private static string FindSampleExtensionSource()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            var packageSource = Path.GetFullPath(Path.Combine(
                projectRoot,
                "Packages",
                "com.pie.agent",
                "Samples~",
                "Extension Demo",
                "Extensions",
                "demo-host-tools.js.txt"));

            if (File.Exists(packageSource))
                return packageSource;

            var scriptGuids = AssetDatabase.FindAssets($"{InstallerScriptName} t:Script");
            foreach (var guid in scriptGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith($"{InstallerScriptName}.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var editorDir = Path.GetDirectoryName(assetPath);
                if (string.IsNullOrEmpty(editorDir))
                    continue;

                var sourcePath = Path.GetFullPath(Path.Combine(projectRoot, editorDir, "..", "Extensions", "demo-host-tools.js.txt"));
                if (File.Exists(sourcePath))
                {
                    return sourcePath;
                }
            }

            return null;
        }
    }
}
