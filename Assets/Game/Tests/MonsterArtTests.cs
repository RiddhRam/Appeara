using System.Linq;
using Armory.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Armory.Tests
{
    public class MonsterArtTests
    {
        [TestCase(EnemyKind.Grunt, 2.1f)]
        [TestCase(EnemyKind.Swarm, .9f)]
        [TestCase(EnemyKind.Armored, 3.2f)]
        [TestCase(EnemyKind.Fast, 1.6f)]
        [TestCase(EnemyKind.Shielded, 2.4f)]
        public void MonsterVisualIsTexturedFittedArtWithNoGameplay(EnemyKind kind, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Enemies/Authored/" + kind + "Visual.prefab");
            Assert.That(prefab, Is.Not.Null, "Run Armory/Art/Build Monster Visuals");
            var instance = Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Camera>(true), Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Light>(true), Is.Empty);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null);
                        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                        Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
                    }
                }
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.025f));
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(.025f));
                foreach (var animator in instance.GetComponentsInChildren<Animator>()) Assert.That(animator.applyRootMotion, Is.False);
                if (kind == EnemyKind.Grunt || kind == EnemyKind.Swarm)
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    Assert.That(animator, Is.Not.Null);
                    Assert.That(animator.avatar, Is.Not.Null);
                    Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True, "Idle2 retargeting needs a valid humanoid avatar");
                    Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
                }
                var table = AssetDatabase.LoadAssetAtPath<EnemyVisuals>("Assets/Game/Enemies/Resources/EnemyVisuals.asset");
                Assert.That(table.For(kind).Prefab, Is.EqualTo(prefab));
                Assert.That(table.For(kind).Height, Is.EqualTo(height));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase("Armored")]
        [TestCase("Fast")]
        [TestCase("Shielded")]
        public void HoverAnimationLoopsWithoutMovingGameplayRoot(string kind)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/Enemies/Authored/" + kind + "Hover.anim");
            Assert.That(clip, Is.Not.Null);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.True);
            Assert.That(AnimationUtility.GetCurveBindings(clip).All(b => b.path == "Motion"), Is.True);
            Assert.That(clip.length, Is.EqualTo(2.5f).Within(.01f));
        }

        [Test]
        public void WendyPosePreservesSourceTopologyAndTextureCoordinates()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Enemies/Wendy/wendy_Scene.obj").GetComponentInChildren<MeshFilter>().sharedMesh;
            var posed = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Game/Art/Enemies/Authored/ShieldedPosed_0.asset");
            Assert.That(posed, Is.Not.Null);
            Assert.That(posed, Is.Not.SameAs(source));
            Assert.That(posed.vertexCount, Is.EqualTo(source.vertexCount));
            Assert.That(posed.triangles, Is.EqualTo(source.triangles));
            Assert.That(posed.uv, Is.EqualTo(source.uv));
            Assert.That(posed.bounds.size.x, Is.LessThan(source.bounds.size.x * .95f), "Lowered arms must narrow the original T-pose silhouette");
            Assert.That(posed.bounds.min.y, Is.EqualTo(source.bounds.min.y).Within(.0001f), "Foot vertices must remain unchanged");
        }
    }
}
