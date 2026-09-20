using System.Collections.Generic;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Regression cover for the boss-targeting bug: bone-anchored organs sat inside the body collider, so shots at
    /// three of the four organs hit the body instead and those organs could never be destroyed.
    /// </summary>
    public class HiveTargetsTests
    {
        // Bone positions measured live from the rigged avatar (local space, metres).
        private static readonly Vector3[] BoneAnchors =
        {
            new Vector3(-2.43f, 3.91f, 2.72f),  // left claw
            new Vector3(2.57f, 3.97f, 2.76f),   // right claw
            new Vector3(0.47f, 10.34f, 0.19f),  // spore sac (sits on the body axis)
            new Vector3(-2.68f, 7.97f, 6.04f),  // crest
        };

        [Test]
        public void RawBoneAnchorsAreInsideTheTorso()
        {
            // The bug: two of these are buried in the body, which is why they were unhittable.
            Assert.IsFalse(HiveTargets.IsClearOfTorso(BoneAnchors[0]), "left claw bone should be inside the torso");
            Assert.IsFalse(HiveTargets.IsClearOfTorso(BoneAnchors[2]), "spore sac bone should be inside the torso");
        }

        [Test]
        public void ProtrudePushesEveryOrganClearOfTheTorso()
        {
            foreach (var anchor in BoneAnchors)
                Assert.IsTrue(HiveTargets.IsClearOfTorso(HiveTargets.Protrude(anchor)), "organ still inside torso: " + anchor);
        }

        [Test]
        public void ProtrudeKeepsOrgansOnTheirOwnSide()
        {
            var left = HiveTargets.Protrude(BoneAnchors[0]);
            var right = HiveTargets.Protrude(BoneAnchors[1]);
            Assert.Less(left.x, 0f);
            Assert.Greater(right.x, 0f);
            // Height is preserved for limb organs; only the radial distance changes.
            Assert.AreEqual(BoneAnchors[0].y, left.y, 0.001f);
        }

        [Test]
        public void AxisOrganIsLiftedAboveTheTorsoAndSetBack()
        {
            var sac = HiveTargets.Protrude(BoneAnchors[2]);
            Assert.Greater(sac.y, HiveTargets.TorsoTopY);
            Assert.AreEqual(BoneAnchors[2].x, sac.x, 0.001f);
            // Sitting back keeps the front-mounted crest out of the line of sight.
            Assert.Less(sac.z, BoneAnchors[2].z);
        }

        [Test]
        public void PlayerRayHitsEachOrganBeforeTheBody()
        {
            var spawned = new List<GameObject>();
            try
            {
                var body = new GameObject("Boss");
                spawned.Add(body);
                var torso = body.AddComponent<CapsuleCollider>();
                torso.center = new Vector3(0f, HiveTargets.TorsoCenterY, 0f);
                torso.radius = HiveTargets.TorsoRadius;
                torso.height = HiveTargets.TorsoHeight;
                torso.direction = 1;

                var organs = new List<Collider>();
                foreach (var anchor in BoneAnchors)
                {
                    var organ = new GameObject("Organ");
                    spawned.Add(organ);
                    organ.transform.position = HiveTargets.Protrude(anchor);
                    var sphere = organ.AddComponent<SphereCollider>();
                    sphere.radius = HiveTargets.OrganRadius;
                    organs.Add(sphere);
                }
                Physics.SyncTransforms();
                // Organs must not shadow each other either: the sac was blocked by the crest before the set-back.

                // Player eye height, 17 m away on the +Z side, the same geometry the live capture used.
                var eye = new Vector3(0f, 3.3f, 17f);
                for (int i = 0; i < organs.Count; i++)
                {
                    Vector3 target = organs[i].transform.position;
                    Assert.IsTrue(Physics.Raycast(eye, (target - eye).normalized, out var hit, 100f), "no hit for organ " + i);
                    Assert.AreSame(organs[i], hit.collider, $"organ {i} is occluded by '{hit.collider.name}'");
                }
            }
            finally
            {
                foreach (var go in spawned) Object.DestroyImmediate(go);
            }
        }
    }

    public class WaveStartPhraseTests
    {
        [TestCase("ready")]
        [TestCase("Ready!")]
        [TestCase("I'm ready")]
        [TestCase("start the wave")]
        [TestCase("ok bring them on")]
        [TestCase("let's go")]
        public void StartsTheWave(string spoken)
        {
            Assert.IsTrue(ShipAI.IsWaveStartPhrase(spoken), spoken);
        }

        [TestCase("give me a shotgun that fires sticky mines")]
        [TestCase("ready the plasma cannon with homing rounds and a big magazine")]
        [TestCase("make something that goes boom")]
        [TestCase("")]
        public void BuildsAWeaponInstead(string spoken)
        {
            Assert.IsFalse(ShipAI.IsWaveStartPhrase(spoken), spoken);
        }
    }

    public class StatusEffectTests
    {
        [Test]
        public void PlasmaBurnsAndSoftensArmour()
        {
            var status = new StatusState();
            status.Apply(Payload.Plasma, 30f, 0f);
            Assert.IsTrue(status.Burning(1f));
            Assert.Greater(status.BurnDamage(1f, 1f), 1f);
            Assert.Greater(status.DamageTakenMultiplier(1f), 1f);
            Assert.AreEqual(0f, status.BurnDamage(StatusState.BurnSeconds + 1f, 1f));
        }

        [Test]
        public void CryoChillsThenLeavesThemBrittle()
        {
            var status = new StatusState();
            status.Apply(Payload.Cryo, 20f, 0f);
            Assert.AreEqual(StatusState.ChillSpeed, status.SpeedMultiplier(1f));
            Assert.AreEqual(StatusState.BrittleDamage, status.DamageTakenMultiplier(1f), 0.001f);
            Assert.AreEqual(1f, status.SpeedMultiplier(StatusState.ChillSeconds + 0.1f));
        }

        [Test]
        public void ElectricStunsThemInPlace()
        {
            var status = new StatusState();
            status.Apply(Payload.Electric, 15f, 0f);
            Assert.AreEqual(0f, status.SpeedMultiplier(0.2f));
            Assert.AreEqual(1f, status.SpeedMultiplier(StatusState.StunSeconds + 0.1f));
        }

        [Test]
        public void KineticLeavesNoDebuff()
        {
            var status = new StatusState();
            status.Apply(Payload.Kinetic, 40f, 0f);
            Assert.IsFalse(status.Any(0.1f));
            Assert.IsNull(status.Tint(0.1f));
        }

        [Test]
        public void TintShowsTheActiveDebuff()
        {
            var burning = new StatusState();
            burning.Apply(Payload.Plasma, 20f, 0f);
            var chilled = new StatusState();
            chilled.Apply(Payload.Cryo, 20f, 0f);
            Assert.IsNotNull(burning.Tint(1f));
            Assert.IsNotNull(chilled.Tint(1f));
            Assert.AreNotEqual(burning.Tint(1f), chilled.Tint(1f));
        }

        [Test]
        public void StackedDebuffsMultiplyIncomingDamage()
        {
            var status = new StatusState();
            status.Apply(Payload.Cryo, 20f, 0f);
            status.Apply(Payload.Plasma, 20f, 0f);
            Assert.AreEqual(StatusState.BrittleDamage * StatusState.MeltDamage, status.DamageTakenMultiplier(1f), 0.001f);
        }
    }

    public class WeaponMeshTests
    {
        private static string Json(float scale) =>
            "{\"parts\":[" +
            "{\"shape\":\"box\",\"x\":0,\"y\":0,\"z\":" + (0.5f * scale) + ",\"sx\":" + (0.1f * scale) + ",\"sy\":" + (0.1f * scale) + ",\"sz\":" + (1.0f * scale) + ",\"color\":\"#FF0000\",\"glow\":false}," +
            "{\"shape\":\"cylinder\",\"x\":0,\"y\":0,\"z\":" + (1.2f * scale) + ",\"rx\":90,\"sx\":" + (0.05f * scale) + ",\"sy\":" + (0.6f * scale) + ",\"sz\":" + (0.05f * scale) + ",\"color\":\"#00FF00\",\"glow\":true}" +
            "],\"muzzleX\":0,\"muzzleY\":0,\"muzzleZ\":" + (1.5f * scale) + "}";

        [Test]
        public void ParsesPartsAndMuzzle()
        {
            var parts = WeaponMesh.Parse(Json(1f), Color.white, out var muzzle);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(PartShape.Cylinder, parts[1].Shape);
            Assert.IsTrue(parts[1].Glow);
            Assert.Greater(muzzle.z, 0f);
        }

        [TestCase(1f)]
        [TestCase(100f)]   // the model answered in centimetres
        [TestCase(0.01f)]  // ...or in something far too small
        public void NormalizeGivesTheSameWeaponSizeWhateverTheUnits(float scale)
        {
            var parts = WeaponMesh.Parse(Json(scale), Color.white, out var muzzle);
            WeaponMesh.Normalize(parts, ref muzzle);
            float longest = 0f;
            foreach (var part in parts)
                longest = Mathf.Max(longest, Mathf.Max(part.Scale.x, Mathf.Max(part.Scale.y, part.Scale.z)));
            Assert.LessOrEqual(longest, WeaponMesh.TargetLength + 0.01f);
            Assert.Greater(longest, 0.05f);
            // Muzzle ends up in front of the hand, where shots should spawn.
            Assert.Greater(muzzle.z, 0.05f);
            Assert.Less(muzzle.z, 0.6f);
        }

        [TestCase(1.5f, 0f, 0f)]
        [TestCase(-1.5f, 0f, 0f)]
        [TestCase(0f, 1.5f, 0f)]
        [TestCase(0f, 0f, -1.5f)]
        public void NormalizeTurnsTheReportedMuzzleForward(float muzzleX, float muzzleY, float muzzleZ)
        {
            string json =
                "{\"parts\":[{\"shape\":\"box\",\"x\":" + (muzzleX * 0.4f) +
                ",\"y\":" + (muzzleY * 0.4f) + ",\"z\":" + (muzzleZ * 0.4f) +
                ",\"sx\":0.1,\"sy\":0.1,\"sz\":0.6,\"color\":\"#FF0000\",\"glow\":false}]," +
                "\"muzzleX\":" + muzzleX + ",\"muzzleY\":" + muzzleY + ",\"muzzleZ\":" + muzzleZ + "}";

            var parts = WeaponMesh.Parse(json, Color.white, out var muzzle);
            WeaponMesh.Normalize(parts, ref muzzle);

            Assert.Greater(muzzle.z, 0.05f);
            Assert.AreEqual(0f, muzzle.x, 0.001f);
            Assert.AreEqual(0f, muzzle.y, 0.001f);
        }

        [Test]
        public void BadJsonYieldsNoParts()
        {
            Assert.IsEmpty(WeaponMesh.Parse("not json", Color.white, out _));
            Assert.IsEmpty(WeaponMesh.Parse("", Color.white, out _));
        }
    }
}
