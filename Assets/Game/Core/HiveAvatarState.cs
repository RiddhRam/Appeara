using UnityEngine;

namespace Armory.Core
{
    public enum HiveOrgan { LeftClaw, RightClaw, SporeSac, Crest }
    public enum HiveAttack { SweepLeft, SweepRight, Spores, Wreck }

    /// <summary>
    /// Everything the avatar can play in one beat, including the three authored attack clips.
    /// This is a second vocabulary next to <see cref="HiveAttack"/> because that enum is a strict one-organ-to-one
    /// -attack mapping by index, while the sac and the crest each power two beats: breaking the sac has to take
    /// away both the fireball and the spore pods, which the index cast cannot express.
    /// </summary>
    public enum HiveMove { Stomp, Fireball, Laser, ClawSweep, Spores, Wreck }

    /// <summary>
    /// The beat order and the organ that powers each beat, kept pure so the rotation and the organ gating are
    /// testable without an animator. Silencing a beat by shooting its organ is the whole fight, so it is the one
    /// piece of this encounter that must never depend on Unity timing.
    /// </summary>
    public static class HiveMoves
    {
        /// <summary>
        /// One full cycle. The authored attacks lead because they are what the player came to see; the two
        /// shootable launches break up the rhythm and give the player something to shoot between dodges.
        /// </summary>
        public static readonly HiveMove[] Rotation =
        {
            HiveMove.Stomp, HiveMove.Fireball, HiveMove.ClawSweep, HiveMove.Laser, HiveMove.Spores, HiveMove.Wreck,
        };

        public static HiveOrgan Organ(HiveMove move)
        {
            switch (move)
            {
                case HiveMove.Stomp: return HiveOrgan.LeftClaw;
                case HiveMove.ClawSweep: return HiveOrgan.RightClaw;
                case HiveMove.Fireball:
                case HiveMove.Spores: return HiveOrgan.SporeSac;
                default: return HiveOrgan.Crest;
            }
        }

        /// <summary>
        /// Picks the next playable beat from <paramref name="cursor"/>, skipping beats whose organ is gone and
        /// advancing the cursor past them. Returns false only when every organ is broken, which is the state a
        /// boss reaches moments before it dies; the caller idles instead of standing in a silent attack.
        /// </summary>
        public static bool TryNext(HiveAvatarState rules, ref int cursor, out HiveMove move)
        {
            for (int i = 0; i < Rotation.Length; i++)
            {
                move = Rotation[((cursor % Rotation.Length) + Rotation.Length) % Rotation.Length];
                cursor++;
                if (rules == null || rules.CanAttack(move)) return true;
            }
            move = HiveMove.Stomp;
            return false;
        }
    }

    /// <summary>
    /// What each beat costs the player's hundred-point shield, and how much room they have to dodge it.
    /// Sized against <see cref="PlayerVitals"/>: four landed beats inside one rotation put the marine down, so
    /// ignoring every telegraph loses the wave, while no single beat takes more than about a third of the bar and
    /// the four-second recharge delay hands a full bar back to anyone who dodges the next two.
    /// </summary>
    public static class HiveDamage
    {
        public const float Stomp = 32f;
        public const float Fireball = 28f;
        public const float ClawSweep = 22f;
        /// <summary>Per contact tick. The grace window in PlayerVitals caps the two-second sweep at four ticks.</summary>
        public const float LaserTick = 12f;

        public const float StompRadius = 6.5f;
        public const float FireballRadius = 4f;
        public const float ClawSweepRadius = 3.2f;
        /// <summary>Half-width of the beam's hit volume, kept under the drawn beam so it never hits what it misses.</summary>
        public const float LaserRadius = 1.4f;
        /// <summary>The ring drawn while the mouth charges. Wider than the beam because the sweep crosses it.</summary>
        public const float LaserWarnRadius = 6f;

        /// <summary>
        /// Shortest distance from a point to the beam, which is a segment from the mouth to the spot it burns on
        /// the deck and not an infinite ray: standing behind the boss, or past where the beam lands, is safe.
        /// </summary>
        public static float DistanceToBeam(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 along = to - from;
            float lengthSq = along.sqrMagnitude;
            if (lengthSq < 0.0001f) return Vector3.Distance(point, from);
            float t = Mathf.Clamp01(Vector3.Dot(point - from, along) / lengthSq);
            return Vector3.Distance(point, from + along * t);
        }

        public static float Of(HiveMove move)
        {
            switch (move)
            {
                case HiveMove.Stomp: return Stomp;
                case HiveMove.Fireball: return Fireball;
                case HiveMove.ClawSweep: return ClawSweep;
                case HiveMove.Laser: return LaserTick;
                default: return 0f;
            }
        }
    }

    /// <summary>Encounter rules, separate from animation and Unity timing.</summary>
    public sealed class HiveAvatarState
    {
        public const float OrganHealth = 180f;
        /// <summary>Health fraction at which the avatar enrages and picks up its pace.</summary>
        public const float EnrageFraction = 0.35f;
        public string Plating { get; private set; }
        public int BrokenCount { get; private set; }
        private readonly float[] health = { OrganHealth, OrganHealth, OrganHealth, OrganHealth };
        private bool enraged;
        public bool IsBroken(HiveOrgan organ) => health[(int)organ] <= 0f;
        public bool HitOrgan(HiveOrgan organ, float damage)
        {
            if (IsBroken(organ) || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return false;
            health[(int)organ] = System.Math.Max(0f, health[(int)organ] - damage);
            if (!IsBroken(organ)) return false;
            BrokenCount++;
            return true;
        }
        public bool CanAttack(HiveAttack attack) => !IsBroken((HiveOrgan)attack);
        public bool CanAttack(HiveMove move) => !IsBroken(HiveMoves.Organ(move));

        /// <summary>
        /// True on the first call after health crosses the threshold and never again. The encounter polls this
        /// once per beat, and the Enrage trigger must fire exactly once: re-firing it would restart the roar and
        /// cut whatever attack was already running.
        /// </summary>
        public bool ShouldEnrage(float health, float maxHealth)
        {
            if (enraged || maxHealth <= 0f || health > maxHealth * EnrageFraction) return false;
            enraged = true;
            return true;
        }

        public void Adapt(string primitive)
        {
            if (AdaptationRules.IsPrimitive(primitive)) Plating = primitive;
        }
        public float DamageScale(ParsedWeapon weapon)
        {
            if (weapon != null && Plating != null)
                foreach (string primitive in weapon.Primitives())
                    if (primitive == Plating) return DamageTable.ResistFactor;
            return 1f;
        }
    }
}
