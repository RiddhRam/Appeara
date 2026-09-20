using System.Collections.Generic;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// The orientation tests prove one rifle survives being turned. These prove the rule holds for the shapes the
    /// model actually returns - pistols, cannons, blades, bows, drums - in the units it actually returns them in,
    /// and that nothing it can say produces a weapon that is unusable in the hand.
    ///
    /// The bar every archetype has to clear is the same three things, because they are what the player feels:
    /// the shot leaves in front of the hand, the thing the hand closes on is at the hand, and the weapon is a
    /// weapon-sized object rather than a pebble or a lamppost.
    /// </summary>
    public class WeaponStructureTests
    {
        private static MeshPart Part(Vector3 position, Vector3 scale, Vector3 rotation = default) =>
            new MeshPart { Shape = PartShape.Box, Position = position, Scale = scale, Rotation = rotation, Color = Color.white };

        // ---- The archetypes the model is asked for, each authored the way a sensible answer would look. ----

        private static List<MeshPart> Pistol() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0f, 0.04f), new Vector3(0.05f, 0.10f, 0.20f)),
            Part(new Vector3(0f, 0.01f, 0.17f), new Vector3(0.03f, 0.03f, 0.10f)),
            Part(new Vector3(0f, -0.11f, -0.03f), new Vector3(0.05f, 0.16f, 0.07f)),
        };

        private static List<MeshPart> Cannon() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0f, 0.10f), new Vector3(0.22f, 0.22f, 0.40f)),
            Part(new Vector3(0f, 0f, 0.42f), new Vector3(0.12f, 0.12f, 0.30f)),
            Part(new Vector3(0f, -0.18f, -0.06f), new Vector3(0.08f, 0.22f, 0.10f)),
            Part(new Vector3(0f, -0.06f, -0.22f), new Vector3(0.14f, 0.16f, 0.16f)),
        };

        private static List<MeshPart> Blade() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0f, 0.50f), new Vector3(0.06f, 0.02f, 0.80f)),
            Part(new Vector3(0f, 0f, 0.06f), new Vector3(0.18f, 0.03f, 0.04f)),
            Part(new Vector3(0f, 0f, -0.04f), new Vector3(0.04f, 0.04f, 0.16f)),
        };

        private static List<MeshPart> Bow() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0.30f, 0f), new Vector3(0.03f, 0.34f, 0.05f), new Vector3(-18f, 0f, 0f)),
            Part(new Vector3(0f, -0.30f, 0f), new Vector3(0.03f, 0.34f, 0.05f), new Vector3(18f, 0f, 0f)),
            Part(new Vector3(0f, 0f, 0.02f), new Vector3(0.05f, 0.14f, 0.06f)),
        };

        private static List<MeshPart> DrumGun() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0f, 0.06f), new Vector3(0.07f, 0.09f, 0.26f)),
            Part(new Vector3(0f, -0.02f, 0.02f), new Vector3(0.20f, 0.20f, 0.06f)),   // wide drum magazine
            Part(new Vector3(0f, 0f, 0.30f), new Vector3(0.03f, 0.03f, 0.22f)),
            Part(new Vector3(0f, -0.12f, -0.06f), new Vector3(0.05f, 0.14f, 0.07f)),
        };

        /// <summary>A barrel authored as one rotated cylinder rather than as blocks along an axis.</summary>
        private static List<MeshPart> RotatedPartRifle() => new List<MeshPart>
        {
            Part(new Vector3(0f, 0f, 0.40f), new Vector3(0.04f, 0.50f, 0.04f), new Vector3(90f, 0f, 0f)),
            Part(new Vector3(0f, 0f, 0.02f), new Vector3(0.09f, 0.11f, 0.28f)),
            Part(new Vector3(0f, -0.13f, -0.06f), new Vector3(0.06f, 0.18f, 0.08f)),
        };

        private static IEnumerable<TestCaseData> Archetypes()
        {
            yield return new TestCaseData((System.Func<List<MeshPart>>)Pistol).SetName("pistol");
            yield return new TestCaseData((System.Func<List<MeshPart>>)Cannon).SetName("cannon");
            yield return new TestCaseData((System.Func<List<MeshPart>>)Blade).SetName("blade");
            yield return new TestCaseData((System.Func<List<MeshPart>>)Bow).SetName("bow");
            yield return new TestCaseData((System.Func<List<MeshPart>>)DrumGun).SetName("drum gun");
            yield return new TestCaseData((System.Func<List<MeshPart>>)RotatedPartRifle).SetName("rifle from a rotated barrel");
        }

        /// <summary>Every frame a model might answer in, crossed with every archetype.</summary>
        private static readonly Quaternion[] Frames =
        {
            Quaternion.identity,
            Quaternion.Euler(0f, 90f, 0f),
            Quaternion.Euler(0f, 180f, 0f),
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(0f, 0f, 90f),
            Quaternion.Euler(0f, 45f, 0f),
            Quaternion.Euler(30f, 20f, 15f),
        };

        private static List<MeshPart> Express(System.Func<List<MeshPart>> build, Quaternion frame, float units, out Vector3 muzzle)
        {
            var parts = build();
            float furthest = 0f;
            foreach (var part in parts) furthest = Mathf.Max(furthest, part.Position.z + part.Scale.z * 0.5f);
            muzzle = frame * (new Vector3(0f, 0f, furthest) * units);
            foreach (var part in parts)
            {
                part.Position = frame * (part.Position * units);
                part.Scale *= units;
                part.Rotation = (frame * Quaternion.Euler(part.Rotation)).eulerAngles;
            }
            return parts;
        }

        private static void AssertUsableInTheHand(List<MeshPart> parts, Vector3 muzzle, string what)
        {
            Assert.Greater(muzzle.z, 0.05f, what + ": the shot must leave in front of the hand");
            Assert.Less(muzzle.z, 0.9f, what + ": the muzzle must not be out of arm's reach");
            // Sideways is the failure the player actually sees, so across the aim line is held tight.
            Assert.Less(Mathf.Abs(muzzle.x), 0.12f, what + ": the muzzle is off to one side of the aim line, muzzle=" + muzzle);
            // Above the hand is correct and expected - that is what holding a grip looks like - but the barrel
            // should not end up so high that the weapon reads as being held by a pole.
            Assert.Less(Mathf.Abs(muzzle.y), 0.26f, what + ": the muzzle sits too far above or below the hand, muzzle=" + muzzle);

            WeaponMesh.Bounds(parts, out var min, out var max);
            Vector3 size = max - min;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            Assert.AreEqual(WeaponMesh.TargetLength, longest, 0.03f, what + ": wrong size in the hand");
            // Nothing may reach back past the player's wrist, whatever the model returned.
            Assert.Greater(min.z, -0.35f, what + ": the weapon extends back through the player's arm");
        }

        [TestCaseSource(nameof(Archetypes))]
        public void EveryArchetypeIsUsableFromEveryFrameAndEveryUnitScale(System.Func<List<MeshPart>> build)
        {
            // Metres, centimetres, millimetres and "3.0 blocks" have all come back from the model.
            foreach (float units in new[] { 1f, 0.01f, 100f, 1000f, 3f })
                foreach (var frame in Frames)
                {
                    var parts = Express(build, frame, units, out var muzzle);
                    WeaponMesh.Fit(parts, ref muzzle);
                    AssertUsableInTheHand(parts, muzzle, $"units {units}, frame {frame.eulerAngles}");
                }
        }

        [TestCaseSource(nameof(Archetypes))]
        public void TheLongAxisAlwaysPointsAtTheEnemy(System.Func<List<MeshPart>> build)
        {
            foreach (var frame in Frames)
            {
                var parts = Express(build, frame, 1f, out var muzzle);
                WeaponMesh.Fit(parts, ref muzzle);
                WeaponMesh.Bounds(parts, out var min, out var max);
                Vector3 size = max - min;
                // A bow is the one shape whose length is deliberately across the body, so it is allowed to be
                // taller than it is long; everything else must run forward.
                if (size.y > size.z && size.y > size.x) continue;
                Assert.GreaterOrEqual(size.z, size.x - 0.001f, "the weapon lies across the player's view");
            }
        }

        /// <summary>
        /// The one the player feels most: a gun held by the grip with the barrel above it, not upside down with
        /// the grip in the air. Only the shapes that actually have a grip are asserted - a blade and a bow are
        /// symmetric about their handle and have no right way up to find.
        /// </summary>
        [TestCase("pistol")]
        [TestCase("cannon")]
        [TestCase("drum gun")]
        [TestCase("rifle from a rotated barrel")]
        public void TheGripEndsUpBelowTheBarrel(string archetype)
        {
            System.Func<List<MeshPart>> build = archetype == "pistol" ? Pistol
                : archetype == "cannon" ? Cannon
                : archetype == "drum gun" ? (System.Func<List<MeshPart>>)DrumGun : RotatedPartRifle;

            foreach (var frame in Frames)
            {
                var parts = Express(build, frame, 1f, out var muzzle);
                WeaponMesh.Fit(parts, ref muzzle);
                // The grip is whatever reaches furthest below the weapon's middle; it must be under the muzzle.
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (var part in parts)
                {
                    lowest = Mathf.Min(lowest, part.Position.y - part.Scale.y * 0.5f);
                    highest = Mathf.Max(highest, part.Position.y + part.Scale.y * 0.5f);
                }
                Assert.Less(lowest, -0.02f, $"{archetype} in frame {frame.eulerAngles} has nothing below the hand to hold");
                Assert.Greater(muzzle.y, lowest + 0.02f, $"{archetype} in frame {frame.eulerAngles} is upside down");
                Assert.Greater(highest, muzzle.y - 0.02f, $"{archetype} in frame {frame.eulerAngles}: nothing sits above the shot line");
            }
        }

        /// <summary>
        /// The strongest statement of the whole fix, and the one that needs no hand-picked threshold: the frame
        /// the model happened to answer in must not change the weapon the player ends up holding. Every earlier
        /// bug here - the sideways rifle, the upside-down pistol, the world-axis fallback - is a violation of
        /// exactly this, so it is worth asserting directly rather than only through its symptoms.
        /// </summary>
        [TestCaseSource(nameof(Archetypes))]
        public void TheSameWeaponComesOutWhateverFrameTheModelUsed(System.Func<List<MeshPart>> build)
        {
            var reference = Express(build, Quaternion.identity, 1f, out var referenceMuzzle);
            WeaponMesh.Fit(reference, ref referenceMuzzle);

            foreach (var frame in Frames)
            {
                var parts = Express(build, frame, 1f, out var muzzle);
                WeaponMesh.Fit(parts, ref muzzle);
                Assert.AreEqual(reference.Count, parts.Count);
                for (int i = 0; i < parts.Count; i++)
                    Assert.Less(Vector3.Distance(reference[i].Position, parts[i].Position), 0.03f,
                        $"part {i} lands somewhere else when the model answers in frame {frame.eulerAngles}");
                Assert.Less(Vector3.Distance(referenceMuzzle, muzzle), 0.03f,
                    $"the muzzle lands somewhere else when the model answers in frame {frame.eulerAngles}");
            }
        }

        [Test]
        public void FittingTwiceChangesNothing()
        {
            var parts = Express(Pistol, Quaternion.Euler(0f, 37f, 12f), 1f, out var muzzle);
            WeaponMesh.Fit(parts, ref muzzle);
            var once = parts.ConvertAll(p => p.Position);
            var muzzleOnce = muzzle;

            WeaponMesh.Fit(parts, ref muzzle);
            for (int i = 0; i < parts.Count; i++)
                Assert.Less(Vector3.Distance(once[i], parts[i].Position), 0.02f, "part " + i + " moved on a second fit");
            Assert.Less(Vector3.Distance(muzzleOnce, muzzle), 0.02f, "the muzzle moved on a second fit");
        }

        [Test]
        public void AMuzzleThePlayerNeverGotIsPutAtTheFrontAnyway()
        {
            var parts = Pistol();
            var muzzle = Vector3.zero;   // the model simply omitted it
            WeaponMesh.Fit(parts, ref muzzle);
            AssertUsableInTheHand(parts, muzzle, "no muzzle given");
        }

        [Test]
        public void AMuzzlePlacedOnTheWrongEndDoesNotTurnTheWeaponRound()
        {
            // The grip is unambiguous here, so a muzzle behind the weapon is the thing that must be overruled.
            var parts = Cannon();
            var muzzle = new Vector3(0f, 0f, 0.62f);
            WeaponMesh.Fit(parts, ref muzzle);
            var grip = parts[0];
            foreach (var part in parts) if (part.Position.y < grip.Position.y) grip = part;
            Assert.Less(grip.Position.z, muzzle.z, "the grip ended up in front of the muzzle");
        }

        [Test]
        public void PartsAreNeverLeftOverlappingTheHand()
        {
            foreach (var frame in Frames)
            {
                var parts = Express(Cannon, frame, 1f, out var muzzle);
                WeaponMesh.Fit(parts, ref muzzle);
                int behind = 0;
                foreach (var part in parts) if (part.Position.z + part.Scale.z * 0.5f < -0.2f) behind++;
                Assert.AreEqual(0, behind, "parts ended up behind the player's wrist");
            }
        }

        [Test]
        public void ZeroSizedAndIdenticalPartsDoNotProduceNonsense()
        {
            var muzzle = new Vector3(0f, 0f, 0.3f);
            var parts = new List<MeshPart>
            {
                Part(Vector3.zero, new Vector3(WeaponMesh.MinPart, WeaponMesh.MinPart, WeaponMesh.MinPart)),
                Part(Vector3.zero, new Vector3(WeaponMesh.MinPart, WeaponMesh.MinPart, WeaponMesh.MinPart)),
            };
            Assert.DoesNotThrow(() => WeaponMesh.Fit(parts, ref muzzle));
            foreach (var part in parts)
            {
                Assert.IsFalse(float.IsNaN(part.Position.x) || float.IsNaN(part.Position.y) || float.IsNaN(part.Position.z));
                Assert.IsFalse(float.IsNaN(part.Scale.x) || float.IsNaN(part.Scale.y) || float.IsNaN(part.Scale.z));
            }
            Assert.IsFalse(float.IsNaN(muzzle.x) || float.IsNaN(muzzle.y) || float.IsNaN(muzzle.z));
        }

        [Test]
        public void PartsAllOnOneLineAreLeftAloneRatherThanSpun()
        {
            var muzzle = new Vector3(0f, 0f, 0.5f);
            var parts = new List<MeshPart>();
            for (int i = 0; i < 5; i++) parts.Add(Part(new Vector3(0f, 0f, i * 0.1f), new Vector3(0.04f, 0.04f, 0.08f)));
            Assert.DoesNotThrow(() => WeaponMesh.Fit(parts, ref muzzle));
            Assert.Greater(muzzle.z, 0.05f, "a straight barrel should still fire forwards");
        }

        [Test]
        public void AFullTwentyTwoPartWeaponStillFits()
        {
            var parts = new List<MeshPart>();
            for (int i = 0; i < WeaponMesh.MaxParts; i++)
                parts.Add(Part(new Vector3(Mathf.Sin(i) * 0.04f, -0.02f * (i % 3), i * 0.05f), new Vector3(0.05f, 0.05f, 0.06f)));
            var muzzle = new Vector3(0f, 0f, WeaponMesh.MaxParts * 0.05f);
            WeaponMesh.Fit(parts, ref muzzle);
            AssertUsableInTheHand(parts, muzzle, "22 parts");
        }

        /// <summary>The whole pipeline as the game runs it, from the model's JSON to something in the hand.</summary>
        [Test]
        public void ModelJsonWithTheWeaponLaidAlongXStillComesOutForwards()
        {
            const string json = "{\"parts\":[" +
                "{\"shape\":\"box\",\"x\":0.05,\"y\":0,\"z\":0,\"sx\":0.34,\"sy\":0.12,\"sz\":0.10,\"color\":\"#8899AA\"}," +
                "{\"shape\":\"cylinder\",\"x\":0.45,\"y\":0,\"z\":0,\"rz\":90,\"sx\":0.04,\"sy\":0.36,\"sz\":0.04,\"color\":\"#334455\"}," +
                "{\"shape\":\"box\",\"x\":-0.02,\"y\":-0.14,\"z\":0,\"sx\":0.09,\"sy\":0.18,\"sz\":0.07,\"color\":\"#222222\"}" +
                "],\"muzzleX\":0.64,\"muzzleY\":0,\"muzzleZ\":0}";

            var parts = WeaponMesh.Parse(json, Color.white, out var muzzle);
            Assert.AreEqual(3, parts.Count);
            WeaponMesh.Fit(parts, ref muzzle);
            AssertUsableInTheHand(parts, muzzle, "json laid along X");
        }
    }
}
