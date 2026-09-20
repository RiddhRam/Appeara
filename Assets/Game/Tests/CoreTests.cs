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

        [Test]
        public void WeaponPackageAlwaysHasOneDefenseAndOneTactic()
        {
            var weapon = WeaponSpecParser.Parse("{\"name\":\"Arc Lobber\",\"fireMode\":\"thrown\",\"payload\":\"electric\",\"modifiers\":[\"sticky\"]}");
            var package = AdaptationRules.ForWeapon(weapon, null, null, null, 7);
            Assert.AreEqual(CounterFamily.Defensive, CounterCatalog.Family(package.Defense.Kind));
            Assert.AreEqual(CounterFamily.Tactical, CounterCatalog.Family(package.Tactic.Kind));
            Assert.AreEqual(new Counter(CounterKind.Resist, "electric"), package.Defense);
            Assert.AreEqual(new Counter(CounterKind.Intercept), package.Tactic);
            Assert.AreEqual(7, package.WeaponRevision);
        }

        [Test]
        public void WeaponPackageRejectsWrongFamiliesAndAbsentResistance()
        {
            var weapon = WeaponSpecParser.Parse("{\"name\":\"Boomstick\",\"payload\":\"explosive\",\"projectileSpeed\":50}");
            var package = AdaptationRules.ForWeapon(weapon, "dodge", "armor", "We adapt.", 1);
            Assert.AreEqual(new Counter(CounterKind.Resist, "explosive"), package.Defense);
            Assert.AreEqual(new Counter(CounterKind.Spread), package.Tactic);

            package = AdaptationRules.ForWeapon(weapon, "resist:cryo", "rush", null, 2);
            Assert.AreEqual(new Counter(CounterKind.Resist, "explosive"), package.Defense);
            Assert.AreEqual(new Counter(CounterKind.Rush), package.Tactic);
        }

        [Test]
        public void ValidModelPackageIsPreserved()
        {
            var weapon = WeaponSpecParser.Parse("{\"name\":\"Plasma Seeker\",\"payload\":\"plasma\",\"modifiers\":[\"homing\"]}");
            var package = AdaptationRules.ForWeapon(weapon, "resist:homing", "teleport", "Found you.", 3);
            Assert.AreEqual(new Counter(CounterKind.Resist, "homing"), package.Defense);
            Assert.AreEqual(new Counter(CounterKind.Teleport), package.Tactic);
            Assert.AreEqual("Found you.", package.Taunt);
        }

        [Test]
        public void AnalysisClockConsumesOnlyCombatTimeAndTriggersOnce()
        {
            var clock = new CounterAnalysisClock();
            clock.Start(20f);
            Assert.IsFalse(clock.Advance(12f, false));
            Assert.AreEqual(20f, clock.Remaining);
            Assert.IsFalse(clock.Advance(19f, true));
            Assert.AreEqual(1f, clock.Remaining);
            Assert.IsTrue(clock.Advance(1f, true));
            Assert.IsFalse(clock.Advance(5f, true));
            Assert.IsFalse(clock.Running);
        }

        [Test]
        public void StartingAnalysisAgainResetsTheGracePeriod()
        {
            var clock = new CounterAnalysisClock();
            clock.Start(20f);
            clock.Advance(8f, true);
            clock.Start(20f);
            Assert.AreEqual(20f, clock.Remaining);
            Assert.IsTrue(clock.Running);
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

    public class VoiceAudioTests
    {
        private static float[] Tone(int count, float amplitude)
        {
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = Mathf.Sin(i * 0.05f) * amplitude;
            return samples;
        }

        [Test]
        public void SilentCaptureIsReportedNotSwallowed()
        {
            // The Quest virtual mic returned ~3e-5 peaks; that must surface as a reason, not vanish.
            var verdict = VoiceAudio.Judge(Tone(16000, 0.00003f), 16000, out string report);
            Assert.AreEqual(CaptureVerdict.Silent, verdict);
            StringAssert.Contains("heard nothing", report);
        }

        [Test]
        public void ShortCaptureIsReported()
        {
            Assert.AreEqual(CaptureVerdict.TooShort, VoiceAudio.Judge(Tone(1600, 0.4f), 16000, out string report));
            StringAssert.Contains("too short", report);
        }

        [Test]
        public void NormalSpeechPasses()
        {
            Assert.AreEqual(CaptureVerdict.Ok, VoiceAudio.Judge(Tone(24000, 0.08f), 16000, out _));
        }

        [Test]
        public void QuietCaptureIsNormalisedNotRejected()
        {
            var samples = Tone(24000, 0.05f);
            Assert.AreEqual(CaptureVerdict.Ok, VoiceAudio.Judge(samples, 16000, out _));
            float gain = VoiceAudio.Normalize(samples);
            Assert.Greater(gain, 1f);
            Assert.AreEqual(VoiceAudio.TargetPeak, VoiceAudio.Peak(samples), 0.02f);
        }

        [Test]
        public void VeryQuietCaptureIsLiftedButGainIsCapped()
        {
            // 12x is the ceiling: louder amplification would just raise the mic's hiss.
            var samples = Tone(24000, 0.01f);
            Assert.AreEqual(12f, VoiceAudio.Normalize(samples), 0.01f);
            Assert.AreEqual(0.12f, VoiceAudio.Peak(samples), 0.01f);
        }

        [Test]
        public void DownsampleKeepsDurationAndRate()
        {
            var samples = VoiceAudio.Downsample(Tone(48000, 0.5f), 48000, 16000, out int rate);
            Assert.AreEqual(16000, rate);
            Assert.AreEqual(16000, samples.Length);
        }

        [Test]
        public void TrimUsesRelativeThresholdSoQuietMicsSurvive()
        {
            var samples = new float[16000];
            for (int i = 6000; i < 7000; i++) samples[i] = 0.02f; // quiet speech burst
            var trimmed = VoiceAudio.Trim(samples, 160);
            Assert.Greater(trimmed.Length, 900);
            Assert.Less(trimmed.Length, 1400);
            Assert.IsEmpty(VoiceAudio.Trim(new float[8000], 160));
        }
    }
}
