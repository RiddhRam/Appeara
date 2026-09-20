using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Debuffs an element leaves on an alien. Pure state + maths so the numbers are unit-tested and the enemy
    /// script only has to render them (tint) and apply the results.
    /// </summary>
    public sealed class StatusState
    {
        public const float BurnSeconds = 4f;
        public const float ChillSeconds = 3f;
        public const float ChillSpeed = 0.45f;
        public const float BrittleSeconds = 4f;
        public const float BrittleDamage = 1.25f;
        public const float StunSeconds = 0.7f;
        public const float MeltSeconds = 4f;
        public const float MeltDamage = 1.3f;

        private float burnUntil, burnDps, chillUntil, brittleUntil, stunUntil, meltUntil;

        public bool Burning(float now) => now < burnUntil;
        public bool Chilled(float now) => now < chillUntil;
        public bool Stunned(float now) => now < stunUntil;
        public bool Melting(float now) => now < meltUntil;
        public bool Brittle(float now) => now < brittleUntil;
        public bool Any(float now) => Burning(now) || Chilled(now) || Stunned(now) || Melting(now) || Brittle(now);

        /// <summary>Applies the debuff that belongs to a payload. Damage is the hit that caused it.</summary>
        public void Apply(Payload payload, float damage, float now)
        {
            switch (payload)
            {
                case Payload.Plasma:
                    burnUntil = now + BurnSeconds;
                    burnDps = Mathf.Max(burnDps, Mathf.Max(1f, damage * 0.18f));
                    meltUntil = now + MeltSeconds;
                    break;
                case Payload.Electric:
                    stunUntil = Mathf.Max(stunUntil, now + StunSeconds);
                    break;
                case Payload.Cryo:
                    chillUntil = now + ChillSeconds;
                    brittleUntil = now + BrittleSeconds;
                    break;
                case Payload.Explosive:
                    // Concussive: a short stagger, no lingering damage.
                    stunUntil = Mathf.Max(stunUntil, now + StunSeconds * 0.35f);
                    break;
            }
        }

        public void ApplySlow(float now)
        {
            chillUntil = Mathf.Max(chillUntil, now + ChillSeconds);
        }

        /// <summary>Speed multiplier from chill and stun.</summary>
        public float SpeedMultiplier(float now)
        {
            if (Stunned(now)) return 0f;
            return Chilled(now) ? ChillSpeed : 1f;
        }

        /// <summary>Extra damage taken while brittle (cryo) or melting (plasma); they stack multiplicatively.</summary>
        public float DamageTakenMultiplier(float now)
        {
            float multiplier = 1f;
            if (Brittle(now)) multiplier *= BrittleDamage;
            if (Melting(now)) multiplier *= MeltDamage;
            return multiplier;
        }

        /// <summary>Burn damage for this frame.</summary>
        public float BurnDamage(float now, float deltaTime) => Burning(now) ? burnDps * deltaTime : 0f;

        /// <summary>Tint so the player can read the debuff at a glance; null when nothing is active.</summary>
        public Color? Tint(float now)
        {
            if (Stunned(now)) return new Color(1f, 0.95f, 0.35f);   // electric: yellow
            if (Burning(now)) return new Color(1f, 0.45f, 0.12f);   // plasma/fire: orange
            if (Chilled(now)) return new Color(0.45f, 0.8f, 1f);    // cryo: blue
            if (Melting(now)) return new Color(0.85f, 0.35f, 1f);   // plasma afterburn: violet
            return null;
        }

        public void Clear()
        {
            burnUntil = chillUntil = brittleUntil = stunUntil = meltUntil = 0f;
            burnDps = 0f;
        }
    }
}
