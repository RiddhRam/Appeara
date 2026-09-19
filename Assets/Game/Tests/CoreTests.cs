using System.Collections.Generic;
using System.Linq;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    public class WeaponSpecParserTests
    {
        [Test]
        public void ParsesTypicalLlmOutput()
        {
            var weapon = WeaponSpecParser.Parse("{\"name\":\"Mine Shotgun\",\"fireMode\":\"projectile\",\"payload\":\"explosive\",\"modifiers\":[\"sticky\",\"proximity\"],\"projectileCount\":6,\"spreadDeg\":20,\"visual\":{\"primaryColor\":\"#FF0000\",\"projectileShape\":\"mine\"}}");
            Assert.NotNull(weapon);
            Assert.AreEqual(Payload.Explosive, weapon.Payload);
            Assert.IsTrue(weapon.Has(Mods.Sticky) && weapon.Has(Mods.Proximity) && weapon.Has(Mods.Splash));
            Assert.AreEqual(6, weapon.ProjectileCount);
            Assert.AreEqual(ProjectileShape.Mine, weapon.Shape);
            Assert.AreEqual(Color.red, weapon.Color);
        }

        [Test]
        public void InvalidJsonReturnsNull()
        {
            Assert.IsNull(WeaponSpecParser.Parse("not json {"));
            Assert.IsNull(WeaponSpecParser.Parse(""));
        }

        [Test]
        public void UnknownEnumsFallBackToDefaults()
        {
            var weapon = WeaponSpecParser.Parse("{\"fireMode\":\"nuke\",\"payload\":\"antimatter\",\"modifiers\":[\"telepathic\",\"255\",\"homing\"]}");
            Assert.AreEqual(FireMode.Projectile, weapon.FireMode);
            Assert.AreEqual(Payload.Kinetic, weapon.Payload);
            Assert.AreEqual(Mods.Homing, weapon.Mods);
            Assert.AreEqual("Unnamed Contraption", weapon.Name);
        }

        [Test]
        public void ClampsExtremeNumbers()
        {
            var weapon = WeaponSpecParser.Parse("{\"fireRate\":9999,\"projectileCount\":500,\"spreadDeg\":720,\"projectileSpeed\":99999,\"damage\":1e9}");
            Assert.LessOrEqual(weapon.FireRate, 15f);
            Assert.LessOrEqual(weapon.ProjectileCount, 12);
            Assert.LessOrEqual(weapon.SpreadDeg, 45f);
            Assert.LessOrEqual(weapon.ProjectileSpeed, 80f);
            Assert.LessOrEqual(weapon.Damage * weapon.FireRate * weapon.ProjectileCount, WeaponSpecParser.MaxDps + 0.01f);
        }

        [Test]
        public void StackingModifiersCostsDamage()
        {
            var plain = WeaponSpecParser.Parse("{\"damage\":10,\"fireRate\":1,\"modifiers\":[\"homing\"]}");
            var stacked = WeaponSpecParser.Parse("{\"damage\":10,\"fireRate\":1,\"modifiers\":[\"homing\",\"piercing\",\"bouncing\"]}");
            Assert.Less(stacked.Damage, plain.Damage);
        }

        [Test]
        public void MissingNumbersGetPlayableDefaults()
        {
            var weapon = WeaponSpecParser.Parse("{}");
            Assert.Greater(weapon.FireRate, 1f);
            Assert.Greater(weapon.ProjectileSpeed, 10f);
            Assert.Greater(weapon.Damage, 1f);
        }

        [Test]
        public void PrimitivesIncludePayloadModeAndMods()
        {
            var weapon = WeaponSpecParser.Parse("{\"fireMode\":\"beam\",\"payload\":\"electric\",\"modifiers\":[\"homing\"]}");
            CollectionAssert.AreEquivalent(new[] { "electric", "beam", "homing", "chain" }, weapon.Primitives().ToArray());
        }
    }

    public class DamageTableTests
    {
        private static ParsedWeapon W(string json) => WeaponSpecParser.Parse(json);

        [Test]
        public void ArmoredPunishesKineticRewardsPiercing()
        {
            float kinetic = DamageTable.Multiplier(EnemyKind.Armored, W("{\"payload\":\"kinetic\"}"), false, null);
            float rail = DamageTable.Multiplier(EnemyKind.Armored, W("{\"payload\":\"kinetic\",\"modifiers\":[\"piercing\"]}"), false, null);
            Assert.Less(kinetic, 0.5f);
            Assert.Greater(rail, 1.2f);
        }

        [Test]
        public void ShieldOnlyYieldsToElectric()
        {
            Assert.AreEqual(3f, DamageTable.Multiplier(EnemyKind.Shielded, W("{\"payload\":\"electric\"}"), true, null));
            Assert.AreEqual(0.2f, DamageTable.Multiplier(EnemyKind.Shielded, W("{\"payload\":\"plasma\"}"), true, null), 1e-4);
        }

        [Test]
        public void SwarmLikesSplash()
        {
            Assert.Greater(DamageTable.Multiplier(EnemyKind.Swarm, W("{\"payload\":\"explosive\"}"), false, null), 1.2f);
        }

        [Test]
        public void ResistanceCutsDamage()
        {
            var weapon = W("{\"payload\":\"plasma\"}");
            float normal = DamageTable.Multiplier(EnemyKind.Boss, weapon, false, null);
            float resisted = DamageTable.Multiplier(EnemyKind.Boss, weapon, false, new HashSet<string> { "plasma" });
            Assert.AreEqual(normal * DamageTable.ResistFactor, resisted, 1e-4);
        }
    }

    public class AdaptationRulesTests
    {
        [Test]
        public void ExplosiveSpamMakesEnemiesSpreadAndResist()
        {
            var log = new CombatLog();
            log.Record(WeaponSpecParser.Parse("{\"payload\":\"explosive\"}"), 500f);
            log.Record(WeaponSpecParser.Parse("{\"payload\":\"kinetic\"}"), 50f);
            var counters = AdaptationRules.Fallback(log);
            Assert.Contains(new Counter(CounterKind.Spread), counters);
            Assert.Contains(new Counter(CounterKind.Resist, "explosive"), counters);
            Assert.LessOrEqual(counters.Count, AdaptationRules.MaxCounters);
        }

        [Test]
        public void BeamSpamGetsReflect()
        {
            var log = new CombatLog();
            log.Record(WeaponSpecParser.Parse("{\"fireMode\":\"beam\",\"payload\":\"kinetic\"}"), 100f);
            log.Record(WeaponSpecParser.Parse("{\"fireMode\":\"beam\",\"payload\":\"plasma\"}"), 400f);
            CollectionAssert.Contains(AdaptationRules.Fallback(log), new Counter(CounterKind.Reflect));
        }

        [Test]
        public void EmptyLogGivesNoCounters()
        {
            Assert.IsEmpty(AdaptationRules.Fallback(new CombatLog()));
        }

        [Test]
        public void SanitizeDropsGarbageAndDuplicates()
        {
            var counters = AdaptationRules.Sanitize(new[] { "armor", "ARMOR", "resist:plasma", "resist:love", "summon_dragon", "shield" });
            CollectionAssert.AreEqual(new[] { new Counter(CounterKind.Armor), new Counter(CounterKind.Resist, "plasma"), new Counter(CounterKind.Shield) }, counters);
        }

        [Test]
        public void CounterRoundTrips()
        {
            Assert.IsTrue(Counter.TryParse("resist:homing", out var counter));
            Assert.AreEqual("resist:homing", counter.ToString());
        }
    }

    public class WavPcmTests
    {
        [Test]
        public void WavHasHeaderAndSixteenBitSamples()
        {
            var wav = WavPcm.EncodeWav(new float[100], 1, 16000);
            Assert.AreEqual(44 + 200, wav.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        }

        [Test]
        public void Pcm16RoundTrips()
        {
            var floats = WavPcm.Pcm16ToFloats(new byte[] { 0x00, 0x40, 0x00, 0xC0 });
            Assert.AreEqual(0.5f, floats[0], 1e-3);
            Assert.AreEqual(-0.5f, floats[1], 1e-3);
        }

        [Test]
        public void TrimSilenceKeepsSpeech()
        {
            var samples = new float[10000];
            samples[5000] = 0.5f;
            var trimmed = WavPcm.TrimSilence(samples, 0.01f, 100);
            Assert.AreEqual(201, trimmed.Length);
            Assert.IsEmpty(WavPcm.TrimSilence(new float[500]));
        }
    }

    public class MockInterpreterTests
    {
        [Test]
        public void StickyMineShotgun()
        {
            var weapon = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("Give me a shotgun that fires sticky mines which explode when aliens get close"));
            Assert.Greater(weapon.ProjectileCount, 1);
            Assert.IsTrue(weapon.Has(Mods.Sticky) && weapon.Has(Mods.Proximity));
            Assert.AreEqual(Payload.Explosive, weapon.Payload);
        }

        [Test]
        public void RailgunPierces()
        {
            var weapon = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("railgun that pierces three enemies"));
            Assert.IsTrue(weapon.Has(Mods.Piercing));
        }

        [Test]
        public void WordStartMatchingAvoidsFalsePositives()
        {
            var weapon = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson("a nice spray gun"));
            Assert.AreEqual(Payload.Kinetic, weapon.Payload);
            Assert.AreEqual(FireMode.Projectile, weapon.FireMode);
        }
    }
}
