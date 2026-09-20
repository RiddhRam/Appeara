using System;
using UnityEngine;

namespace Armory.Core
{
    /// <summary>Turns untrusted LLM JSON into a clamped, budgeted weapon. Returns null only for unparseable JSON.</summary>
    public static class WeaponSpecParser
    {
        public const float MaxDps = 140f;
        public const float ModifierTax = 0.85f;
        public const int PartVariants = 6;

        public static ParsedWeapon Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            WeaponSpec spec;
            try { spec = JsonUtility.FromJson<WeaponSpec>(json); }
            catch (ArgumentException) { return null; }
            return spec == null ? null : FromSpec(spec);
        }

        public static ParsedWeapon FromSpec(WeaponSpec spec)
        {
            var weapon = new ParsedWeapon
            {
                Name = string.IsNullOrWhiteSpace(spec.name) ? "Unnamed Contraption" : Truncate(spec.name.Trim(), 48),
                ShipAILine = Truncate(spec.shipAILine?.Trim() ?? "", 160),
                FireMode = ParseEnum(spec.fireMode, FireMode.Projectile),
                Payload = ParseEnum(spec.payload, Payload.Kinetic),
                SfxPrompt = string.IsNullOrWhiteSpace(spec.sfxPrompt) ? null : Truncate(spec.sfxPrompt.Trim(), 200),
            };
            weapon.Mods = ParseMods(spec.modifiers) | ParseMods(spec.onHit);
            int requestedMods = CountBits((int)weapon.Mods);
            // Explosions always splash; electricity always arcs. Keeps behaviour matching the words.
            if (weapon.Payload == Payload.Explosive) weapon.Mods |= Mods.Splash;
            if (weapon.Payload == Payload.Electric) weapon.Mods |= Mods.Chain;
            if (weapon.Payload == Payload.Cryo) weapon.Mods |= Mods.Slow;

            bool thrown = weapon.FireMode == FireMode.Thrown;
            bool melee = weapon.FireMode == FireMode.Melee;
            bool bow = weapon.FireMode == FireMode.Bow;
            // Swings and draws set their own pace: a sword is limited by the arm, a bow by the draw.
            weapon.FireRate = melee ? Mathf.Clamp(Default(spec.fireRate, 2.5f), 1f, 4f)
                : bow ? Mathf.Clamp(Default(spec.fireRate, 1.2f), 0.4f, 2.5f)
                : Mathf.Clamp(Default(spec.fireRate, thrown ? 1.5f : 6f), 0.5f, 15f);
            weapon.ProjectileCount = melee ? 1 : Mathf.Clamp(spec.projectileCount <= 0 ? 1 : spec.projectileCount, 1, 12);
            weapon.SpreadDeg = Mathf.Clamp(spec.spreadDeg, 0f, 45f);
            if (weapon.ProjectileCount > 1 && weapon.SpreadDeg < 4f) weapon.SpreadDeg = 12f;
            weapon.ProjectileSpeed = thrown
                ? Mathf.Clamp(Default(spec.projectileSpeed, 14f), 6f, 25f)
                : bow ? Mathf.Clamp(Default(spec.projectileSpeed, 55f), 20f, 90f)
                : Mathf.Clamp(Default(spec.projectileSpeed, 40f), 5f, 80f);
            weapon.PierceCount = Mathf.Clamp(Default(spec.pierceCount, 3), 1, 5);
            weapon.BounceCount = Mathf.Clamp(Default(spec.bounceCount, 3), 1, 5);
            weapon.ChainCount = Mathf.Clamp(Default(spec.chainCount, 3), 1, 5);
            weapon.SplashRadius = Mathf.Clamp(Default(spec.splashRadius, 3f), 1f, 6f);
            weapon.Damage = Mathf.Clamp(Default(spec.damage, 12f), 1f, 100f);
            ApplyBudget(weapon, requestedMods);

            var visual = spec.visual ?? new WeaponVisual();
            weapon.Body = Mod(visual.body, PartVariants);
            weapon.Barrel = Mod(visual.barrel, PartVariants);
            weapon.Color = ParseColor(visual.primaryColor);
            weapon.Shape = ParseEnum(visual.projectileShape, weapon.Has(Mods.Sticky) ? ProjectileShape.Mine : ProjectileShape.Orb);
            weapon.Trail = visual.trail;
            return weapon;
        }

        /// <summary>Caps total damage output and taxes stacked modifiers so no request yields a god-weapon.</summary>
        private static void ApplyBudget(ParsedWeapon weapon, int modCount)
        {
            if (modCount > 1) weapon.Damage *= Mathf.Pow(ModifierTax, modCount - 1);
            float shotsPerSecond = weapon.FireMode == FireMode.Beam ? 1f : weapon.FireRate * weapon.ProjectileCount;
            float dps = weapon.Damage * shotsPerSecond;
            if (dps > MaxDps) weapon.Damage *= MaxDps / dps;
            weapon.Damage = Mathf.Max(0.5f, weapon.Damage);
        }

        public static Mods ParseMods(string[] values)
        {
            var mods = Mods.None;
            if (values == null) return mods;
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out Mods parsed) && parsed != Mods.None && Enum.IsDefined(typeof(Mods), parsed))
                    mods |= parsed;
            return mods;
        }

        public static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return Enum.TryParse(value.Trim(), true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : fallback;
        }

        public static Color ParseColor(string hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                string value = hex.Trim();
                if (!value.StartsWith("#")) value = "#" + value;
                if (ColorUtility.TryParseHtmlString(value, out var color)) return color;
            }
            return new Color(0.2f, 0.9f, 1f);
        }

        private static float Default(float value, float fallback) => value > 0f ? value : fallback;
        private static int Default(int value, int fallback) => value > 0 ? value : fallback;
        private static int Mod(int value, int count) => ((value % count) + count) % count;
        private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length);

        private static int CountBits(int value)
        {
            int count = 0;
            for (; value != 0; value &= value - 1) count++;
            return count;
        }
    }
}
