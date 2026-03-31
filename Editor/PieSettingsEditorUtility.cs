using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pie.Editor
{
    public static class PieSettingsEditorUtility
    {
        [MenuItem("Tools/Pie/Create Pie Settings Asset")]
        public static void CreateSettingsAsset()
        {
            var assetPath = PieProjectPaths.GetDefaultSettingsAssetPath();
            var existing = AssetDatabase.LoadAssetAtPath<PieSettings>(assetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                EnsureFolder(dir);
            }

            var asset = ScriptableObject.CreateInstance<PieSettings>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[Pie] Created Pie settings asset at {assetPath}");
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            var normalized = assetFolderPath.Replace("\\", "/");
            var parts = normalized.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
