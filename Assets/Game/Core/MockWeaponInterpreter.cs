using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Armory.Core
{
    /// <summary>Keyword interpreter: offline mode, API fallback, and desktop quick-test prompts.</summary>
    public static class MockWeaponInterpreter
    {
        public static WeaponSpec Interpret(string request)
        {
            string text = (request ?? "").ToLowerInvariant();
            var modifiers = new List<string>();
            var onHit = new List<string>();
            var spec = new WeaponSpec
            {
                name = MakeName(request),
                fireMode = "projectile",
                payload = "kinetic",
                fireRate = 6f,
                projectileCount = 1,
                projectileSpeed = 45f,
                damage = 12f,
                visual = new WeaponVisual { body = Mathf.Abs(text.GetHashCode()) % 6, barrel = Mathf.Abs(text.Length * 7) % 6, primaryColor = "#33E6FF", projectileShape = "orb", trail = true },
            };

            if (Has(text, "shotgun", "scatter", "spread")) { spec.projectileCount = 8; spec.spreadDeg = 16f; spec.fireRate = 1.5f; }
            if (Has(text, "machine", "minigun", "rapid", "gatling", "smg")) { spec.fireRate = 12f; spec.damage = 8f; }
            if (Has(text, "rail", "sniper")) { spec.projectileSpeed = 80f; spec.damage = 60f; spec.fireRate = 1f; modifiers.Add("piercing"); spec.visual.projectileShape = "bolt"; }
            if (Has(text, "laser", "beam", "ray")) { spec.fireMode = "beam"; spec.payload = "plasma"; spec.damage = 40f; }
            if (Has(text, "sword", "katana", "blade", "axe", "hammer", "machete", "claw", "whip", "slash", "melee"))
            { spec.fireMode = "melee"; spec.damage = 55f; spec.fireRate = 2.5f; spec.visual.projectileShape = "bolt"; }
            if (Has(text, "bow", "crossbow", "arrow", "sling", "charge"))
            { spec.fireMode = "bow"; spec.damage = 45f; spec.fireRate = 1.2f; spec.projectileSpeed = 60f; spec.visual.projectileShape = "bolt"; }
            if (Has(text, "grenade", "throw", "lob", "toss")) { spec.fireMode = "thrown"; spec.payload = "explosive"; spec.fireRate = 1.5f; spec.damage = 40f; }
            if (Has(text, "mine", "sticky", "sticks")) { modifiers.Add("sticky"); modifiers.Add("proximity"); spec.payload = "explosive"; spec.visual.projectileShape = "mine"; }
            if (Has(text, "homing", "seeking", "seeker", "missile", "rocket", "tracking")) modifiers.Add("homing");
            if (Has(text, "missile", "rocket", "explo", "boom", "bomb")) spec.payload = "explosive";
            if (Has(text, "bounc", "ricochet")) modifiers.Add("bouncing");
            if (Has(text, "pierc", "penetrat", "through")) modifiers.Add("piercing");
            if (Has(text, "plasma")) spec.payload = "plasma";
            if (Has(text, "electric", "lightning", "tesla", "emp", "shock", "zap")) spec.payload = "electric";
            if (Has(text, "freez", "ice", "cryo", "frost", "cold")) spec.payload = "cryo";
            if (Has(text, "chain", "arc")) onHit.Add("chain");
            if (Has(text, "slow")) onHit.Add("slow");

            spec.visual.primaryColor = ColorFor(spec.payload);
            spec.modifiers = modifiers.ToArray();
            spec.onHit = onHit.ToArray();
            spec.shipAILine = "Offline fabrication complete: " + spec.name + ".";
            spec.sfxPrompt = spec.payload + " sci-fi weapon shot, short punchy";
            return spec;
        }

        public static string InterpretJson(string request) => JsonUtility.ToJson(Interpret(request));

        /// <summary>Word-start match so "ice" doesn't fire on "nice" but "explo" still hits "explosive".</summary>
        private static bool Has(string text, params string[] words)
        {
            foreach (var word in words)
                if (Regex.IsMatch(text, @"\b" + word)) return true;
            return false;
        }

        private static string ColorFor(string payload)
        {
            switch (payload)
            {
                case "explosive": return "#FF7A1A";
                case "plasma": return "#B04DFF";
                case "electric": return "#4DA6FF";
                case "cryo": return "#9FF4FF";
                default: return "#E8E8E8";
            }
        }

        private static string MakeName(string request)
        {
            if (string.IsNullOrWhiteSpace(request)) return "Standard Issue Blaster";
            string[] words = request.Trim().Split(' ');
            string joined = string.Join(" ", words, 0, Mathf.Min(words.Length, 5));
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(joined.ToLowerInvariant());
        }
    }
}
