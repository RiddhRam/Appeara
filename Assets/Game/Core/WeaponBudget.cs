using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Energy the station can spare to fabricate one weapon, and what a weapon costs to build. The parser already
    /// caps raw DPS, but nothing stopped a request being fast AND huge AND homing at once. The budget makes those
    /// compete, and Trim explains the cut in words so the player learns to ask for less rather than feeling robbed.
    /// </summary>
    public static class WeaponBudget
    {
        public const int Wave1Budget = 6;
        public const int Wave2Budget = 8;
        public const int Wave3Budget = 10;
        public const int Wave4Budget = 12;
        public const int Wave5Budget = 15;
        public const int BudgetStep = 3;

        public const int ElementCost = 2;     // anything but kinetic; kinetic is the free baseline
        public const int ModifierCost = 1;
        public const int HighBandCost = 2;
        public const int MidBandCost = 1;

        public const float HighFireRate = 8f;
        public const float MidFireRate = 4f;
        public const int HighProjectileCount = 6;
        public const int MidProjectileCount = 2;
        public const float HighDamage = 60f;
        public const float MidDamage = 30f;

        public const int BeamCost = 2;
        public const int BowCost = 1;
        public const int ThrownCost = 1;
        public const int MeleeCost = 0;
        public const int ProjectileCost = 0;

        public const float MinFireRate = 0.5f;
        public const int MinProjectileCount = 1;
        public const float MinDamage = 1f;

        /// <summary>Cheapest first: what the player misses least is what goes first.</summary>
        private static readonly Mods[] TrimOrder =
        {
            Mods.Slow, Mods.Chain, Mods.Splash, Mods.Bouncing, Mods.Proximity, Mods.Sticky, Mods.Piercing, Mods.Homing
        };

        /// <summary>Allowance for a wave. waveIndex is zero-based, like WaveDirector.WaveIndex.</summary>
        public static int Budget(int waveIndex)
        {
            if (waveIndex <= 0) return Wave1Budget;
            if (waveIndex == 1) return Wave2Budget;
            if (waveIndex == 2) return Wave3Budget;
            if (waveIndex == 3) return Wave4Budget;
            return Wave5Budget + (waveIndex - 4) * BudgetStep;
        }

        /// <summary>What this weapon draws from the wave allowance.</summary>
        public static int Cost(ParsedWeapon weapon)
        {
            if (weapon == null) return 0;
            return ElementBand(weapon.Payload)
                + ModifierCost * ModCount(weapon.Mods)
                + RateBand(weapon.FireRate)
                + CountBand(weapon.ProjectileCount)
                + DamageBand(weapon.Damage)
                + ModeBand(weapon.FireMode);
        }

        public static int ElementBand(Payload payload) => payload == Payload.Kinetic ? 0 : ElementCost;

        public static int RateBand(float fireRate) =>
            fireRate > HighFireRate ? HighBandCost : fireRate > MidFireRate ? MidBandCost : 0;

        public static int CountBand(int projectileCount) =>
            projectileCount > HighProjectileCount ? HighBandCost : projectileCount > MidProjectileCount ? MidBandCost : 0;

        // Rarely bites on its own: the parser's DPS cap already shrinks damage when rate and salvo are high.
        public static int DamageBand(float damage) =>
            damage > HighDamage ? HighBandCost : damage > MidDamage ? MidBandCost : 0;

        public static int ModeBand(FireMode mode)
        {
            switch (mode)
            {
                case FireMode.Beam: return BeamCost;
                case FireMode.Bow: return BowCost;
                case FireMode.Thrown: return ThrownCost;
                case FireMode.Melee: return MeleeCost;
                default: return ProjectileCost;
            }
        }

        /// <summary>
        /// Cuts the weapon down until it fits, in place. Modifiers go first, cheapest to dearest, then rate, salvo
        /// and damage drop one band per pass. Returns true when something was cut; report is then a player-facing
        /// sentence, otherwise empty.
        /// </summary>
        public static bool Trim(ParsedWeapon weapon, int budget, out string report)
        {
            report = "";
            if (weapon == null) return false;

            var dropped = new List<string>();
            bool cutRate = false, cutSalvo = false, cutDamage = false;
            while (Cost(weapon) > budget)
            {
                Mods cheapest = Cheapest(weapon);
                if (cheapest != Mods.None)
                {
                    weapon.Mods &= ~cheapest;
                    dropped.Add(cheapest.ToString().ToLowerInvariant());
                    continue;
                }
                // Nothing left to drop; a weapon that still overruns its budget is as small as it is allowed to get.
                if (!StepDown(weapon, ref cutRate, ref cutSalvo, ref cutDamage)) break;
            }

            report = Describe(dropped, cutRate, cutSalvo, cutDamage, budget);
            return report.Length > 0;
        }

        private static Mods Cheapest(ParsedWeapon weapon)
        {
            foreach (var mod in TrimOrder)
                if (weapon.Has(mod)) return mod;
            return Mods.None;
        }

        /// <summary>One band off each of rate, salvo and damage. False once all three sit in their free band.</summary>
        private static bool StepDown(ParsedWeapon weapon, ref bool cutRate, ref bool cutSalvo, ref bool cutDamage)
        {
            bool changed = false;

            float rate = Mathf.Max(MinFireRate, NextRate(weapon.FireRate));
            if (rate < weapon.FireRate) { weapon.FireRate = rate; cutRate = true; changed = true; }

            int count = Mathf.Max(MinProjectileCount, NextCount(weapon.ProjectileCount));
            if (count < weapon.ProjectileCount) { weapon.ProjectileCount = count; cutSalvo = true; changed = true; }

            float damage = Mathf.Max(MinDamage, NextDamage(weapon.Damage));
            if (damage < weapon.Damage) { weapon.Damage = damage; cutDamage = true; changed = true; }

            return changed;
        }

        private static float NextRate(float rate) =>
            rate > HighFireRate ? HighFireRate : rate > MidFireRate ? MidFireRate : rate;

        private static int NextCount(int count) =>
            count > HighProjectileCount ? HighProjectileCount : count > MidProjectileCount ? MidProjectileCount : count;

        private static float NextDamage(float damage) =>
            damage > HighDamage ? HighDamage : damage > MidDamage ? MidDamage : damage;

        private static string Describe(List<string> dropped, bool cutRate, bool cutSalvo, bool cutDamage, int budget)
        {
            var parts = new List<string>();
            if (dropped.Count > 0) parts.Add("dropped " + List(dropped));

            var trimmed = new List<string>();
            if (cutRate) trimmed.Add("rate");
            if (cutSalvo) trimmed.Add("salvo");
            if (cutDamage) trimmed.Add("damage");
            if (trimmed.Count > 0) parts.Add("trimmed " + List(trimmed));

            if (parts.Count == 0) return "";
            return string.Join(" and ", parts) + " to fit " + budget + " energy";
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
        private static string List(List<string> items)
        {
            if (items.Count == 1) return items[0];
            var text = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) text.Append(i == items.Count - 1 ? " and " : ", ");
                text.Append(items[i]);
            }
            return text.ToString();
        }

        private static int ModCount(Mods mods)
        {
            int count = 0;
            for (int value = (int)mods; value != 0; value &= value - 1) count++;
            return count;
        }
    }
}
