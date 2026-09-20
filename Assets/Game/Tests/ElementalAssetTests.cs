using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Armory.Tests
{
    public class ElementalAssetTests
    {
        private static readonly string[] Elements = { "Plasma", "Cryo", "Electric", "Explosive", "Kinetic" };

        [Test]
        public void StatusLoopsAreCheapAndRemainOwnedByTheirEnemy()
        {
            foreach (var name in new[] { "Burning", "Chilled", "Stunned" })
            {
                var prefab = Load("Status/" + name);
                var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(systems.Length, Is.GreaterThan(0), name);
                Assert.That(systems.Sum(p => p.main.maxParticles), Is.LessThanOrEqualTo(48), name);
                foreach (var particles in systems)
                {
                    Assert.IsTrue(particles.main.loop, name);
                    Assert.AreEqual(ParticleSystemStopAction.None, particles.main.stopAction, name);
                }
            }
        }

        [Test]
        public void ImpactsFlashesAndDeathsHaveBoundedLifetimesAndSelfClean()
        {
            foreach (var element in Elements)
            {
                CheckOneShot("Effects/" + element + "Impact", 0.5f);
                CheckOneShot("Effects/MuzzleFlash_" + element, 0.2f);
            }
            foreach (var kind in new[] { "Grunt", "Swarm", "Armored", "Fast", "Shielded" })
                CheckOneShot("Effects/Death_" + kind, 0.7f);
            Assert.That(Load("Effects/Death_Swarm").GetComponentsInChildren<ParticleSystem>()
                .Sum(p => p.main.maxParticles), Is.LessThanOrEqualTo(16));
        }

        [Test]
        public void TrailsAndBossVisualsExposeRendererAssets()
        {
            foreach (var element in Elements)
            {
                var trail = Load("Trails/" + element + "Trail").GetComponent<TrailRenderer>();
                Assert.IsNotNull(trail, element);
                Assert.That(trail.time, Is.InRange(0.05f, 0.5f));
                Assert.That(trail.sharedMaterial.GetTexture("_BaseMap").wrapModeU, Is.EqualTo(TextureWrapMode.Repeat), "Tiled long trails must repeat rather than sample a transparent clamped edge");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Game/Art/Trails/" + element + "Trail.mat"));
            }
            foreach (var name in new[] { "BossShockwave", "BossFireball", "BossLaserCharge", "BossLaserBeam" })
                Load("Effects/" + name);
        }

        private static void CheckOneShot(string path, float maxLifetime)
        {
            var prefab = Load(path);
            Assert.AreEqual(ParticleSystemStopAction.Destroy, prefab.GetComponent<ParticleSystem>().main.stopAction, path);
            foreach (var particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.IsFalse(particles.main.loop, path);
                Assert.That(particles.main.startLifetime.constantMax, Is.LessThanOrEqualTo(maxLifetime), path);
                Assert.IsFalse(particles.collision.enabled, path);
                Assert.IsFalse(particles.lights.enabled, path);
            }
        }

        private static GameObject Load(string relativePath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/" + relativePath + ".prefab");
            Assert.IsNotNull(prefab, "Build elemental art first: " + relativePath);
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), relativePath);
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, relativePath);
                    Assert.IsNotNull(material.shader, relativePath);
                    Assert.IsTrue(material.shader.name.StartsWith("Universal Render Pipeline/"), relativePath);
                    Assert.That(material.renderQueue, Is.GreaterThanOrEqualTo(3000), relativePath);
                }
            Assert.IsEmpty(prefab.GetComponentsInChildren<Light>(true), relativePath);
            return prefab;
        }
    }
}
