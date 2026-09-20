using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Choosing a model per enemy kind. The table is authored in the inspector, so it can be half-filled, have a
    /// row with no prefab, or list the same kind twice; none of those may hand back a model for the wrong enemy.
    /// </summary>
    public class EnemyVisualsTests
    {
        private EnemyVisuals visuals;
        private GameObject prefabA, prefabB;

        [SetUp]
        public void Setup()
        {
            visuals = ScriptableObject.CreateInstance<EnemyVisuals>();
            prefabA = new GameObject("Model A");
            prefabB = new GameObject("Model B");
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(visuals);
            Object.DestroyImmediate(prefabA);
            Object.DestroyImmediate(prefabB);
        }

        private static EnemyVisuals.Entry Entry(EnemyKind kind, GameObject prefab, float height = 2f)
        {
            return new EnemyVisuals.Entry { Kind = kind, Prefab = prefab, Height = height };
        }

        [Test]
        public void ReturnsTheEntryAuthoredForThatKind()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Swarm, prefabA), Entry(EnemyKind.Armored, prefabB) };

            Assert.AreSame(prefabB, visuals.For(EnemyKind.Armored).Prefab);
        }

        [Test]
        public void KindsWithNoRowKeepThePrimitivePlaceholder()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Swarm, prefabA) };

            Assert.IsNull(visuals.For(EnemyKind.Fast));
        }

        [Test]
        public void ARowWithNoPrefabIsNotAModel()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Fast, null) };

            Assert.IsNull(visuals.For(EnemyKind.Fast));
        }

        [Test]
        public void TheFirstUsableRowWinsWhenAKindIsListedTwice()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Grunt, null), Entry(EnemyKind.Grunt, prefabA), Entry(EnemyKind.Grunt, prefabB) };

            Assert.AreSame(prefabA, visuals.For(EnemyKind.Grunt).Prefab);
        }

        [Test]
        public void AnEmptyTableIsSafeToAsk()
        {
            visuals.Entries = null;

            Assert.IsNull(visuals.For(EnemyKind.Boss));
        }

        [Test]
        public void ANonPositiveHeightMeansDoNotRescaleTheModel()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Swarm, prefabA, height: 0f) };

            Assert.IsFalse(visuals.For(EnemyKind.Swarm).Rescales);
        }

        [Test]
        public void APositiveHeightMeansFitTheModelToIt()
        {
            visuals.Entries = new[] { Entry(EnemyKind.Swarm, prefabA, height: 1.8f) };

            Assert.IsTrue(visuals.For(EnemyKind.Swarm).Rescales);
        }
    }

    /// <summary>
    /// Normalising an imported model to a gameplay size. Downloaded models arrive at wildly different scales and
    /// pivots, so the fit has to work from measured bounds alone and always stand the model on the deck.
    /// </summary>
    public class ModelFitTests
    {
        [Test]
        public void ScalesAModelToTheRequestedHeight()
        {
            var fit = ModelFit.Solve(new Bounds(new Vector3(0f, 50f, 0f), new Vector3(20f, 100f, 20f)), 2f);

            Assert.AreEqual(0.02f, fit.Scale, 0.0001f);
        }

        [Test]
        public void StandsTheModelOnTheGroundWhateverItsPivot()
        {
            // Pivot at the model's centre: the feet are 50 units below the origin.
            var fit = ModelFit.Solve(new Bounds(Vector3.zero, new Vector3(20f, 100f, 20f)), 2f);

            Assert.AreEqual(1f, fit.Offset.y, 0.0001f);
        }

        [Test]
        public void CentresTheModelOverItsPivotHorizontally()
        {
            var fit = ModelFit.Solve(new Bounds(new Vector3(30f, 50f, -10f), new Vector3(20f, 100f, 20f)), 2f);

            Assert.AreEqual(-0.6f, fit.Offset.x, 0.0001f);
            Assert.AreEqual(0.2f, fit.Offset.z, 0.0001f);
        }

        [Test]
        public void AFlatOrEmptyMeshDoesNotProduceAnInfiniteScale()
        {
            var fit = ModelFit.Solve(new Bounds(Vector3.zero, Vector3.zero), 2f);

            Assert.AreEqual(1f, fit.Scale);
            Assert.AreEqual(Vector3.zero, fit.Offset);
        }

        [Test]
        public void ANonPositiveTargetHeightLeavesTheModelAlone()
        {
            var fit = ModelFit.Solve(new Bounds(new Vector3(0f, 50f, 0f), new Vector3(20f, 100f, 20f)), 0f);

            Assert.AreEqual(1f, fit.Scale);
            Assert.AreEqual(Vector3.zero, fit.Offset);
        }
    }
}
