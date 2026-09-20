using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// The five weapon kinds differ in what the player physically does, so the parser and the swing/draw maths
    /// have to keep each one usable: a sword is limited by the arm, a bow by the draw.
    /// </summary>
    public class ArchetypeTests
    {
        [TestCase("melee", FireMode.Melee)]
        [TestCase("bow", FireMode.Bow)]
        [TestCase("beam", FireMode.Beam)]
        [TestCase("thrown", FireMode.Thrown)]
        [TestCase("projectile", FireMode.Projectile)]
        public void ParsesEveryArchetype(string mode, FireMode expected)
        {
            Assert.AreEqual(expected, WeaponSpecParser.Parse("{\"fireMode\":\"" + mode + "\"}").FireMode);
        }

        [Test]
        public void SwordsSwingAtArmSpeedAndFireOnePellet()
        {
            var sword = WeaponSpecParser.Parse("{\"fireMode\":\"melee\",\"fireRate\":15,\"projectileCount\":8}");
            Assert.LessOrEqual(sword.FireRate, 4f);
            Assert.AreEqual(1, sword.ProjectileCount);
        }

        [Test]
        public void KeywordFallbackRecognisesSwordsAndBows()
        {
            Assert.AreEqual(FireMode.Melee, WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("a flaming katana")).FireMode);
            Assert.AreEqual(FireMode.Bow, WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("an ice crossbow")).FireMode);
            Assert.AreEqual(FireMode.Projectile, WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("a plain rifle")).FireMode);
        }

        [Test]
        public void CarryingTheControllerIsNotASwing()
        {
            Assert.AreEqual(0f, SwingAndDraw.SwingStrength(0.4f));
            Assert.AreEqual(0f, SwingAndDraw.SwingDamage(50f, 0.4f));
        }

        [Test]
        public void HarderSwingsHitHarderUpToFullDamage()
        {
            float slow = SwingAndDraw.SwingDamage(50f, SwingAndDraw.SwingStart + 0.2f);
            float fast = SwingAndDraw.SwingDamage(50f, SwingAndDraw.SwingFull);
            Assert.Greater(slow, 0f);
            Assert.Greater(fast, slow);
            Assert.AreEqual(50f, fast, 0.01f);
            // Swinging like a lunatic is still capped at the weapon's damage.
            Assert.AreEqual(50f, SwingAndDraw.SwingDamage(50f, 20f), 0.01f);
        }

        [Test]
        public void ATwitchOnTheTriggerDoesNotLooseAnArrow()
        {
            Assert.IsFalse(SwingAndDraw.CanRelease(0.05f));
            Assert.IsTrue(SwingAndDraw.CanRelease(SwingAndDraw.MinDrawRelease + 0.01f));
        }

        [Test]
        public void FullDrawHitsHarderAndFliesFaster()
        {
            float weak = SwingAndDraw.DrawDamageMultiplier(0.2f);
            float full = SwingAndDraw.DrawDamageMultiplier(SwingAndDraw.FullDrawSeconds);
            Assert.Less(weak, 1f);
            Assert.AreEqual(SwingAndDraw.FullDrawDamage, full, 0.01f);
            Assert.Greater(SwingAndDraw.DrawSpeedMultiplier(SwingAndDraw.FullDrawSeconds), SwingAndDraw.DrawSpeedMultiplier(0.2f));
            // Holding longer than a full draw changes nothing.
            Assert.AreEqual(full, SwingAndDraw.DrawDamageMultiplier(5f), 0.01f);
        }
    }
}
