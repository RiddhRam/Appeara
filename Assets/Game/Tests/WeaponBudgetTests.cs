using Armory.Core;
using NUnit.Framework;

namespace Armory.Tests
{
    /// <summary>
    /// Guards the fabrication budget: the allowance has to grow, an honest weapon has to fit early, and a greedy
    /// one has to come back trimmed, explained and still shootable.
    /// </summary>
    public class WeaponBudgetTests
    {
        private static ParsedWeapon W(string json) => WeaponSpecParser.Parse(json);

        private const string MachineGun = "{\"name\":\"Machine Gun\",\"payload\":\"kinetic\",\"fireRate\":10,\"damage\":8}";
        private const string EverythingRocket =
            "{\"name\":\"Infinity Rocket\",\"payload\":\"explosive\",\"modifiers\":[\"homing\",\"piercing\",\"chain\",\"bouncing\"]," +
            "\"onHit\":[\"splash\",\"slow\"],\"fireRate\":12,\"projectileCount\":8,\"damage\":9999}";

        [Test]
        public void BudgetGrowsWithTheWave()
        {
            Assert.AreEqual(WeaponBudget.Wave1Budget, WeaponBudget.Budget(0));
            for (int wave = 1; wave < 12; wave++)
                Assert.Greater(WeaponBudget.Budget(wave), WeaponBudget.Budget(wave - 1), "wave " + wave);
            Assert.AreEqual(WeaponBudget.Wave5Budget, WeaponBudget.Budget(4));
            Assert.AreEqual(WeaponBudget.Wave5Budget + WeaponBudget.BudgetStep, WeaponBudget.Budget(5));
            // A negative index can only come from an uninitialised director; it must not hand out a free weapon.
            Assert.AreEqual(WeaponBudget.Wave1Budget, WeaponBudget.Budget(-3));
        }

        [Test]
        public void ElementsAndFireModesCarryTheirPrice()
        {
            Assert.AreEqual(0, WeaponBudget.Cost(W("{\"payload\":\"kinetic\",\"fireRate\":2,\"damage\":10}")));
            Assert.AreEqual(WeaponBudget.ElementCost, WeaponBudget.Cost(W("{\"payload\":\"plasma\",\"fireRate\":2,\"damage\":10}")));
            Assert.AreEqual(WeaponBudget.ElementCost + WeaponBudget.BeamCost,
                WeaponBudget.Cost(W("{\"fireMode\":\"beam\",\"payload\":\"plasma\",\"fireRate\":2,\"damage\":10}")));
            // Electric arcs whether or not it was asked for, so it pays for the chain it gets.
            Assert.AreEqual(WeaponBudget.ElementCost + WeaponBudget.ModifierCost,
                WeaponBudget.Cost(W("{\"payload\":\"electric\",\"fireRate\":2,\"damage\":10}")));
        }

        [Test]
        public void RateSalvoAndDamageBandsCost()
        {
            Assert.AreEqual(WeaponBudget.HighBandCost * 2,
                WeaponBudget.Cost(W("{\"payload\":\"kinetic\",\"fireRate\":10,\"projectileCount\":8,\"damage\":5}")));
            Assert.AreEqual(WeaponBudget.HighBandCost,
                WeaponBudget.Cost(W("{\"payload\":\"kinetic\",\"fireRate\":1,\"damage\":70}")));
            Assert.AreEqual(WeaponBudget.MidBandCost,
                WeaponBudget.Cost(W("{\"payload\":\"kinetic\",\"fireRate\":6,\"damage\":5}")));
        }

        [Test]
        public void PlainMachineGunFitsTheFirstWave()
        {
            var weapon = W(MachineGun);
            Assert.LessOrEqual(WeaponBudget.Cost(weapon), WeaponBudget.Budget(0));
        }

        [Test]
        public void WeaponUnderBudgetIsLeftAloneAndSaysNothing()
        {
            var weapon = W(MachineGun);
            Assert.IsFalse(WeaponBudget.Trim(weapon, WeaponBudget.Budget(2), out string report));
            Assert.IsEmpty(report);
            Assert.AreEqual(10f, weapon.FireRate);
            Assert.AreEqual(1, weapon.ProjectileCount);
            Assert.AreEqual(8f, weapon.Damage, 1e-4);
            Assert.AreEqual(Mods.None, weapon.Mods);
        }

        [Test]
        public void OverSpecifiedWeaponIsTrimmedUntilItFits()
        {
            var weapon = W(EverythingRocket);
            int budget = WeaponBudget.Budget(0);
            Assert.Greater(WeaponBudget.Cost(weapon), budget, "test weapon is supposed to overrun the first wave");

            Assert.IsTrue(WeaponBudget.Trim(weapon, budget, out string report));
            Assert.LessOrEqual(WeaponBudget.Cost(weapon), budget);
            // Modifiers are the cheapest thing to lose, so they go before the gun stops feeling like a gun.
            Assert.AreEqual(12f, weapon.FireRate);
            Assert.AreEqual(8, weapon.ProjectileCount);
        }

        [Test]
        public void ReportNamesWhatWasCut()
        {
            var weapon = W(EverythingRocket);
            WeaponBudget.Trim(weapon, WeaponBudget.Budget(0), out string report);
            StringAssert.Contains("dropped", report);
            StringAssert.Contains("chain", report);
            StringAssert.Contains("homing", report);
            StringAssert.Contains("to fit " + WeaponBudget.Budget(0) + " energy", report);
        }

        [Test]
        public void TightBudgetAlsoTrimsRateAndSalvo()
        {
            var weapon = W(EverythingRocket);
            Assert.IsTrue(WeaponBudget.Trim(weapon, 4, out string report));
            Assert.LessOrEqual(WeaponBudget.Cost(weapon), 4);
            Assert.Less(weapon.FireRate, 12f);
            Assert.Less(weapon.ProjectileCount, 8);
            StringAssert.Contains("trimmed rate and salvo", report);
        }

        [Test]
        public void TrimmingNeverLeavesAnUnusableWeapon()
        {
            // Budget 0 is unreachable - the payload alone costs something - so this also proves the loop terminates.
            var weapon = W(EverythingRocket);
            WeaponBudget.Trim(weapon, 0, out _);
            Assert.GreaterOrEqual(weapon.FireRate, WeaponBudget.MinFireRate);
            Assert.GreaterOrEqual(weapon.ProjectileCount, WeaponBudget.MinProjectileCount);
            Assert.GreaterOrEqual(weapon.Damage, WeaponBudget.MinDamage);
        }

        [TestCase("{\"payload\":\"cryo\",\"fireMode\":\"beam\",\"fireRate\":15,\"projectileCount\":12,\"damage\":9999,\"modifiers\":[\"homing\",\"piercing\",\"sticky\",\"proximity\"]}")]
        [TestCase("{\"payload\":\"plasma\",\"fireMode\":\"thrown\",\"projectileCount\":12,\"onHit\":[\"splash\",\"chain\",\"slow\"]}")]
        [TestCase("{\"payload\":\"electric\",\"fireMode\":\"bow\",\"damage\":100,\"modifiers\":[\"bouncing\",\"homing\"]}")]
        public void EveryWeaponFitsSomeWaveBudget(string json)
        {
            var weapon = W(json);
            int budget = WeaponBudget.Budget(0);
            WeaponBudget.Trim(weapon, budget, out _);
            Assert.LessOrEqual(WeaponBudget.Cost(weapon), budget, weapon.Name);
            Assert.GreaterOrEqual(weapon.Damage, WeaponBudget.MinDamage);
        }
    }
}
