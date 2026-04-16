using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pie
{
    [Serializable]
    public sealed class PieUnityContextSnapshotPayload
    {
        public string projectRoot = "";
        public string mode = "";
        public string productName = "";
        public string unityVersion = "";
        public string sceneName = "";
        public string scenePath = "";
        public string selectedObject = "";
        public int rootObjectCount;
        public string[] rootObjects;
        public bool hasProjectMemory;
        public string[] skillSearchPaths;
    }

    public static class PieUnityContextSnapshot
    {
        public static string BuildJson(bool isEditor, string projectRoot)
        {
            var activeScene = SceneManager.GetActiveScene();
            var roots = activeScene.IsValid() ? activeScene.GetRootGameObjects() : new GameObject[0];
            var rootNames = new string[Math.Min(roots.Length, 12)];
            for (var i = 0; i < rootNames.Length; i++)
                rootNames[i] = roots[i] != null ? roots[i].name : "";

            var payload = new PieUnityContextSnapshotPayload
            {
                projectRoot = projectRoot ?? "",
                mode = isEditor ? "editor" : "runtime",
                productName = PieUnityCapabilitiesBootstrap.ProductName ?? "",
                unityVersion = Application.unityVersion,
                sceneName = activeScene.name ?? "",
                scenePath = activeScene.path ?? "",
                selectedObject = GetSelectedObjectName(isEditor),
                rootObjectCount = roots.Length,
                rootObjects = rootNames,
                hasProjectMemory = File.Exists(PieProjectPaths.GetProjectMemoryPath(projectRoot)),
                skillSearchPaths = ToArray(PieProjectPaths.GetSkillSearchPaths(projectRoot)),
            };

            return JsonUtility.ToJson(payload);
        }

        private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> list)
        {
            if (list == null || list.Count == 0)
                return new string[0];
            var items = new string[list.Count];
            for (var i = 0; i < list.Count; i++)
                items[i] = list[i] ?? "";
            return items;
        }

        private static string GetSelectedObjectName(bool isEditor)
        {
#if UNITY_EDITOR
            if (isEditor && Selection.activeGameObject != null)
                return Selection.activeGameObject.name ?? "";
#endif
            return "";
        }
    }
}
