using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>Configures the explicitly requested Mixamo downloads and merges them into both alien controllers.</summary>
    public static class MixamoEnemyAnimationSetup
    {
        public const string AnimationFolder = "Assets/Game/Art/Enemies/AlienMonster/Animations";
        private const string AlienModel = "Assets/Game/Art/Enemies/AlienMonster/AlienMonster.fbx";
        private const string ControllerFolder = "Assets/Game/Art/Enemies/Authored";
        private const float AttackSequenceSeconds = 1.2f;

        private static readonly Dictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Zombie Attack", "Attack" },
            { "Mutant Swiping", "Attack" },
            { "Zombie Death", "Die" },
            { "Mutant Dying", "Die" },
            { "Standing React Small From Front", "Hit" },
            { "Zombie Running", "Moving" },
        };

        private static readonly string[] RequiredMixamoBones =
        {
            "Hips", "Spine", "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm",
            "RightHand", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot"
        };

        [MenuItem("Armory/Art/Configure Mixamo Enemy Animations")]
        public static void Configure()
        {
            var mappedPaths = FindMappedFbxPaths().ToArray();
            foreach (var path in mappedPaths) ConfigureImporter(path);
            ConfigureControllers(true);
            AssetDatabase.SaveAssets();
            Debug.Log("Mixamo enemy animation setup complete. Configured " + mappedPaths.Length + " recognized download(s).");
        }

        /// <summary>Returns the controller role for an exact documented download name, or null for an unknown file.</summary>
        public static string RoleForFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            Roles.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var role);
            return role;
        }

        /// <summary>Merges available clips into GruntRig and SwarmRig without replacing motions for missing clips.</summary>
        public static void ConfigureControllers(bool warnForMissing)
        {
            var clips = FindMappedFbxPaths()
                .Select(path => new { Role = RoleForFileName(path), Clip = LoadClip(path) })
                .Where(item => item.Clip != null)
                .GroupBy(item => item.Role)
                .ToDictionary(group => group.Key, group => group.First().Clip);

            foreach (var name in new[] { "GruntRig", "SwarmRig" })
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerFolder + "/" + name + ".controller");
                if (controller == null) continue;
                EnsureParameter(controller, "Moving", AnimatorControllerParameterType.Bool);
                var machine = controller.layers[0].stateMachine;
                if (clips.TryGetValue("Moving", out var run))
                    FindOrAddState(machine, "Moving").motion = run;
                AddTriggeredState(controller, "Attack", clips);
                AddTriggeredState(controller, "Hit", clips);
                AddTriggeredState(controller, "Die", clips);
                EditorUtility.SetDirty(controller);
            }

            AssetDatabase.SaveAssets();
            if (warnForMissing)
            {
                var missing = new[] { "Attack", "Hit", "Die", "Moving" }.Where(role => !clips.ContainsKey(role)).ToArray();
                if (missing.Length > 0)
                    Debug.LogWarning("Mixamo clips not found for: " + string.Join(", ", missing) + ". Existing controller motions were preserved.");
            }
        }

        private static IEnumerable<string> FindMappedFbxPaths()
        {
            if (!AssetDatabase.IsValidFolder(AnimationFolder)) yield break;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && RoleForFileName(path) != null)
                    yield return path;
            }
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;

            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(AlienModel);
            var source = sourceModel?.GetComponentInChildren<Animator>()?.avatar;
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool copiedAvatar = source != null && source.isValid && source.isHuman && HasCompatibleMixamoSkeleton(sourceModel, imported);
            if (copiedAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = source;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                Debug.LogWarning("Using this file's humanoid avatar because its skeleton could not be verified against AlienMonster: " + path);
            }

            bool loop = RoleForFileName(path) == "Moving";
            string downloadedName = Path.GetFileNameWithoutExtension(path);
            var takes = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var take in takes)
            {
                take.name = downloadedName;
                take.loopTime = loop;
                take.loopPose = loop;
                take.keepOriginalOrientation = true;
                take.keepOriginalPositionY = true;
                take.keepOriginalPositionXZ = true;
                take.lockRootRotation = true;
                take.lockRootHeightY = true;
                take.lockRootPositionXZ = true;
            }
            importer.clipAnimations = takes;
            importer.SaveAndReimport();
            if (copiedAvatar)
            {
                var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<Animator>()?.avatar;
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.sourceAvatar = null;
                    importer.SaveAndReimport();
                    Debug.LogWarning("AlienMonster avatar was incompatible after import; using this file's valid humanoid avatar: " + path);
                }
            }
        }

        private static bool HasCompatibleMixamoSkeleton(GameObject source, GameObject candidate)
        {
            if (source == null || candidate == null) return false;
            var candidateByName = candidate.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var sourceBones = source.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name.StartsWith("mixamorig:", StringComparison.Ordinal))
                .ToArray();
            var normalizedNames = new HashSet<string>(candidateByName.Keys.Select(NormalizedBoneName), StringComparer.OrdinalIgnoreCase);
            if (!RequiredMixamoBones.All(normalizedNames.Contains)) return false;
            foreach (var sourceBone in sourceBones)
            {
                if (!candidateByName.TryGetValue(sourceBone.name, out var candidateBone)) return false;
                bool sourceParentIsBone = sourceBone.parent != null && sourceBone.parent.name.StartsWith("mixamorig:", StringComparison.Ordinal);
                if (sourceParentIsBone && (candidateBone.parent == null || candidateBone.parent.name != sourceBone.parent.name)) return false;
            }
            return sourceBones.Length > 0;
        }

        private static string NormalizedBoneName(string name) => name.Contains(":") ? name.Substring(name.LastIndexOf(':') + 1) : name;

        private static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

        private static void AddTriggeredState(AnimatorController controller, string role, IDictionary<string, AnimationClip> clips)
        {
            if (!clips.TryGetValue(role, out var clip)) return;
            EnsureParameter(controller, role, AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var state = FindOrAddState(machine, role);
            state.motion = clip;
            state.speed = role == "Attack" ? Mathf.Max(.01f, clip.length / AttackSequenceSeconds) : 1f;
            var enter = machine.anyStateTransitions.FirstOrDefault(transition => transition.destinationState == state &&
                transition.conditions.Any(condition => condition.parameter == role));
            if (enter == null)
            {
                enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0f, role);
            }
            enter.hasExitTime = false;
            enter.duration = .06f;
            enter.canTransitionToSelf = role == "Attack";
            if (role != "Die" && !state.transitions.Any(transition => transition.destinationState != null && transition.destinationState.name == "Idle"))
            {
                var exit = state.AddTransition(FindOrAddState(machine, "Idle"));
                exit.hasExitTime = true;
                exit.exitTime = 1f;
                exit.duration = .1f;
            }
        }

        private static AnimatorState FindOrAddState(AnimatorStateMachine machine, string name)
        {
            var state = machine.states.FirstOrDefault(child => child.state.name == name).state;
            return state != null ? state : machine.AddState(name);
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (!controller.parameters.Any(parameter => parameter.name == name)) controller.AddParameter(name, type);
        }
    }

    /// <summary>Schedules the same idempotent setup after a recognized FBX is added to the animation folder.</summary>
    public sealed class MixamoEnemyAnimationPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Any(path => path.StartsWith(MixamoEnemyAnimationSetup.AnimationFolder + "/", StringComparison.OrdinalIgnoreCase)
                && MixamoEnemyAnimationSetup.RoleForFileName(path) != null)) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                try { MixamoEnemyAnimationSetup.Configure(); }
                finally { scheduled = false; }
            };
        }
    }
}
