using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>
    /// Adds (or refreshes) one "Armory" root in SpaceStation.unity. Everything else is built at runtime, so if the
    /// scene conflicts in a merge, take the teammate's version and re-run this menu.
    /// </summary>
    public static class ArmorySceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SpaceStation.unity";

        [MenuItem("Armory/Setup SpaceStation Scene")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play Mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var root = GameObject.Find("Armory");
            if (root == null)
            {
                root = new GameObject("Armory");
                Undo.RegisterCreatedObjectUndo(root, "Create Armory root");
            }
            var game = root.GetComponent<ArmoryGame>();
            if (game == null) game = root.AddComponent<ArmoryGame>();

            // Teammates' station map: centre the arena on its hologram radar, which plays the station core.
            var station = GameObject.Find("Space Station Scene");
            var hologram = station != null ? station.transform.Find("hologram_ground") : null;
            if (hologram != null)
            {
                root.transform.position = new Vector3(hologram.position.x, 0f, hologram.position.z);
                game.BuildFloor = false;
                game.BuildCoreVisual = false;
                game.CoreReachRadius = 8f;
                game.PadRadius = 11f;
            }

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child.name.StartsWith("Teleport Pad") || child.name.StartsWith("Speed Pad") || child.GetComponent<TeleportPad>() != null) Object.DestroyImmediate(child.gameObject);
            }
            for (int i = 0; i < 4; i++)
            {
                var pad = new GameObject("Speed Pad " + (i + 1));
                pad.transform.SetParent(root.transform, false);
                pad.transform.localPosition = Quaternion.Euler(0f, 180f + i * 90f, 0f) * Vector3.forward * game.PadRadius;
                pad.AddComponent<TeleportPad>();
            }
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log("Armory: SpaceStation scene ready. Press Play (Quest Link running for VR, or desktop controls).");
        }

        [MenuItem("Armory/Open SpaceStation Scene")]
        public static void Open() => EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
