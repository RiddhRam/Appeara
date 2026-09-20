using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>
    /// Regenerates the build-safe name to prefab table for Assets/Game/Art/Effects. Run it after the art builders
    /// add or rename an effect; nothing else references those prefabs, so an unbuilt table means silent no-art.
    /// </summary>
    public static class EffectLibraryBuilder
    {
        private const string EffectsFolder = "Assets/Game/Art/Effects";
        private const string AssetPath = "Assets/Game/Enemies/Resources/EffectLibrary.asset";

        [MenuItem("Armory/Art/Build Effect Library")]
        public static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<EffectLibrary>(AssetPath);
            bool created = library == null;
            if (created) library = ScriptableObject.CreateInstance<EffectLibrary>();

            var entries = new List<EffectLibrary.Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { EffectsFolder }).Distinct())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                entries.Add(new EffectLibrary.Entry { Key = Path.GetFileNameWithoutExtension(path), Prefab = prefab });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            library.Entries = entries.ToArray();

            if (created) AssetDatabase.CreateAsset(library, AssetPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"Effect library: {entries.Count} effects -> {AssetPath}");
        }
    }
}
