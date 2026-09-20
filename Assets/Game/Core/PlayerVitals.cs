using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// The player's own shields. Until now every threat drained the station core and the player could stand in a
    /// swarm unharmed, which made melee weapons and the boss's attacks feel weightless. Shields recharge after a
    /// short lull so a mistake costs ground, not the run. Pure logic so the recharge curve is testable.
    /// </summary>
    public sealed class PlayerVitals
    {
        public const float MaxShield = 100f;
        public const float RechargeDelay = 4f;
        public const float RechargePerSecond = 18f;
        /// <summary>Hits inside this window after taking damage are ignored, so a swarm cannot chain-stun you.</summary>
        public const float GraceSeconds = 0.6f;

        public float Shield { get; private set; } = MaxShield;
        public float Fraction => Shield / MaxShield;
        public bool Down => Shield <= 0f;
        public float LastHitAt { get; private set; } = -999f;

        /// <returns>Damage actually taken; zero while in the grace window.</returns>
        public float Damage(float amount, float now)
        {
            if (amount <= 0f || now - LastHitAt < GraceSeconds) return 0f;
            LastHitAt = now;
            float taken = Mathf.Min(Shield, amount);
            Shield -= taken;
            return taken;
        }

        public void Tick(float deltaTime, float now)
        {
            if (Down || now - LastHitAt < RechargeDelay) return;
            Shield = Mathf.Min(MaxShield, Shield + RechargePerSecond * deltaTime);
        }

        /// <summary>Shields are restored between waves; being downed is not a run-ending state.</summary>
        public void Restore() => Shield = MaxShield;
    }
}
