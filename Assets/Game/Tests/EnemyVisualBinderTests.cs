using System.Collections.Generic;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Armory.Tests
{
    /// <summary>
    /// Hanging an authored model on an enemy. The art arrives at whatever scale and pivot the source used, the
    /// table can be half-filled, and none of it may move a hitbox or cost the enemy its textures.
    /// </summary>
    public class EnemyVisualBinderTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var item in spawned)
                if (item != null) Object.DestroyImmediate(item);
            spawned.Clear();
        }

        private T Track<T>(T item) where T : Object
        {
            spawned.Add(item);
            return item;
        }

        private GameObject Root() => Track(new GameObject("Enemy"));

        /// <summary>The factory body: the shape that carries the collider the weapons actually hit.</summary>
        private GameObject Placeholder(GameObject root)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * 0.9f;
            return body;
        }

        private static EnemyVisuals.Entry Entry(GameObject prefab, float height = 2f, string moving = "") =>
            new EnemyVisuals.Entry { Kind = EnemyKind.Grunt, Prefab = prefab, Height = height, MovingParameter = moving };

        private Mesh SkinnedCube()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Track(Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh));
            Object.DestroyImmediate(source);
            var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity };
            return mesh;
        }

        [Test]
        public void ScalesAPlainMeshToTheAuthoredHeightAndStandsItOnTheDeck()
        {
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            prefab.transform.localScale = new Vector3(3f, 7f, 3f);

            var binder = EnemyVisualBinder.Bind(Root(), Entry(prefab, 2.1f));

            Assert.That(binder, Is.Not.Null);
            var bounds = binder.Renderers[0].bounds;
            Assert.That(bounds.size.y, Is.EqualTo(2.1f).Within(0.001f));
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void MeasuresASkinnedModelFromItsBakedPoseNotItsImportedBounds()
        {
            var prefab = Track(new GameObject("Skinned Model"));
            var skinned = new GameObject("Skin");
            skinned.transform.SetParent(prefab.transform, false);
            // Source FBXs land with a hundredfold renderer transform; the fit has to see through it.
            skinned.transform.localScale = Vector3.one * 100f;
            var skin = skinned.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = SkinnedCube();
            skin.bones = new[] { skinned.transform };
            skin.rootBone = skinned.transform;
            // Imported bounds of an animated model are padded to cover every frame of every clip.
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 50f);

            var binder = EnemyVisualBinder.Bind(Root(), Entry(prefab, 2.5f));

            Assert.That(binder, Is.Not.Null);
            Assert.That(binder.Model.localScale.x, Is.EqualTo(0.025f).Within(0.0005f));
            Assert.That(binder.Model.localPosition.y, Is.EqualTo(1.25f).Within(0.005f));
        }

        [Test]
        public void ARowWithNoHeightKeepsTheImportScaleButStillStandsOnTheDeck()
        {
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            prefab.transform.localScale = new Vector3(2f, 4f, 2f);

            var binder = EnemyVisualBinder.Bind(Root(), Entry(prefab, height: 0f));

            Assert.That(binder.Model.localScale.y, Is.EqualTo(4f).Within(0.001f));
            Assert.That(binder.Renderers[0].bounds.min.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void TheAuthoredEulerOffsetTurnsTheModelToFaceForward()
        {
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var entry = Entry(prefab, 2f);
            entry.EulerOffset = new Vector3(0f, 90f, 0f);

            var binder = EnemyVisualBinder.Bind(Root(), entry);

            Assert.That(binder.Model.localRotation.eulerAngles.y, Is.EqualTo(90f).Within(0.01f));
        }

        [Test]
        public void AMissingRowLeavesThePlaceholderShowing()
        {
            var root = Root();
            var body = Placeholder(root);

            Assert.That(EnemyVisualBinder.Bind(root, null), Is.Null);
            Assert.That(root.GetComponent<EnemyVisualBinder>(), Is.Null);
            Assert.That(body.GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        public void ARowWithNoPrefabLeavesThePlaceholderShowing()
        {
            var root = Root();
            var body = Placeholder(root);

            Assert.That(EnemyVisualBinder.Bind(root, Entry(null)), Is.Null);
            Assert.That(root.GetComponent<EnemyVisualBinder>(), Is.Null);
            Assert.That(body.GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        public void AModelWithNothingToDrawLeavesThePlaceholderShowing()
        {
            var root = Root();
            var body = Placeholder(root);
            var prefab = Track(new GameObject("Empty Model"));

            Assert.That(EnemyVisualBinder.Bind(root, Entry(prefab)), Is.Null);
            Assert.That(root.GetComponent<EnemyVisualBinder>(), Is.Null);
            Assert.That(body.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(root.transform.childCount, Is.EqualTo(1), "The unusable model instance must not be left behind");
        }

        [Test]
        public void TheModelReplacesThePlaceholderWithoutMovingItsCollider()
        {
            var root = Root();
            var body = Placeholder(root);
            var collider = body.GetComponent<Collider>();
            var before = collider.bounds;
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));

            EnemyVisualBinder.Bind(root, Entry(prefab, 2f));

            Assert.That(body.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.bounds, Is.EqualTo(before));
        }

        [Test]
        public void CollidersThatCameInOnTheArtPrefabAreSwitchedOff()
        {
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));

            var binder = EnemyVisualBinder.Bind(Root(), Entry(prefab, 2f));

            var colliders = binder.Model.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Is.Not.Empty);
            foreach (var collider in colliders) Assert.That(collider.enabled, Is.False);
        }

        [Test]
        public void AControllerlessModelIsNotAnimatedAndSetMovingIsHarmless()
        {
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            prefab.AddComponent<Animator>();

            var binder = EnemyVisualBinder.Bind(Root(), Entry(prefab, 2f, moving: "Moving"));

            Assert.That(binder.Animated, Is.False);
            Assert.DoesNotThrow(() => binder.SetMoving(true));
        }

        /// <summary>
        /// The one test that runs the shipped table through the real code path: the rows, the authored prefabs
        /// and the fit together, on an enemy root, so a re-export that changes a pivot or a scale is caught here
        /// rather than in a headset.
        /// </summary>
        [TestCase(EnemyKind.Grunt, 2.1f)]
        [TestCase(EnemyKind.Swarm, 0.9f)]
        [TestCase(EnemyKind.Armored, 3.2f)]
        [TestCase(EnemyKind.Fast, 1.6f)]
        [TestCase(EnemyKind.Shielded, 2.4f)]
        public void TheShippedTableFitsEveryAuthoredMonsterOntoAnEnemyRoot(EnemyKind kind, float height)
        {
            var root = Root();

            var binder = EnemyVisualBinder.Attach(root, kind);

            Assert.That(binder, Is.Not.Null, "Resources/EnemyVisuals is missing a usable row for " + kind);
            Assert.That(EnemyVisualBinder.TryMeasure(binder.Model, root.transform, out var bounds), Is.True);
            Assert.That(bounds.size.y, Is.EqualTo(height).Within(0.03f));
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.03f));
        }

        [Test]
        public void TheBossIsLeftToItsOwnAvatar()
        {
            Assert.That(EnemyVisualBinder.EntryFor(EnemyKind.Boss), Is.Null);
        }

        [Test]
        public void ReleasingTwiceTakesTheModelAwayExactlyOnce()
        {
            var root = Root();
            var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var binder = EnemyVisualBinder.Bind(root, Entry(prefab, 2f));

            binder.Release();

            Assert.That(binder.Model, Is.Null);
            Assert.That(root.transform.childCount, Is.EqualTo(0));
            Assert.DoesNotThrow(() => binder.Release());
        }
    }

    /// <summary>
    /// Recolouring an enemy. The tint has to reach a flat placeholder capsule and a textured monster alike
    /// without either of them losing the material it was built with.
    /// </summary>
    public class EnemyTintTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            // ignoreFailingMessages is deliberately not reset here: the runner clears it per test, and the log
            // check that reads it runs after this teardown.
            foreach (var item in spawned)
                if (item != null) Object.DestroyImmediate(item);
            spawned.Clear();
        }

        private T Track<T>(T item) where T : Object
        {
            spawned.Add(item);
            return item;
        }

        private static readonly Color Grunt = new Color(0.3f, 0.95f, 0.4f);

        private Enemy Alien(out Renderer placeholder, out EnemyVisualBinder visual, bool withModel)
        {
            var root = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            root.name = "Body";
            placeholder = root.GetComponent<Renderer>();
            var enemy = root.AddComponent<Enemy>();
            enemy.MaxHealth = enemy.Health = 500f;
            enemy.BaseColor = Grunt;
            visual = null;
            if (withModel)
            {
                var prefab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
                visual = EnemyVisualBinder.Bind(root, new EnemyVisuals.Entry { Kind = EnemyKind.Grunt, Prefab = prefab, Height = 2f });
                enemy.Visual = visual;
            }
            enemy.Init(EnemyKind.Grunt, Vector3.forward * 20f);
            return enemy;
        }

        [Test]
        public void TintingAPlaceholderUsesAPropertyBlockAndKeepsItsSharedMaterial()
        {
            var enemy = Alien(out var placeholder, out _, withModel: false);
            var material = placeholder.sharedMaterial;

            Assert.That(enemy.BaseColor, Is.EqualTo(Grunt));
            Assert.That(placeholder.sharedMaterial, Is.SameAs(material));
            Assert.That(placeholder.HasPropertyBlock(), Is.True);
            var block = new MaterialPropertyBlock();
            placeholder.GetPropertyBlock(block);
            var tint = block.GetColor("_BaseColor");
            Assert.That(tint.r, Is.EqualTo(Grunt.r).Within(0.001f));
            Assert.That(tint.g, Is.EqualTo(Grunt.g).Within(0.001f));
            Assert.That(tint.b, Is.EqualTo(Grunt.b).Within(0.001f));
        }

        /// <summary>
        /// The keys are indexed by the enum, so inserting a kind in the middle would give every alien after it
        /// somebody else's death burst.
        /// </summary>
        [TestCase(EnemyKind.Grunt, "Death_Grunt")]
        [TestCase(EnemyKind.Swarm, "Death_Swarm")]
        [TestCase(EnemyKind.Armored, "Death_Armored")]
        [TestCase(EnemyKind.Fast, "Death_Fast")]
        [TestCase(EnemyKind.Shielded, "Death_Shielded")]
        [TestCase(EnemyKind.Boss, null)]
        public void EachKindDiesWithItsOwnAuthoredBurst(EnemyKind kind, string key)
        {
            Assert.That(Enemy.DeathEffectKey(kind), Is.EqualTo(key));
        }

        [Test]
        public void TheAuthoredModelKeepsItsOwnColoursWhileNothingIsHappeningToIt()
        {
            Alien(out var placeholder, out var visual, withModel: true);

            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.Renderers[0].HasPropertyBlock(), Is.False);
            Assert.That(placeholder.HasPropertyBlock(), Is.True);
        }

        [Test]
        public void AHitFlashesTheModelWithoutReplacingItsMaterial()
        {
            var enemy = Alien(out _, out var visual, withModel: true);
            var art = visual.Renderers[0];
            var material = art.sharedMaterial;
            // A first hit raises the health bar, and the UI kit strips its panels' colliders with Destroy, which
            // edit mode refuses. That noise belongs to the bar, not to the tint this test is about.
            LogAssert.ignoreFailingMessages = true;

            enemy.TakeHit(new ParsedWeapon { FireMode = FireMode.Projectile, Payload = Payload.Kinetic, Damage = 5f }, 5f);

            Assert.That(art.sharedMaterial, Is.SameAs(material));
            Assert.That(art.HasPropertyBlock(), Is.True);
            var block = new MaterialPropertyBlock();
            art.GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor").g, Is.EqualTo(1f).Within(0.001f), "The hit flash is white");
        }
    }
}
