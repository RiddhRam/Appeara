using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Feel is mostly a headset judgement, but it has invariants worth holding: a flourish must never move the
    /// thing the player is aiming at, it must always return the model exactly where it found it, and it must not
    /// do anything at all to a kind that has no model.
    /// </summary>
    public class EnemyPresenceTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        /// <summary>A root with a child "model", the shape the binder leaves behind.</summary>
        private Transform Model(out Transform root)
        {
            var rootObject = new GameObject("Enemy");
            spawned.Add(rootObject);
            root = rootObject.transform;
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);
            model.localPosition = new Vector3(0f, 0.4f, 0f);
            model.localScale = Vector3.one * 1.3f;
            return model;
        }

        private static void Tick(EnemyPresence presence, int frames)
        {
            var method = typeof(EnemyPresence).GetMethod("LateUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < frames; i++) method.Invoke(presence, null);
        }

        [Test]
        public void AHitMovesTheModelAndNeverTheRoot()
        {
            var model = Model(out var root);
            Vector3 rootBefore = root.position;
            var presence = EnemyPresence.Attach(model, 0.6f, idleMotion: false);

            presence.Hit(Vector3.forward, 1f);
            Tick(presence, 1);

            Assert.AreNotEqual(new Vector3(0f, 0.4f, 0f), model.localPosition, "the hit did not move the model");
            // The root carries the collider, Radius and health bar; moving it would make aiming feel unreliable.
            Assert.AreEqual(rootBefore, root.position, "a hit reaction moved the hitbox");
        }

        [Test]
        public void TheModelReturnsExactlyWhereItStarted()
        {
            var model = Model(out _);
            Vector3 restPosition = model.localPosition;
            Vector3 restScale = model.localScale;
            var presence = EnemyPresence.Attach(model, 0.6f, idleMotion: false);

            presence.Hit(Vector3.right, 1.5f);
            // Well past the recovery window, and past the spawn-in.
            typeof(EnemyPresence).GetField("knockAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(presence, Time.time - EnemyPresence.RecoverSeconds - 1f);
            typeof(EnemyPresence).GetField("spawnAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(presence, Time.time - EnemyPresence.SpawnSeconds - 1f);
            Tick(presence, 1);

            Assert.That(Vector3.Distance(restPosition, model.localPosition), Is.LessThan(0.0001f), "the model drifted");
            Assert.That(Vector3.Distance(restScale, model.localScale), Is.LessThan(0.0001f), "the model did not return to size");
        }

        [Test]
        public void SpawningStartsSmallAndReachesFullSize()
        {
            var model = Model(out _);
            Vector3 restScale = model.localScale;
            var presence = EnemyPresence.Attach(model, 0.6f, idleMotion: false);

            Tick(presence, 1);
            Assert.Less(model.localScale.x, restScale.x, "the model did not scale in on spawn");

            typeof(EnemyPresence).GetField("spawnAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(presence, Time.time - EnemyPresence.SpawnSeconds - 1f);
            Tick(presence, 1);
            Assert.That(Vector3.Distance(restScale, model.localScale), Is.LessThan(0.0001f), "the model never reached full size");
        }

        [Test]
        public void ARecycledBodyPopsInAgain()
        {
            var model = Model(out _);
            Vector3 restScale = model.localScale;
            var presence = EnemyPresence.Attach(model, 0.6f, idleMotion: false);
            typeof(EnemyPresence).GetField("spawnAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(presence, Time.time - EnemyPresence.SpawnSeconds - 1f);
            Tick(presence, 1);
            Assert.That(Vector3.Distance(restScale, model.localScale), Is.LessThan(0.0001f));

            presence.Respawned();
            Tick(presence, 1);
            Assert.Less(model.localScale.x, restScale.x, "a body out of the pool skipped its spawn-in");
        }

        [Test]
        public void IdleMotionOnlyMovesAKindThatAsksForIt()
        {
            var still = Model(out _);
            var stillPresence = EnemyPresence.Attach(still, 0.6f, idleMotion: false);
            typeof(EnemyPresence).GetField("spawnAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(stillPresence, Time.time - EnemyPresence.SpawnSeconds - 1f);
            Tick(stillPresence, 5);
            Assert.AreEqual(0.4f, still.localPosition.y, 0.0001f, "a walking kind should not also bob");

            var hovering = Model(out _);
            var hoverPresence = EnemyPresence.Attach(hovering, 0.6f, idleMotion: true);
            typeof(EnemyPresence).GetField("spawnAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(hoverPresence, Time.time - EnemyPresence.SpawnSeconds - 1f);
            // Driven by Time.deltaTime, which is zero in edit mode, so the phase is advanced directly.
            typeof(EnemyPresence).GetField("bobPhase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(hoverPresence, Mathf.PI * 0.5f);
            Tick(hoverPresence, 1);
            Assert.AreNotEqual(0.4f, hovering.localPosition.y, "a hovering kind should never be a statue");
        }

        [Test]
        public void SeverityScalesTheShoveSoASwarmerStaggersAndABruteDoesNot()
        {
            var light = Model(out _);
            var lightPresence = EnemyPresence.Attach(light, 0.6f, idleMotion: false);
            lightPresence.Hit(Vector3.forward, 1.6f);
            Tick(lightPresence, 1);
            float far = Mathf.Abs(light.localPosition.z);

            var heavy = Model(out _);
            var heavyPresence = EnemyPresence.Attach(heavy, 0.6f, idleMotion: false);
            heavyPresence.Hit(Vector3.forward, 0.25f);
            Tick(heavyPresence, 1);
            float near = Mathf.Abs(heavy.localPosition.z);

            Assert.Greater(far, near, "a heavy hit should shove harder than a glancing one");
        }

        [Test]
        public void AttachingToNothingIsNotACrash()
        {
            Assert.IsNull(EnemyPresence.Attach(null, 1f, true));
        }
    }
}
