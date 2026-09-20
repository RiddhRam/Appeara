using System.Collections.Generic;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Generated weapons kept arriving in the hand sideways. The model is asked for barrel-along-+Z and answers
    /// in whatever frame it likes, so the fix reads the axes off the geometry instead. These tests take one
    /// known-good rifle, turn it into every orientation a model could plausibly return, and assert it always
    /// comes back the same way round: barrel forward, grip under the hand.
    /// </summary>
    public class WeaponOrientationTests
    {
        /// <summary>A rifle in the intended frame: long thin barrel forward, fat receiver, grip hanging below.</summary>
        private static List<MeshPart> Rifle(out Vector3 muzzle)
        {
            muzzle = new Vector3(0f, 0f, 0.62f);
            return new List<MeshPart>
            {
                Part(new Vector3(0f, 0f, 0.05f), new Vector3(0.10f, 0.12f, 0.34f)),  // receiver
                Part(new Vector3(0f, 0f, 0.45f), new Vector3(0.04f, 0.04f, 0.36f)),  // barrel, thin and forward
                Part(new Vector3(0f, -0.14f, -0.02f), new Vector3(0.07f, 0.18f, 0.09f)), // grip, hanging below
                Part(new Vector3(0f, -0.02f, -0.20f), new Vector3(0.08f, 0.10f, 0.14f)), // stock, at the back
            };
        }

        private static MeshPart Part(Vector3 position, Vector3 scale) =>
            new MeshPart { Shape = PartShape.Box, Position = position, Scale = scale, Rotation = Vector3.zero, Color = Color.white };

        /// <summary>Re-expresses the rifle in another frame, exactly as a model answering in that frame would.</summary>
        private static List<MeshPart> Turned(Quaternion rotation, out Vector3 muzzle)
        {
            var parts = Rifle(out var original);
            foreach (var part in parts)
            {
                part.Position = rotation * part.Position;
                part.Rotation = (rotation * Quaternion.Euler(part.Rotation)).eulerAngles;
            }
            muzzle = rotation * original;
            return parts;
        }

        private static IEnumerable<TestCaseData> Orientations()
        {
            yield return new TestCaseData(Quaternion.identity).SetName("already correct");
            yield return new TestCaseData(Quaternion.Euler(0f, 90f, 0f)).SetName("barrel along +X");
            yield return new TestCaseData(Quaternion.Euler(0f, -90f, 0f)).SetName("barrel along -X");
            yield return new TestCaseData(Quaternion.Euler(0f, 180f, 0f)).SetName("barrel backwards");
            yield return new TestCaseData(Quaternion.Euler(90f, 0f, 0f)).SetName("stood on end, barrel up");
            yield return new TestCaseData(Quaternion.Euler(-90f, 0f, 0f)).SetName("stood on end, barrel down");
            yield return new TestCaseData(Quaternion.Euler(0f, 0f, 90f)).SetName("rolled onto its side");
            yield return new TestCaseData(Quaternion.Euler(0f, 0f, 180f)).SetName("upside down");
            yield return new TestCaseData(Quaternion.Euler(0f, 45f, 0f)).SetName("yawed off axis");
        }

        [TestCaseSource(nameof(Orientations))]
        public void BarrelEndsUpForwardWhateverFrameTheModelAnswersIn(Quaternion rotation)
        {
            var parts = Turned(rotation, out var muzzle);
            WeaponMesh.Fit(parts, ref muzzle);

            Assert.Greater(muzzle.z, 0.1f, "the shot must leave ahead of the hand");
            Assert.Less(Mathf.Abs(muzzle.x), 0.12f, "the muzzle must be roughly on the aim line, not off to one side");
            Assert.Less(Mathf.Abs(muzzle.y), 0.12f, "the muzzle must be roughly on the aim line, not above or below it");
        }

        [TestCaseSource(nameof(Orientations))]
        public void GripEndsUpUnderTheHandWhateverFrameTheModelAnswersIn(Quaternion rotation)
        {
            var parts = Turned(rotation, out var muzzle);
            WeaponMesh.Fit(parts, ref muzzle);

            // The grip is the part the hand closes on, so it is the one that must sit at the origin.
            var grip = Lowest(parts);
            Assert.Less(Mathf.Abs(grip.Position.y), 0.08f, "the grip should be at hand height, not hanging off it");
            Assert.Less(grip.Position.z, 0.2f, "the grip belongs at the back of the weapon, near the hand");
        }

        [TestCaseSource(nameof(Orientations))]
        public void TheWeaponStaysAWeaponSizedObject(Quaternion rotation)
        {
            var parts = Turned(rotation, out var muzzle);
            WeaponMesh.Fit(parts, ref muzzle);

            WeaponMesh.Bounds(parts, out var min, out var max);
            Vector3 size = max - min;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            Assert.AreEqual(WeaponMesh.TargetLength, longest, 0.02f);
            // And the long axis must be the one pointing at the enemy, not across the player's view.
            Assert.Greater(size.z, size.x, "the weapon's length should run forward");
            Assert.Greater(size.z, size.y, "the weapon's length should run forward, not vertically");
        }

        /// <summary>Whatever ended up lowest is what the hand is holding.</summary>
        private static MeshPart Lowest(List<MeshPart> parts)
        {
            MeshPart best = parts[0];
            foreach (var part in parts) if (part.Position.y < best.Position.y) best = part;
            return best;
        }

        [Test]
        public void ScaleSurvivesTheTurnSoABarrelDoesNotBecomeASlab()
        {
            var parts = Turned(Quaternion.Euler(0f, 90f, 0f), out var muzzle);
            WeaponMesh.Fit(parts, ref muzzle);
            // The barrel is the long thin one; after orienting it must still be long and thin, not rotated
            // into a wide flat plate, which is what happens if a part's own rotation is dropped.
            MeshPart barrel = parts[0];
            foreach (var part in parts)
                if (part.Scale.magnitude > 0f && Elongation(part) > Elongation(barrel)) barrel = part;
            Assert.Greater(Elongation(barrel), 3f, "the barrel lost its proportions in the turn");
        }

        private static float Elongation(MeshPart part)
        {
            float longest = Mathf.Max(part.Scale.x, Mathf.Max(part.Scale.y, part.Scale.z));
            float shortest = Mathf.Min(part.Scale.x, Mathf.Min(part.Scale.y, part.Scale.z));
            return shortest <= 0.0001f ? 0f : longest / shortest;
        }

        [Test]
        public void ASingleBlockIsLeftAloneRatherThanGuessedAt()
        {
            var muzzle = new Vector3(0f, 0f, 0.3f);
            var parts = new List<MeshPart> { Part(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f)) };
            Assert.DoesNotThrow(() => WeaponMesh.Fit(parts, ref muzzle));
            Assert.AreEqual(1, parts.Count);
        }

        [Test]
        public void NoPartsIsNotACrash()
        {
            var muzzle = Vector3.forward;
            var parts = new List<MeshPart>();
            Assert.DoesNotThrow(() => WeaponMesh.Fit(parts, ref muzzle));
        }
    }
}
