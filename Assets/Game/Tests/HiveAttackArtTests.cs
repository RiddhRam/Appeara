using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Armory.Tests
{
    public class HiveAttackArtTests
    {
        private const string Root = "Assets/Game/Art/HiveAvatar/";
        [TestCase("Stomp", 2.3f, "OnStompImpact", 1.5f)]
        [TestCase("Fireball", 1.75f, "OnFireballRelease", 1.25f)]
        [TestCase("Laser", 4.5f, "OnLaserStart", 1.5f)]
        public void AttackHasTimedEventAndRecovery(string name, float length, string callback, float at)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/" + name + ".anim");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.EqualTo(length).Within(.002f));
            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.That(events.Single(e => e.functionName == callback).time, Is.EqualTo(at).Within(.002f));
            Assert.That(events.Single(e => e.functionName == "OnAttackRecovered").time, Is.InRange(length - .03f, length));
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False);
            Assert.That(AnimationUtility.GetCurveBindings(clip).Any(b => b.path == ""), Is.False, "No actor-root movement curves");
        }
        [Test]
        public void LaserEndAndReusableLoopArePresent()
        {
            var full = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/Laser.anim");
            Assert.That(AnimationUtility.GetAnimationEvents(full).Single(e => e.functionName == "OnLaserEnd").time, Is.EqualTo(3.5f));
            var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/LaserSweepLoop.anim");
            Assert.That(AnimationUtility.GetAnimationClipSettings(loop).loopTime, Is.True);
            foreach (var binding in AnimationUtility.GetCurveBindings(loop))
            {
                var curve = AnimationUtility.GetEditorCurve(loop, binding);
                Assert.That(curve.Evaluate(0), Is.EqualTo(curve.Evaluate(loop.length)).Within(.001f), binding.path);
            }
        }
        [Test]
        public void BossHasSocketControllerAndSettledDeath()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "HiveAvatarVisual.prefab");
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Mouth_Socket"), Is.EqualTo(1));
            var animator = prefab.GetComponentInChildren<Animator>(); Assert.That(animator.applyRootMotion, Is.False);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            foreach (var name in new[] { "Moving", "Claw", "Bite", "Die", "Stomp", "Fireball", "Laser", "Enraged" })
                Assert.That(controller.parameters.Any(p => p.name == name), Is.True, name);
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/DeathSettle.anim").length, Is.EqualTo(4.5f).Within(.002f));
        }
        [TestCase("Stomp", 1.2f)]
        [TestCase("Stomp", 1.5f)]
        [TestCase("Laser", 2.5f)]
        [TestCase("DeathSettle", 4.49f)]
        public void SampledSkinStaysOnDeck(string name, float time)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "HiveAvatarVisual.prefab"));
            var mesh = new Mesh();
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/" + name + ".anim");
                clip.SampleAnimation(animator.gameObject, time); skin.BakeMesh(mesh, true);
                Assert.That(mesh.vertices.Min(v => skin.transform.TransformPoint(v).y), Is.EqualTo(0).Within(.025f));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(instance); }
        }
        [Test]
        public void TelegraphsMoveSkinBonesAndDeathDoesNotStandBackUp()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "HiveAvatarVisual.prefab"));
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                var bones = instance.GetComponentsInChildren<Transform>();
                var wrist = bones.Single(t => t.name == "Bone.005_L.004");
                var head = bones.Single(t => t.name == "Bone.008");
                var stomp = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/Stomp.anim");
                stomp.SampleAnimation(animator.gameObject, 0); float wristStart = wrist.position.y;
                stomp.SampleAnimation(animator.gameObject, 1.2f);
                Assert.That(wrist.position.y - wristStart, Is.GreaterThan(2.5f));
                var laser = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/Laser.anim");
                laser.SampleAnimation(animator.gameObject, 1.5f); var left = head.rotation;
                laser.SampleAnimation(animator.gameObject, 3.5f);
                Assert.That(Quaternion.Angle(left, head.rotation), Is.InRange(118f, 122f));
                var death = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animations/DeathSettle.anim");
                death.SampleAnimation(animator.gameObject, 0); float standing = head.position.y;
                death.SampleAnimation(animator.gameObject, 4.49f);
                Assert.That(standing - head.position.y, Is.GreaterThan(1f));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
