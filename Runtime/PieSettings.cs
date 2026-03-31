using System.Collections.Generic;
using UnityEngine;

namespace Pie
{
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

        public IReadOnlyList<string> ExtensionSearchPaths => extensionSearchPaths;
        public IReadOnlyList<string> SkillSearchPaths => skillSearchPaths;
    }
}
