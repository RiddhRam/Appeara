using Armory.Core;
using NUnit.Framework;

namespace Armory.Tests
{
    public class EnemyAttackRhythmTests
    {
        [Test]
        public void ReachingTheCoreStartsBeforeDamageAndHitsOnceAfterTheWindup()
        {
            var rhythm = new EnemyAttackRhythm(0.4f, 0.6f);

            Assert.That(rhythm.Tick(0f, true, true), Is.EqualTo(EnemyAttackBeat.Started));
            Assert.That(rhythm.Tick(0.39f, true, true), Is.EqualTo(EnemyAttackBeat.None));
            Assert.That(rhythm.Tick(0.02f, true, true), Is.EqualTo(EnemyAttackBeat.Hit));
            Assert.That(rhythm.Tick(0.59f, true, true), Is.EqualTo(EnemyAttackBeat.None));
            Assert.That(rhythm.Tick(0.02f, true, true), Is.EqualTo(EnemyAttackBeat.Started));
        }

        [Test]
        public void StunCancelsTheWindupSoDamageNeedsAFreshTellAfterRecovery()
        {
            var rhythm = new EnemyAttackRhythm(0.4f, 0.6f);
            rhythm.Tick(0f, true, true);

            Assert.That(rhythm.Tick(2f, true, false), Is.EqualTo(EnemyAttackBeat.None));
            Assert.That(rhythm.Tick(0f, true, true), Is.EqualTo(EnemyAttackBeat.Started));
            Assert.That(rhythm.Tick(0.39f, true, true), Is.EqualTo(EnemyAttackBeat.None));
            Assert.That(rhythm.Tick(0.02f, true, true), Is.EqualTo(EnemyAttackBeat.Hit));
        }

        [Test]
        public void LeavingReachCancelsAnUnfinishedAttack()
        {
            var rhythm = new EnemyAttackRhythm(0.4f, 0.6f);
            rhythm.Tick(0.3f, true, true);

            Assert.That(rhythm.Tick(0.2f, false, true), Is.EqualTo(EnemyAttackBeat.None));
            Assert.That(rhythm.Tick(0f, true, true), Is.EqualTo(EnemyAttackBeat.Started));
            Assert.That(rhythm.Tick(0.1f, true, true), Is.EqualTo(EnemyAttackBeat.None));
        }

        [Test]
        public void ResetClearsAReusedEnemiesPreviousCycle()
        {
            var rhythm = new EnemyAttackRhythm(0.4f, 0.6f);
            rhythm.Tick(0f, true, true);
            rhythm.Tick(0.4f, true, true);

            rhythm.Reset();

            Assert.That(rhythm.Tick(0f, true, true), Is.EqualTo(EnemyAttackBeat.Started));
            Assert.That(rhythm.Tick(0.39f, true, true), Is.EqualTo(EnemyAttackBeat.None));
        }
    }
}
