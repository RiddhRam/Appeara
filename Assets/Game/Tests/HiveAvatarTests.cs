using Armory.Core;
using NUnit.Framework;

namespace Armory.Tests
{
    public class HiveAvatarTests
    {
        [Test]
        public void NewPlatingReplacesOldResistanceSoSwitchingWeaponsWorks()
        {
            var state = new HiveAvatarState();
            var plasma = WeaponSpecParser.Parse("{\"payload\":\"plasma\"}");
            var cryo = WeaponSpecParser.Parse("{\"payload\":\"cryo\"}");
            state.Adapt("plasma");
            Assert.AreEqual(0.3f, state.DamageScale(plasma), 0.001f);
            Assert.AreEqual(1f, state.DamageScale(cryo));
            state.Adapt("cryo");
            Assert.AreEqual(1f, state.DamageScale(plasma));
            Assert.AreEqual(0.3f, state.DamageScale(cryo), 0.001f);
        }

        [TestCase(HiveOrgan.LeftClaw, HiveAttack.SweepLeft)]
        [TestCase(HiveOrgan.RightClaw, HiveAttack.SweepRight)]
        [TestCase(HiveOrgan.SporeSac, HiveAttack.Spores)]
        [TestCase(HiveOrgan.Crest, HiveAttack.Wreck)]
        public void BreakingOrganDisablesOnlyItsAttack(HiveOrgan organ, HiveAttack attack)
        {
            var state = new HiveAvatarState();
            Assert.IsFalse(state.HitOrgan(organ, 100f));
            Assert.IsTrue(state.CanAttack(attack));
            Assert.IsTrue(state.HitOrgan(organ, 80f));
            Assert.IsFalse(state.CanAttack(attack));
            Assert.AreEqual(1, state.BrokenCount);
            foreach (HiveAttack other in System.Enum.GetValues(typeof(HiveAttack)))
                if (other != attack) Assert.IsTrue(state.CanAttack(other));
        }

        [Test]
        public void OrganBreakIsReportedOnceAndBadDamageCannotHealIt()
        {
            var state = new HiveAvatarState();
            state.HitOrgan(HiveOrgan.Crest, 100f);
            state.HitOrgan(HiveOrgan.Crest, -100f);
            state.HitOrgan(HiveOrgan.Crest, float.NaN);
            Assert.IsTrue(state.HitOrgan(HiveOrgan.Crest, 80f));
            Assert.IsFalse(state.HitOrgan(HiveOrgan.Crest, 500f));
            Assert.AreEqual(1, state.BrokenCount);
        }

        [Test]
        public void InvalidAdaptationDoesNotEraseCurrentPlating()
        {
            var state = new HiveAvatarState();
            state.Adapt("plasma");
            state.Adapt(null);
            state.Adapt("infinite immunity");
            Assert.AreEqual("plasma", state.Plating);
        }
    }
}
