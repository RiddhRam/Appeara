using System.Collections.Generic;
using UnityEngine;

namespace Armory.Core
{
    public enum EnemyKind { Grunt, Swarm, Armored, Fast, Shielded, Boss }

    /// <summary>Type matchups: the reason weapon invention matters.</summary>
    public static class DamageTable
    {
        public const float ResistFactor = 0.3f;
        public const float MinMultiplier = 0.1f;

        public static float Multiplier(EnemyKind kind, ParsedWeapon weapon, bool shieldUp, ICollection<string> resistances)
        {
            float m = TypeMultiplier(kind, weapon, shieldUp);
            if (resistances != null && resistances.Count > 0)
                foreach (var primitive in weapon.Primitives())
                    if (resistances.Contains(primitive)) m *= ResistFactor;
            return Mathf.Max(MinMultiplier, m);
        }

        public static float TypeMultiplier(EnemyKind kind, ParsedWeapon weapon, bool shieldUp)
        {
            // Shields soak everything except electricity until popped.
            if (shieldUp) return weapon.Payload == Payload.Electric ? 3f : 0.2f;

            switch (kind)
            {
                case EnemyKind.Swarm:
                    return weapon.Has(Mods.Splash) || weapon.Has(Mods.Chain) ? 1.5f
                        : weapon.Payload == Payload.Kinetic ? 0.75f : 1f;
                case EnemyKind.Armored:
                {
                    float m = ArmorPayloadFactor(weapon.Payload);
                    return weapon.Has(Mods.Piercing) ? Mathf.Max(m, 1f) * 1.6f : m;
                }
                case EnemyKind.Fast:
                    return weapon.Has(Mods.Homing) || weapon.Has(Mods.Slow) ? 1.5f : 1f;
                default:
                    return 1f;
            }
        }

        private static float ArmorPayloadFactor(Payload payload)
        {
            switch (payload)
            {
                case Payload.Kinetic: return 0.35f;
                case Payload.Plasma: return 0.6f;
                case Payload.Explosive: return 1.6f;
                default: return 0.8f;
            }
        }
    }
}
