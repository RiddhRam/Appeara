using System.Collections.Generic;
using System.IO;
using System.Linq;
using Armory.Core;
using UnityEditor;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>
    /// Brings the downloaded enemy models into the project and builds the kind-to-model table.
    /// The raw downloads are staged in ThirdParty/EnemyModels, outside Assets, so nothing imports until this runs.
    /// </summary>
    public static class EnemyModelSetup
    {
        private const string Staging = "ThirdParty/EnemyModels";
        private const string Destination = "Assets/Game/Art/Enemies";
        private const string AssetPath = "Assets/Game/Enemies/Resources/EnemyVisuals.asset";

        /// <summary>
        /// Default assignments. They are a starting point, not a decision: the table is editable in the inspector
        /// and a kind with no model keeps its primitive placeholder.
        /// AlienMonster is the only rigged-and-animated walker, so it takes the two ground kinds; the floating
        /// robot flies and is animated, so it takes Fast; Gun Bot is heavy and static, so it takes Armored.
        /// </summary>
        private static readonly (EnemyKind Kind, string Model, float Height, string Moving)[] Defaults =
        {
            (EnemyKind.Grunt, "AlienMonster/AlienMonster.fbx", 2.1f, ""),
            (EnemyKind.Swarm, "AlienMonster/AlienMonster.fbx", 0.9f, ""),
            (EnemyKind.Armored, "GunBot/Gun_Bot.fbx", 3.2f, ""),
            (EnemyKind.Fast, "FloatingRobot/R01.fbx", 1.6f, ""),
            (EnemyKind.Shielded, "Wendy/wendy_Scene.obj", 2.4f, ""),
        };

        [MenuItem("Armory/Import Enemy Models")]
        public static void Import()
        {
            string staging = Path.Combine(Directory.GetCurrentDirectory(), Staging);
            if (!Directory.Exists(staging))
            {
                Debug.LogError("No staged models at " + Staging + ". Nothing imported.");
                return;
            }
            Directory.CreateDirectory(Destination);
            var copied = new List<string>();
            foreach (string source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = source.Substring(staging.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string target = Path.Combine(Destination, relative).Replace('\\', '/');
                if (File.Exists(target)) continue;   // never clobber a model somebody has already set up
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target);
                if (!target.EndsWith(".meta")) copied.Add(target);
            }
            AssetDatabase.Refresh();
            foreach (string path in copied) Configure(path);
            AssetDatabase.Refresh();
            Debug.Log(copied.Count == 0
                ? "Enemy models were already imported; nothing copied."
                : "Imported " + copied.Count + " enemy model files into " + Destination + ". Run Armory/Prepare Enemy Visuals next.");
        }

        /// <summary>Imported models default to no animation and read-only meshes; enemies need both the other way.</summary>
        private static void Configure(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.importAnimation = true;
            importer.animationType = importer.importedTakeInfos.Length > 0 ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
        }

        [MenuItem("Armory/Prepare Enemy Visuals")]
        public static void Prepare()
        {
            string folder = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            var visuals = AssetDatabase.LoadAssetAtPath<EnemyVisuals>(AssetPath);
            if (visuals == null)
            {
                visuals = ScriptableObject.CreateInstance<EnemyVisuals>();
                AssetDatabase.CreateAsset(visuals, AssetPath);
            }
            var entries = new List<EnemyVisuals.Entry>();
            var missing = new List<string>();
            foreach (var (kind, model, height, moving) in Defaults)
            {
                string path = Destination + "/" + model;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { missing.Add(path); continue; }
                // Keep whatever the table already says for this kind: re-running must not undo inspector tuning.
                var existing = visuals.Entries?.FirstOrDefault(e => e != null && e.Kind == kind && e.Prefab != null);
                entries.Add(existing ?? new EnemyVisuals.Entry { Kind = kind, Prefab = prefab, Height = height, MovingParameter = moving });
            }
            visuals.Entries = entries.ToArray();
            EditorUtility.SetDirty(visuals);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0) Debug.LogWarning("Not yet imported, those kinds keep their placeholder: " + string.Join(", ", missing));
            Debug.Log("Enemy visuals prepared with " + entries.Count + " of " + Defaults.Length + " kinds. Edit the asset to reassign.");
        }
    }
}
