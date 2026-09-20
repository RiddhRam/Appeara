using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Armory.Tests
{
    public class MixamoEnemyAnimationTests
    {
        private static Type SetupType => Type.GetType("Armory.Editor.MixamoEnemyAnimationSetup, Armory.Editor", true);
        private const string AnimationFolder = "Assets/Game/Art/Enemies/AlienMonster/Animations/";

        [TestCase("Zombie Attack.fbx", "Attack")]
        [TestCase("Mutant Swiping.fbx", "Attack")]
        [TestCase("Zombie Death.fbx", "Die")]
        [TestCase("Mutant Dying.fbx", "Die")]
        [TestCase("Standing React Small From Front.fbx", "Hit")]
        [TestCase("Zombie Running.fbx", "Moving")]
        [TestCase("Idle2.fbx", null)]
        [TestCase("made-up-zombie-attack-ish.fbx", null)]
        public void DownloadNameMapsOnlyDocumentedMixamoClips(string fileName, string expected)
        {
            var method = SetupType.GetMethod("RoleForFileName", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { fileName }), Is.EqualTo(expected));
        }

        [TestCase("Zombie Attack.fbx", false)]
        [TestCase("Zombie Death.fbx", false)]
        [TestCase("Standing React Small From Front.fbx", false)]
        [TestCase("Zombie Running.fbx", true)]
        public void DownloadImportsAsUncompressedHumanoidWithExpectedLoopPolicy(string fileName, bool loops)
        {
            string path = AnimationFolder + fileName;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "Missing requested Mixamo download: " + path);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            Assert.That(importer.animationCompression, Is.EqualTo(ModelImporterAnimationCompression.Off));

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(model, Is.Not.Null);
            var avatar = model.GetComponentInChildren<Animator>().avatar;
            Assert.That(avatar, Is.Not.Null);
            Assert.That(avatar.isValid && avatar.isHuman, Is.True);

            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.That(clips, Has.Length.EqualTo(1));
            Assert.That(clips[0].humanMotion, Is.True);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clips[0]).loopTime, Is.EqualTo(loops));
        }

        [TestCase("GruntRig")]
        [TestCase("SwarmRig")]
        public void RigControllerKeepsMovingAndAddsOnlyStatesBackedByRealClips(string controllerName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Game/Art/Enemies/Authored/" + controllerName + ".controller");
            Assert.That(controller, Is.Not.Null, "Run Armory/Art/Build Monster Visuals");

            Assert.That(controller.parameters.Single(parameter => parameter.name == "Moving").type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
            foreach (var parameterName in new[] { "Attack", "Hit", "Die" })
                Assert.That(controller.parameters.Single(parameter => parameter.name == parameterName).type,
                    Is.EqualTo(AnimatorControllerParameterType.Trigger), parameterName + " must survive a rebuild as a trigger");

            var machine = controller.layers[0].stateMachine;
            var states = machine.states.Select(child => child.state).ToArray();
            var expectedMotions = new[]
            {
                new[] { "Moving", "Zombie Running" },
                new[] { "Attack", "Zombie Attack" },
                new[] { "Hit", "Standing React Small From Front" },
                new[] { "Die", "Zombie Death" },
            };
            foreach (var expected in expectedMotions)
            {
                var state = states.FirstOrDefault(candidate => candidate.name == expected[0]);
                Assert.That(state, Is.Not.Null, expected[0] + " needs its downloaded Mixamo state");
                Assert.That(state.motion, Is.Not.Null, expected[0] + " must be backed by a downloaded Mixamo clip");
                Assert.That(state.motion.name, Is.EqualTo(expected[1]));
                if (expected[0] != "Moving")
                {
                    var transition = machine.anyStateTransitions.Single(candidate => candidate.destinationState == state &&
                        candidate.conditions.Any(condition => condition.parameter == expected[0]));
                    Assert.That(transition.canTransitionToSelf, Is.EqualTo(expected[0] == "Attack"));
                }
            }
        }
    }
}
