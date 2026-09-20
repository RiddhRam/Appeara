using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Small pre-generated silhouette library. Known firearm/support archetypes skip the slow image endpoint while
    /// unfamiliar concepts (bows, melee weapons, genuinely novel props) still get bespoke image generation.
    /// </summary>
    public static class WeaponBlueprintCatalog
    {
        private const string ResourceRoot = "WeaponBlueprints/";

        public static bool TryLoad(ParsedWeapon weapon, out byte[] png, out string archetype)
        {
            archetype = FindClosest(weapon);
            if (archetype == null)
            {
                png = null;
                return false;
            }

            var asset = Resources.Load<TextAsset>(ResourceRoot + archetype);
            if (asset == null || asset.bytes == null || asset.bytes.Length == 0)
            {
                Debug.LogWarning("Blueprint catalog asset missing: " + archetype);
                png = null;
                return false;
            }

            png = asset.bytes;
            return true;
        }

        /// <summary>Deterministic and intentionally conservative: null means the online generator should run.</summary>
        public static string FindClosest(ParsedWeapon weapon)
        {
            if (weapon == null) return null;
            string description = ((weapon.DesignPrompt ?? "") + " " + (weapon.Name ?? "")).ToLowerInvariant();

            // More specific compound archetypes must win before their shared words (launcher, rifle, gun).
            if (Has(description, "grenade launcher", "grenade gun", "rotary grenade")) return "grenade_launcher";
            if (Has(description, "rocket launcher", "missile launcher", "bazooka", "rpg")) return "rocket_launcher";
            if (Has(description, "submachine gun", "sub-machine gun", "machine pistol", " smg", "smg ")) return "smg";
            if (Has(description, "machine gun", "machinegun", "minigun", "gatling", "light machine gun", "heavy machine gun")) return "machine_gun";
            if (Has(description, "sniper", "marksman", "precision rifle", "long-range rifle")) return "sniper_rifle";
            if (Has(description, "shotgun", "scattergun", "scatter gun")) return "shotgun";
            if (Has(description, "assault rifle", "battle rifle", "pulse rifle", "carbine")) return "assault_rifle";
            if (Has(description, "pistol", "handgun", "sidearm", "revolver")) return "pistol";
            if (Has(description, "flamethrower", "flame thrower", "heat projector", "fire projector")) return "flamethrower";
            if (Has(description, "railgun", "rail gun", "coilgun", "coil gun", "magnetic rifle")) return "railgun";
            if (Has(description, "tesla", "arc cannon", "lightning gun", "electric cannon", "shock rifle")) return "tesla_cannon";

            // Trait-based matches cover prompts like "an explosive cannon" without stealing melee/bow/thrown ideas.
            if (weapon.FireMode == FireMode.Beam)
                return weapon.Payload == Payload.Electric ? "tesla_cannon" : "railgun";
            if (weapon.FireMode != FireMode.Projectile) return null;
            if (weapon.Payload == Payload.Explosive && Has(description, "launcher", "cannon", "rocket", "missile")) return "rocket_launcher";
            if (weapon.Payload == Payload.Electric && Has(description, "gun", "rifle", "cannon", "projector")) return "tesla_cannon";
            if (Has(description, "rifle")) return "assault_rifle";
            return null;
        }

        private static bool Has(string description, params string[] phrases)
        {
            foreach (string phrase in phrases)
                if (description.Contains(phrase)) return true;
            return false;
        }
    }
}
