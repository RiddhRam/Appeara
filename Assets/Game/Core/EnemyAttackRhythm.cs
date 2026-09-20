namespace Armory.Core
{
    public enum EnemyAttackBeat
    {
        None,
        Started,
        Hit
    }

    /// <summary>Deterministic wind-up and recovery timing for ordinary enemies attacking the station core.</summary>
    public sealed class EnemyAttackRhythm
    {
        private enum Phase { Ready, Windup, Recovery }

        private readonly float windupSeconds;
        private readonly float recoverySeconds;
        private Phase phase;
        private float elapsed;

        public EnemyAttackRhythm(float windupSeconds, float recoverySeconds)
        {
            this.windupSeconds = windupSeconds > 0f ? windupSeconds : 0.01f;
            this.recoverySeconds = recoverySeconds > 0f ? recoverySeconds : 0.01f;
        }

        public EnemyAttackBeat Tick(float deltaTime, bool inReach, bool canAct)
        {
            if (!inReach)
            {
                Reset();
                return EnemyAttackBeat.None;
            }
            if (!canAct)
            {
                // A stun invalidates the old tell. Starting over when it clears guarantees that every hit is
                // preceded by a complete, visible windup instead of landing late from a forgotten cycle.
                Reset();
                return EnemyAttackBeat.None;
            }

            if (phase == Phase.Ready)
            {
                phase = Phase.Windup;
                elapsed = 0f;
                return EnemyAttackBeat.Started;
            }

            elapsed += deltaTime > 0f ? deltaTime : 0f;
            if (phase == Phase.Windup && elapsed >= windupSeconds)
            {
                phase = Phase.Recovery;
                elapsed = 0f;
                return EnemyAttackBeat.Hit;
            }
            if (phase == Phase.Recovery && elapsed >= recoverySeconds)
            {
                phase = Phase.Ready;
                elapsed = 0f;
                return Tick(0f, true, true);
            }
            return EnemyAttackBeat.None;
        }

        public void Reset()
        {
            phase = Phase.Ready;
            elapsed = 0f;
        }
    }
}
