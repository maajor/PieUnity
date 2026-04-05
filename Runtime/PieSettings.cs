using System.Collections.Generic;
using UnityEngine;

namespace Pie
{
    [System.Serializable]
    public class PieFileToolRootDefinition
    {
        [SerializeField] private string name = "";
        [SerializeField] private string description = "";
        [SerializeField] private string relativePathFromProjectRoot = "";
        [SerializeField] private string absolutePath = "";
        [SerializeField] private bool availableInEditor = true;
        [SerializeField] private bool availableInRuntime = false;
        [SerializeField] private List<string> defaultPrefixes = new List<string>();

        public string Name => name;
        public string Description => description;
        public string RelativePathFromProjectRoot => relativePathFromProjectRoot;
        public string AbsolutePath => absolutePath;
        public bool AvailableInEditor => availableInEditor;
        public bool AvailableInRuntime => availableInRuntime;
        public IReadOnlyList<string> DefaultPrefixes => defaultPrefixes;
    }

    [CreateAssetMenu(fileName = "PieSettings", menuName = "Tools/Pie/Pie Settings")]
    public class PieSettings : ScriptableObject
    {
        [SerializeField] private List<string> extensionSearchPaths = new List<string>
        {
            "Assets/Pie/Extensions",
        };

        [SerializeField] private List<string> skillSearchPaths = new List<string>
        {
            "Assets/Pie/Skills",
        };

        [SerializeField] private List<PieFileToolRootDefinition> fileToolRoots = new List<PieFileToolRootDefinition>();
        [SerializeField] private string defaultFileToolRootEditor = "persistent";
        [SerializeField] private string defaultFileToolRootRuntime = "persistent";

        public IReadOnlyList<string> ExtensionSearchPaths => extensionSearchPaths;
        public IReadOnlyList<string> SkillSearchPaths => skillSearchPaths;
        public IReadOnlyList<PieFileToolRootDefinition> FileToolRoots => fileToolRoots;
        public string DefaultFileToolRootEditor => defaultFileToolRootEditor;
        public string DefaultFileToolRootRuntime => defaultFileToolRootRuntime;
    }
}
