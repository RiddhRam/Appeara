using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Armory.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>Authors visual assets only. Enemy movement, targeting and material tinting remain runtime responsibilities.</summary>
    public static class MonsterArtBuilder
    {
        private const string Root = "Assets/Game/Art/Enemies";
        private const string Output = Root + "/Authored";
        private const string Table = "Assets/Game/Enemies/Resources/EnemyVisuals.asset";

        [MenuItem("Armory/Art/Build Monster Visuals")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            // The staged package requests humanoid auto-mapping and supplies Idle2 mappings. Preserve retargeting between
            // the model's Armature hierarchy and the separately supplied Mixamo Idle2 skeleton.
            foreach (string path in new[] { "AlienMonster/AlienMonster.fbx", "AlienMonster/Animations/Idle2.fbx" })
            {
                var importer = AssetImporter.GetAtPath(Root + "/" + path) as ModelImporter;
                if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.SaveAndReimport();
                }
            }
            var entries = new List<EnemyVisuals.Entry>();
            entries.Add(BuildOne(EnemyKind.Grunt, "AlienMonster/AlienMonster.fbx", "AlienMonster/AlienMonster.png", null, 2.1f, false));
            entries.Add(BuildOne(EnemyKind.Swarm, "AlienMonster/AlienMonster.fbx", "AlienMonster/AlienMonster.png", null, .9f, false));
            entries.Add(BuildOne(EnemyKind.Armored, "GunBot/Gun_Bot.fbx", "GunBot/GB_C.jpg", null, 3.2f, true));
            entries.Add(BuildOne(EnemyKind.Fast, "FloatingRobot/R01.fbx", "FloatingRobot/robot01fbx_robot2_AlbedoTransparency.png", "FloatingRobot/robot01fbx_robot2_Emission.png", 1.6f, true));
            entries.Add(BuildOne(EnemyKind.Shielded, "Wendy/wendy_Scene.obj", "Wendy/tex_wendy.jpg", null, 2.4f, true));
            MixamoEnemyAnimationSetup.ConfigureControllers(false);
            Directory.CreateDirectory(Path.GetDirectoryName(Table));
            AssetDatabase.Refresh();
            var table = AssetDatabase.LoadAssetAtPath<EnemyVisuals>(Table);
            if (table == null) { table = ScriptableObject.CreateInstance<EnemyVisuals>(); AssetDatabase.CreateAsset(table, Table); }
            table.Entries = entries.ToArray();
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("Authored five textured monster prefabs and EnemyVisuals table. Runtime factory integration is separate.");
        }

        private static EnemyVisuals.Entry BuildOne(EnemyKind kind, string modelPath, string texturePath, string emissionPath, float height, bool hover)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + modelPath);
            if (source == null) throw new InvalidOperationException("Run Armory/Import Enemy Models first: " + modelPath);
            var root = new GameObject(kind + " Visual");
            try
            {
                // Outer root is always identity; Motion is separate from fitted model transforms.
                var motion = new GameObject("Motion"); motion.transform.SetParent(root.transform, false);
                var fit = new GameObject("Fit"); fit.transform.SetParent(motion.transform, false);
                var model = UnityEngine.Object.Instantiate(source, fit.transform, false); model.name = "Model";
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (var camera in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera);
                foreach (var light in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
                foreach (var listener in model.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
                if (kind == EnemyKind.Shielded) PoseWendy(model);
                var material = Material(kind.ToString(), texturePath, emissionPath);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
                }
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderers: " + modelPath);
                var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var fitting = ModelFit.Solve(bounds, height);
                fit.transform.localScale = Vector3.one * fitting.Scale;
                fit.transform.localPosition = fitting.Offset;

                string moving = "";
                var clips = AssetDatabase.LoadAllAssetsAtPath(Root + "/" + modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__") && c.name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                var embeddedMotion = clips.FirstOrDefault();
                if (modelPath.StartsWith("AlienMonster/"))
                    clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(Root + "/AlienMonster/Animations/Idle2.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")));
                var animator = model.GetComponentInChildren<Animator>();
                if (clips.Count > 0 && animator != null)
                {
                    var idle = Pick(clips, "idle", "idel", "rest") ?? clips[0];
                    var walk = Pick(clips, kind == EnemyKind.Swarm ? "run" : "walk", "walk", "run") ?? embeddedMotion ?? idle;
                    animator.runtimeAnimatorController = RigController(kind.ToString(), idle, walk);
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    moving = "Moving";
                    Debug.Log(kind + " animation: idle=" + idle.name + ", moving=" + walk.name + (walk == idle ? " (no distinct locomotion take supplied)" : ""));
                }
                if (hover)
                {
                    // Static imports remain honest rigid-body hover motion, not an invented skeletal walk.
                    var animatorRoot = root.AddComponent<Animator>();
                    animatorRoot.runtimeAnimatorController = HoverController(kind.ToString(), height);
                    animatorRoot.applyRootMotion = false;
                    moving = "";
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + kind + "Visual.prefab");
                return new EnemyVisuals.Entry { Kind = kind, Prefab = prefab, Height = height, EulerOffset = Vector3.zero, MovingParameter = moving };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static AnimationClip Pick(IEnumerable<AnimationClip> clips, params string[] words)
        {
            foreach (var word in words) { var clip = clips.FirstOrDefault(c => c.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0); if (clip != null) return clip; }
            return null;
        }

        private static void PoseWendy(GameObject model)
        {
            // Wendy is a static OBJ. Author a new mesh in model space, retaining the original topology,
            // UVs and normals; no skeleton, runtime deformation or source modification is introduced.
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            var rendererBounds = model.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).ToArray();
            var bounds = rendererBounds[0]; foreach (var b in rendererBounds.Skip(1)) bounds.Encapsulate(b);
            float height = bounds.size.y;
            float halfWidth = bounds.extents.x;
            for (int index = 0; index < filters.Length; index++)
            {
                var filter = filters[index]; var source = filter.sharedMesh;
                var vertices = source.vertices; var normals = source.normals; var tangents = source.tangents;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var world = filter.transform.TransformPoint(vertices[i]);
                    float offset = world.x - bounds.center.x;
                    float lateral = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfWidth * .21f, halfWidth * .46f, Mathf.Abs(offset)));
                    float upperBody = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.42f, .64f, (world.y - bounds.min.y) / height));
                    float angle = -Mathf.Sign(offset) * 35f * lateral * upperBody;
                    var rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                    var shoulder = new Vector3(bounds.center.x + Mathf.Sign(offset) * halfWidth * .285f, bounds.min.y + height * .695f, world.z);
                    vertices[i] = filter.transform.InverseTransformPoint(shoulder + rotation * (world - shoulder));
                    if (normals.Length == vertices.Length)
                        normals[i] = filter.transform.InverseTransformDirection(rotation * filter.transform.TransformDirection(normals[i])).normalized;
                    if (tangents.Length == vertices.Length)
                    {
                        Vector3 tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                        tangent = filter.transform.InverseTransformDirection(rotation * filter.transform.TransformDirection(tangent)).normalized;
                        tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
                    }
                }
                var posed = UnityEngine.Object.Instantiate(source); posed.name = "ShieldedPosed_" + index;
                posed.vertices = vertices;
                if (normals.Length == vertices.Length) posed.normals = normals;
                if (tangents.Length == vertices.Length) posed.tangents = tangents;
                posed.RecalculateBounds();
                string path = Output + "/ShieldedPosed_" + index + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { AssetDatabase.CreateAsset(posed, path); existing = posed; }
                else { EditorUtility.CopySerialized(posed, existing); UnityEngine.Object.DestroyImmediate(posed); }
                EditorUtility.SetDirty(existing); filter.sharedMesh = existing;
            }
        }

        private static Material Material(string name, string texture, string emission)
        {
            string path = Output + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is required.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.enableInstancing = true;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + texture));
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", .25f);
            material.SetFloat("_Metallic", name == "Armored" || name == "Fast" ? .55f : .05f);
            if (emission != null)
            {
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + emission));
                material.SetColor("_EmissionColor", Color.white * .8f); material.EnableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material); return material;
        }

        private static AnimatorController Controller(string name)
        {
            string path = Output + "/" + name + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            return controller;
        }

        private static AnimatorController RigController(string name, AnimationClip idle, AnimationClip walk)
        {
            var controller = Controller(name + "Rig");
            if (!controller.parameters.Any(parameter => parameter.name == "Moving")) controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var idleState = machine.AddState("Idle"); idleState.motion = LoopCopy(name + "Idle", idle);
            var movingState = machine.AddState("Moving"); movingState.motion = LoopCopy(name + "Moving", walk);
            machine.defaultState = idleState;
            var enter = idleState.AddTransition(movingState); enter.hasExitTime = false; enter.duration = .12f; enter.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            var leave = movingState.AddTransition(idleState); leave.hasExitTime = false; leave.duration = .12f; leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            EditorUtility.SetDirty(controller); return controller;
        }

        private static AnimationClip LoopCopy(string name, AnimationClip source)
        {
            string path = Output + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(source, clip); clip.name = name;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true; settings.keepOriginalPositionXZ = true; settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip); return clip;
        }

        private static AnimatorController HoverController(string name, float height)
        {
            string path = Output + "/" + name + "Hover.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = 30;
            float amplitude = height * .035f;
            // Stay at/above the fitted deck, no root travel and zero pitch/roll bounds surprises.
            var curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1.25f, amplitude, 0f, 0f), new Keyframe(2.5f, 0f, 0f, 0f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Motion", typeof(Transform), "m_LocalPosition.y"), curve);
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            var controller = Controller(name + "Hover"); var state = controller.layers[0].stateMachine.AddState("Hover"); state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state; EditorUtility.SetDirty(controller); return controller;
        }
    }
}
