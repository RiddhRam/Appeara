using Armory.Core;
using NUnit.Framework;

namespace Armory.Tests
{
    /// <summary>Shields have to punish standing in a swarm without ending the run on one mistake.</summary>
    public class PlayerVitalsTests
    {
        [Test]
        public void TakesDamageAndReportsWhatLanded()
        {
            var vitals = new PlayerVitals();
            Assert.AreEqual(20f, vitals.Damage(20f, 0f));
            Assert.AreEqual(PlayerVitals.MaxShield - 20f, vitals.Shield);
        }

        [Test]
        public void GraceWindowStopsASwarmChainingHits()
        {
            var vitals = new PlayerVitals();
            vitals.Damage(20f, 0f);
            Assert.AreEqual(0f, vitals.Damage(20f, PlayerVitals.GraceSeconds * 0.5f), "second hit inside the grace window should be ignored");
            Assert.Greater(vitals.Damage(20f, PlayerVitals.GraceSeconds + 0.01f), 0f);
        }

        [Test]
        public void RechargesOnlyAfterALull()
        {
            var vitals = new PlayerVitals();
            vitals.Damage(50f, 0f);
            vitals.Tick(1f, PlayerVitals.RechargeDelay - 1f);
            Assert.AreEqual(PlayerVitals.MaxShield - 50f, vitals.Shield, 0.01f);
            vitals.Tick(1f, PlayerVitals.RechargeDelay + 1f);
            Assert.AreEqual(PlayerVitals.MaxShield - 50f + PlayerVitals.RechargePerSecond, vitals.Shield, 0.01f);
        }

        [Test]
        public void DownedShieldsStayDownUntilRestored()
        {
            var vitals = new PlayerVitals();
            vitals.Damage(PlayerVitals.MaxShield, 0f);
            Assert.IsTrue(vitals.Down);
            vitals.Tick(10f, 100f);
            Assert.IsTrue(vitals.Down, "being downed should not quietly self-heal mid-wave");
            vitals.Restore();
            Assert.AreEqual(PlayerVitals.MaxShield, vitals.Shield);
        }

        [Test]
        public void RechargeStopsAtFull()
        {
            var vitals = new PlayerVitals();
            vitals.Damage(10f, 0f);
            vitals.Tick(100f, 1000f);
            Assert.AreEqual(PlayerVitals.MaxShield, vitals.Shield);
        }
    }
}
