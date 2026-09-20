using System.Linq;
using System.Reflection;
using Armory.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Cover for the boss attack loop that does not need a headset: which beat plays, what it costs the player,
    /// that the enrage roar fires once, and that nothing an attack spawns outlives the encounter.
    /// </summary>
    public class HiveBossAttackTests
    {
        [Test]
        public void EveryOrganPowersAtLeastOneBeat()
        {
            foreach (HiveOrgan organ in System.Enum.GetValues(typeof(HiveOrgan)))
                Assert.IsTrue(HiveMoves.Rotation.Any(move => HiveMoves.Organ(move) == organ),
                    "no beat is lost when " + organ + " breaks, so shooting it teaches the player nothing");
        }

        [TestCase(HiveOrgan.LeftClaw, HiveMove.Stomp)]
        [TestCase(HiveOrgan.RightClaw, HiveMove.ClawSweep)]
        [TestCase(HiveOrgan.SporeSac, HiveMove.Fireball)]
        [TestCase(HiveOrgan.Crest, HiveMove.Laser)]
        public void BreakingAnOrganSilencesItsBeat(HiveOrgan organ, HiveMove move)
        {
            var rules = new HiveAvatarState();
            Assert.IsTrue(rules.CanAttack(move));
            rules.HitOrgan(organ, HiveAvatarState.OrganHealth);
            Assert.IsFalse(rules.CanAttack(move));

            int cursor = 0;
            for (int i = 0; i < HiveMoves.Rotation.Length * 2; i++)
            {
                Assert.IsTrue(HiveMoves.TryNext(rules, ref cursor, out var picked));
                Assert.AreNotEqual(move, picked, "the rotation still picked a beat with no organ behind it");
            }
        }

        [Test]
        public void ARotationWithEveryOrganBrokenPicksNothing()
        {
            var rules = new HiveAvatarState();
            foreach (HiveOrgan organ in System.Enum.GetValues(typeof(HiveOrgan)))
                rules.HitOrgan(organ, HiveAvatarState.OrganHealth);
            int cursor = 0;
            Assert.IsFalse(HiveMoves.TryNext(rules, ref cursor, out _));
        }

        [Test]
        public void TheRotationWalksEveryBeatInOrderAndWrapsAround()
        {
            var rules = new HiveAvatarState();
            int cursor = 0;
            foreach (var expected in HiveMoves.Rotation.Concat(HiveMoves.Rotation))
            {
                Assert.IsTrue(HiveMoves.TryNext(rules, ref cursor, out var picked));
                Assert.AreEqual(expected, picked);
            }
        }

        [Test]
        public void EnrageFiresExactlyOnceHoweverOftenItIsPolled()
        {
            var rules = new HiveAvatarState();
            Assert.IsFalse(rules.ShouldEnrage(100f, 100f));
            Assert.IsFalse(rules.ShouldEnrage(HiveAvatarState.EnrageFraction * 100f + 1f, 100f));
            Assert.IsTrue(rules.ShouldEnrage(HiveAvatarState.EnrageFraction * 100f - 1f, 100f));
            for (int i = 0; i < 10; i++) Assert.IsFalse(rules.ShouldEnrage(1f, 100f), "the roar restarted at poll " + i);
            // A boss with no max health is a boss that never spawned; it must not roar on the way out.
            Assert.IsFalse(new HiveAvatarState().ShouldEnrage(0f, 0f));
        }

        [Test]
        public void IgnoringEveryTelegraphCostsTheWaveWithinOneRotation()
        {
            var vitals = new PlayerVitals();
            // No recharge between them: this is the player who stands still and eats the whole rotation.
            float at = 0f;
            foreach (var move in new[] { HiveMove.Stomp, HiveMove.Fireball, HiveMove.ClawSweep, HiveMove.Laser, HiveMove.Laser })
            {
                at += PlayerVitals.GraceSeconds * 2f;
                vitals.Damage(HiveDamage.Of(move), at);
            }
            Assert.IsTrue(vitals.Down, "a player who dodges nothing has to go down inside one rotation");
        }

        [Test]
        public void OneMissedTelegraphIsSurvivableAndRechargesBack()
        {
            var vitals = new PlayerVitals();
            Assert.AreEqual(HiveDamage.Stomp, vitals.Damage(HiveDamage.Stomp, 0f), 0.001f);
            Assert.IsFalse(vitals.Down);
            Assert.Greater(vitals.Fraction, 0.6f, "the heaviest beat must not take most of the bar");
            // The four second lull plus the gap before the next beat hands the bar back to anyone who dodges.
            vitals.Tick(4f, PlayerVitals.RechargeDelay + 4f);
            Assert.AreEqual(PlayerVitals.MaxShield, vitals.Shield, 0.001f);
        }

        [Test]
        public void NoSingleBeatTakesMoreThanAThirdOfTheBar()
        {
            foreach (HiveMove move in System.Enum.GetValues(typeof(HiveMove)))
                Assert.LessOrEqual(HiveDamage.Of(move), PlayerVitals.MaxShield / 3f + 0.001f, move.ToString());
        }

        [Test]
        public void TheGraceWindowCapsWhatStandingInTheBeamCosts()
        {
            var vitals = new PlayerVitals();
            float taken = 0f;
            // Two seconds of beam sampled every frame; PlayerVitals must collapse that into four ticks.
            for (float t = 0f; t < 2f; t += 1f / 72f) taken += vitals.Damage(HiveDamage.LaserTick, t);
            Assert.AreEqual(4f * HiveDamage.LaserTick, taken, 0.001f);
            Assert.IsFalse(vitals.Down, "the beam alone must not be a one-attack kill");
        }

        [Test]
        public void TheBeamIsASegmentSoStandingPastItOrBehindItIsSafe()
        {
            var mouth = new Vector3(0f, 12f, 0f);
            var tip = new Vector3(0f, 0f, 20f);
            Assert.Less(HiveDamage.DistanceToBeam(Vector3.Lerp(mouth, tip, 0.5f), mouth, tip), 0.001f);
            Assert.Greater(HiveDamage.DistanceToBeam(new Vector3(0f, 1f, 40f), mouth, tip), HiveDamage.LaserRadius);
            Assert.Greater(HiveDamage.DistanceToBeam(new Vector3(0f, 1f, -10f), mouth, tip), HiveDamage.LaserRadius);
            Assert.Greater(HiveDamage.DistanceToBeam(new Vector3(8f, 1f, 10f), mouth, tip), HiveDamage.LaserRadius);
        }

        /// <summary>
        /// The most likely way this whole feature fails silently: the clips call by name with DontRequireReceiver,
        /// so a renamed or re-signatured method is a no-op nobody sees. Hold the art and the receiver together.
        /// </summary>
        [Test]
        public void EveryAuthoredAnimationEventResolvesToAPublicVoidNoArgMethod()
        {
            foreach (var name in HiveAvatarEvents.Callbacks)
            {
                var method = typeof(HiveAvatarEvents).GetMethod(name, BindingFlags.Instance | BindingFlags.Public,
                    null, System.Type.EmptyTypes, null);
                Assert.IsNotNull(method, "HiveAvatarEvents is missing " + name);
                Assert.AreEqual(typeof(void), method.ReturnType, name);
            }
        }

        [TestCase("Stomp")]
        [TestCase("Fireball")]
        [TestCase("Laser")]
        public void TheClipsOnlyCallMethodsTheReceiverHas(string clipName)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/HiveAvatar/Animations/" + clipName + ".anim");
            Assert.IsNotNull(clip, clipName);
            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.IsNotEmpty(events, clipName);
            foreach (var authored in events)
                Assert.Contains(authored.functionName, HiveAvatarEvents.Callbacks,
                    clipName + " calls a method the receiver does not carry");
        }

        [Test]
        public void TheDeathSequenceOutlastsTheSettleClip()
        {
            var settle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/HiveAvatar/Animations/DeathSettle.anim");
            Assert.IsNotNull(settle);
            Assert.Greater(HiveAvatar.DeathSequenceSeconds, settle.length,
                "Enemy.Die would destroy the body before the collapse finishes");
        }

        [Test]
        public void ClearingHazardsEmptiesEveryCollection()
        {
            var hazards = new HiveHazards();
            var owner = new GameObject("Hazard Root");
            var stray = new GameObject("Stray Effect");
            try
            {
                hazards.Track(HiveTelegraph.Create(owner.transform, Vector3.zero, 3f, Color.white));
                hazards.Track(HiveTelegraph.Create(owner.transform, Vector3.one, 3f, Color.white));
                hazards.Track("BossLaserBeam", stray);
                Assert.AreEqual(2, hazards.MarkerCount);
                Assert.AreEqual(1, hazards.EffectCount);
                Assert.AreEqual(3, hazards.Count);

                // Destroyed before the clear on purpose: a wave restart takes the scene away first, and the
                // cleanup still has to empty its lists instead of throwing on the corpses.
                Object.DestroyImmediate(stray);
                hazards.Clear();
                Assert.AreEqual(0, hazards.Count);
                Assert.AreEqual(0, hazards.MarkerCount);
                Assert.AreEqual(0, hazards.EffectCount);
                // Clearing twice, and clearing after the scene took the objects away, must both be harmless.
                hazards.Clear();
            }
            finally
            {
                foreach (var child in owner.GetComponentsInChildren<Transform>(true).Where(t => t != owner.transform).ToArray())
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                if (owner != null) Object.DestroyImmediate(owner);
                if (stray != null) Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void TrackingNothingIsSafeAndReleasingAnUnknownEffectDoesNotThrow()
        {
            var hazards = new HiveHazards();
            hazards.Track((HiveTelegraph)null);
            hazards.Track((HiveThreat)null);
            hazards.Track("BossFireball", null);
            hazards.Release((HiveTelegraph)null);
            hazards.Release((GameObject)null);
            Assert.AreEqual(0, hazards.Count);
        }
    }
}
