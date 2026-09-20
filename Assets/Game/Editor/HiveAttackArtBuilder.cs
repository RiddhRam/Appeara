using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Armory.Editor
{
    /// <summary>Offline pose authoring only; the encounter owns attack scheduling and damage.</summary>
    public static class HiveAttackArtBuilder
    {
        public const string Folder = "Assets/Game/Art/HiveAvatar";
        private const string Model = "Assets/AlienAnimal/Alien Animal_Fbx_7.4.fbx";
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        [MenuItem("Armory/Art/Build Hive Attack Animations")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder + "/Animations");
            AssetDatabase.Refresh();
            var root = PrefabUtility.LoadPrefabContents(Folder + "/HiveAvatarVisual.prefab");
            try
            {
                var animator = root.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;
                var rig = animator.transform;
                var mouth = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Mouth_Socket");
                if (mouth == null)
                {
                    mouth = new GameObject("Mouth_Socket").transform;
                    mouth.SetParent(Find(rig, "Bone.028"), false);
                    mouth.position = Find(rig, "Bone.028").position + root.transform.forward * .8f;
                    mouth.rotation = root.transform.rotation;
                }
                var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().ToArray();
                var idle = clips.First(c => c.name.EndsWith("Idle_Aggressive"));
                var death = clips.First(c => c.name.EndsWith("Die_1"));
                var controller = (AnimatorController)animator.runtimeAnimatorController;
                var stomp = Bake(rig, idle, "Stomp", 2.3f, false, Event(1.5f, "OnStompImpact"), Event(2.28f, "OnAttackRecovered"));
                var fireball = Bake(rig, idle, "Fireball", 1.75f, false, Event(1.25f, "OnFireballRelease"), Event(1.73f, "OnAttackRecovered"));
                var laser = Bake(rig, idle, "Laser", 4.5f, false, Event(1.5f, "OnLaserStart"), Event(3.5f, "OnLaserEnd"), Event(4.48f, "OnAttackRecovered"));
                var sweepLoop = Bake(rig, idle, "LaserSweepLoop", 4f, true);
                var enrage = Bake(rig, idle, "Enrage", 1.5f, false);
                var settled = Bake(rig, death, "DeathSettle", 4.5f, false);
                AddAttack(controller, "Stomp", stomp);
                AddAttack(controller, "Fireball", fireball);
                AddAttack(controller, "Laser", laser);
                AddParameter(controller, "Enraged", AnimatorControllerParameterType.Bool);
                AddParameter(controller, "Enrage", AnimatorControllerParameterType.Trigger);
                AddAttack(controller, "Enrage", enrage);
                var machine = controller.layers[0].stateMachine;
                State(machine, "LaserSweepLoop").motion = sweepLoop;
                foreach (var pair in new[] { new[] { "Idle", "Idle_Aggressive" }, new[] { "Walk", "Walk-Cycle" }, new[] { "Claw", "Attack_Hit" }, new[] { "Bite", "Attack_Bite" } })
                {
                    var original = clips.First(c => c.name.EndsWith(pair[1]));
                    bool locomotion = pair[0] == "Idle" || pair[0] == "Walk";
                    var grounded = Bake(rig, original, pair[0] + "Grounded", original.length, locomotion,
                        locomotion ? Array.Empty<AnimationEvent>() : new[] { Event(original.length - .02f, "OnAttackRecovered") });
                    foreach (var child in machine.states) if (child.state.name == pair[0] || child.state.name == pair[0] + "Attack") child.state.motion = grounded;
                }
                foreach (var state in machine.states)
                    if (state.state.name == "Death" || state.state.name == "Die") state.state.motion = settled;
                // Explicit variants let code choose faster locomotion without accelerating attack events.
                var normalIdle = machine.states.First(s => s.state.name == "Idle").state;
                var normalWalk = machine.states.First(s => s.state.name == "Walk").state;
                var rageIdle = State(machine, "EnragedIdle"); rageIdle.motion = normalIdle.motion; rageIdle.speed = 1.2f;
                var rageWalk = State(machine, "EnragedWalk"); rageWalk.motion = normalWalk.motion; rageWalk.speed = 1.25f;
                Link(normalIdle, rageIdle, "Enraged", true);
                Link(normalWalk, rageWalk, "Enraged", true);
                Link(rageIdle, rageWalk, "Moving", true);
                Link(rageWalk, rageIdle, "Moving", false);
                Link(rageIdle, normalIdle, "Enraged", false);
                Link(rageWalk, normalWalk, "Enraged", false);
                ((AnimationClip)normalIdle.motion).SampleAnimation(rig.gameObject, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/HiveAvatarVisual.prefab");
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("Hive attack art built: six clips, mouth socket, controller states and exact event timings.");
        }

        private static AnimationEvent Event(float time, string name) => new AnimationEvent { time = time, functionName = name, messageOptions = SendMessageOptions.DontRequireReceiver };
        private static float Ease(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        private static float Pulse(float time, float peak, float end) => time < peak ? Ease(time / peak) : 1 - Ease((time - peak) / (end - peak));

        private static AnimationClip Bake(Transform rig, AnimationClip source, string name, float duration, bool loop, params AnimationEvent[] events)
        {
            var bones = rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig && (t.name.StartsWith("Bone") || t.name == "Armature" || t.name == "breathing" || t.name == "animation_Hip" || t.name.StartsWith("Chitin Scale"))).ToArray();
            var positions = bones.Select(t => t.localPosition).ToArray();
            var rotations = bones.Select(t => t.localRotation).ToArray();
            var scales = bones.Select(t => t.localScale).ToArray();
            var curves = new AnimationCurve[bones.Length, 10];
            for (int b = 0; b < bones.Length; b++) for (int c = 0; c < 10; c++) curves[b, c] = new AnimationCurve();
            var hip = Find(rig, "animation_Hip");
            var chest = Find(rig, "Bone.005");
            var head = Find(rig, "Bone.008");
            var jaw = Find(rig, "Bone.011");
            var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            var groundMesh = new UnityEngine.Mesh();
            var axis = rig.root.right;
            int frames = Mathf.CeilToInt(duration * 30);
            for (int f = 0; f <= frames; f++)
            {
                float t = duration * f / frames;
                for (int b = 0; b < bones.Length; b++) { bones[b].localPosition = positions[b]; bones[b].localRotation = rotations[b]; bones[b].localScale = scales[b]; }
                source.SampleAnimation(rig.gameObject, name == "DeathSettle" ? Mathf.Min(t, 1.5f) : name.EndsWith("Grounded") ? Mathf.Min(t, source.length - .001f) : 0f);
                var feet = new[] { "Bone.005_L.004", "Bone.005_R.004", "Bone.009_L.004", "Bone.009_R.004" }.Select(n => Find(rig, n)).ToArray();
                var targets = feet.Select(x => x.position).ToArray();
                var footRotations = feet.Select(x => x.rotation).ToArray();
                if (name == "Stomp")
                {
                    float lift = t <= 1.2f ? Ease(t / 1.2f) : t <= 1.5f ? 1 - Ease((t - 1.2f) / .3f) : 0;
                    float recoil = t > 1.5f ? Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 1.5f) / .8f)) : 0;
                    hip.position += Vector3.up * (1.0f * lift - .3f * recoil);
                    chest.rotation = Quaternion.AngleAxis(-18 * lift + 5 * recoil, axis) * chest.rotation;
                    for (int i = 0; i < 2; i++) targets[i] += Vector3.up * 3.1f * lift - rig.root.forward * .8f * lift;
                }
                else if (name == "Fireball")
                {
                    float charge = t < 1 ? Ease(t) : t < 1.25f ? 1 - 1.4f * Ease((t - 1) / .25f) : -.4f * (1 - Ease((t - 1.25f) / .5f));
                    chest.rotation = Quaternion.AngleAxis(-8 * charge, axis) * chest.rotation;
                    head.rotation = Quaternion.AngleAxis(-25 * charge, axis) * head.rotation;
                    jaw.rotation = Quaternion.AngleAxis(20 * Mathf.Max(0, charge), axis) * jaw.rotation;
                }
                else if (name == "Laser" || name == "LaserSweepLoop")
                {
                    float brace = name == "LaserSweepLoop" ? 1 : t < 1.5f ? Ease(t / 1.5f) : t < 3.5f ? 1 : 1 - Ease(t - 3.5f);
                    float sweep = name == "LaserSweepLoop" ? -60 * Mathf.Cos(t * Mathf.PI / 2) : t < 1.5f ? -60 * brace : t < 3.5f ? Mathf.Lerp(-60, 60, Ease((t - 1.5f) / 2)) : 60 * brace;
                    chest.rotation = Quaternion.AngleAxis(-7 * brace, axis) * chest.rotation;
                    head.rotation = Quaternion.AngleAxis(sweep, Vector3.up) * Quaternion.AngleAxis(-18 * brace, axis) * head.rotation;
                    hip.position -= Vector3.up * .25f * brace;
                    Flare(bones, .28f * brace);
                }
                else if (name == "Enrage")
                {
                    float roar = Pulse(t, .65f, 1.5f);
                    chest.rotation = Quaternion.AngleAxis(-15 * roar, axis) * chest.rotation;
                    head.rotation = Quaternion.AngleAxis(-30 * roar, axis) * head.rotation;
                    hip.position -= Vector3.up * .35f * roar;
                    Flare(bones, .55f * roar);
                }
                else if (name == "DeathSettle" && t > 1.5f)
                {
                    float s = Mathf.Clamp01((t - 1.5f) / (duration - 1.5f));
                    chest.rotation = Quaternion.AngleAxis(5 * Ease(s) + 2.5f * Mathf.Sin(s * Mathf.PI * 3) * (1 - s), axis) * chest.rotation;
                    head.rotation = Quaternion.AngleAxis(8 * Ease(s), axis) * head.rotation;
                }
                if (name != "DeathSettle" && !name.EndsWith("Grounded"))
                    for (int i = 0; i < feet.Length; i++) { SolveLeg(feet[i], targets[i]); feet[i].rotation = footRotations[i]; }
                skin.BakeMesh(groundMesh, true);
                float minimum = float.PositiveInfinity;
                foreach (var vertex in groundMesh.vertices) minimum = Mathf.Min(minimum, skin.transform.TransformPoint(vertex).y - rig.root.position.y);
                hip.position -= Vector3.up * minimum;
                for (int b = 0; b < bones.Length; b++)
                {
                    var p = bones[b].localPosition; var q = bones[b].localRotation; var s = bones[b].localScale;
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                    for (int c = 0; c < 10; c++) curves[b, c].AddKey(t, values[c]);
                }
            }
            Object.DestroyImmediate(groundMesh);
            var clip = new AnimationClip { name = name, frameRate = 30 };
            string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            for (int b = 0; b < bones.Length; b++)
                for (int c = 0; c < 10; c++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b], rig), typeof(Transform), props[c]), Reduce(curves[b, c], name == "DeathSettle" ? .00001f : c < 3 ? .0001f : .001f));
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, events);
            string path = Folder + "/Animations/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) { AssetDatabase.CreateAsset(clip, path); existing = clip; }
            else { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); }
            EditorUtility.SetDirty(existing);
            source.SampleAnimation(rig.gameObject, 0);
            for (int b = 0; b < bones.Length; b++) { bones[b].localPosition = positions[b]; bones[b].localRotation = rotations[b]; bones[b].localScale = scales[b]; }
            return existing;
        }

        // CCD keeps the original wrist contact points while the torso shifts; raised forelegs get explicit targets.
        private static void SolveLeg(Transform end, Vector3 target)
        {
            var elbow = end.parent; var shoulder = elbow.parent;
            for (int iteration = 0; iteration < 10; iteration++)
                foreach (var joint in new[] { elbow, shoulder })
                    joint.rotation = Quaternion.FromToRotation(end.position - joint.position, target - joint.position) * joint.rotation;
        }
        private static void Flare(Transform[] bones, float amount)
        {
            foreach (var bone in bones.Where(t => t.name.StartsWith("Chitin Scale")))
                bone.localRotation *= Quaternion.Euler(0, 0, amount * 35);
        }
        // Preserve sampled motion while removing redundant keys on the hundreds of unmoving rig bones.
        private static AnimationCurve Reduce(AnimationCurve source, float tolerance)
        {
            var keys = source.keys;
            var keep = new SortedSet<int> { 0, keys.Length - 1 };
            ReduceSpan(keys, 0, keys.Length - 1, tolerance, keep);
            var result = new AnimationCurve(keep.Select(i => keys[i]).ToArray());
            for (int i = 0; i < result.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(result, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(result, i, AnimationUtility.TangentMode.Linear);
            }
            return result;
        }
        private static void ReduceSpan(Keyframe[] keys, int start, int end, float tolerance, SortedSet<int> keep)
        {
            float maximum = tolerance; int split = -1;
            for (int i = start + 1; i < end; i++)
            {
                float expected = Mathf.Lerp(keys[start].value, keys[end].value, (keys[i].time - keys[start].time) / (keys[end].time - keys[start].time));
                float error = Mathf.Abs(expected - keys[i].value);
                if (error > maximum) { maximum = error; split = i; }
            }
            if (split < 0) return;
            keep.Add(split); ReduceSpan(keys, start, split, tolerance, keep); ReduceSpan(keys, split, end, tolerance, keep);
        }
        private static void AddParameter(AnimatorController c, string name, AnimatorControllerParameterType type)
        { if (!c.parameters.Any(p => p.name == name)) c.AddParameter(name, type); }
        private static AnimatorState State(AnimatorStateMachine machine, string name)
        { return machine.states.FirstOrDefault(s => s.state.name == name).state ?? machine.AddState(name); }
        private static void AddAttack(AnimatorController c, string name, AnimationClip clip)
        {
            AddParameter(c, name, AnimatorControllerParameterType.Trigger);
            var machine = c.layers[0].stateMachine;
            var state = State(machine, name); state.motion = clip;
            if (!machine.anyStateTransitions.Any(t => t.destinationState == state))
            {
                var enter = machine.AddAnyStateTransition(state); enter.hasExitTime = false; enter.duration = .06f; enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0, name);
            }
            if (state.transitions.Length == 0)
            { var exit = state.AddTransition(State(machine, "Idle")); exit.hasExitTime = true; exit.exitTime = 1; exit.duration = .12f; }
        }
        private static void Link(AnimatorState from, AnimatorState to, string parameter, bool value)
        {
            if (from.transitions.Any(t => t.destinationState == to)) return;
            var transition = from.AddTransition(to); transition.duration = .15f; transition.hasExitTime = false;
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, parameter);
        }
    }
}
