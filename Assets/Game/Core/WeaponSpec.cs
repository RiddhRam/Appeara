using System;
using System.Collections.Generic;
using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// How the weapon is used, which decides what the player physically does: pull the trigger (gun), hold it
    /// (beam), lob it (grenade), swing the controller (sword) or draw and release (bow).
    /// </summary>
    public enum FireMode { Projectile, Beam, Thrown, Melee, Bow }
    public enum Payload { Kinetic, Explosive, Plasma, Electric, Cryo }
    public enum ProjectileShape { Orb, Bolt, Disc, Mine }

    /// <summary>Delivery modifiers and on-hit effects share one flag set so logging and damage rules stay simple.</summary>
    [Flags]
    public enum Mods
    {
        None = 0,
        Homing = 1 << 0,
        Piercing = 1 << 1,
        Bouncing = 1 << 2,
        Sticky = 1 << 3,
        Proximity = 1 << 4,
        Splash = 1 << 5,
        Chain = 1 << 6,
        Slow = 1 << 7,
    }

    /// <summary>Raw LLM output. Strings on purpose: unknown values must degrade, not throw.</summary>
    [Serializable]
    public class WeaponSpec
    {
        public string name;
        public string shipAILine;
        public string fireMode;
        public string payload;
        public string[] modifiers;
        public string[] onHit;
        public float fireRate;
        public int projectileCount;
        public float spreadDeg;
        public float projectileSpeed;
        public float damage;
        public int pierceCount;
        public int bounceCount;
        public int chainCount;
        public float splashRadius;
        public WeaponVisual visual;
        public string sfxPrompt;
    }

    [Serializable]
    public class WeaponVisual
    {
        public int body;
        public int barrel;
        public string primaryColor;
        public string projectileShape;
        public bool trail;
    }

    /// <summary>Validated, clamped weapon the game systems consume.</summary>
    public sealed class ParsedWeapon
    {
        public string Name;
        /// <summary>Original player wording retained for visual art direction; never part of the model's JSON.</summary>
        public string DesignPrompt;
        public string ShipAILine;
        public FireMode FireMode;
        public Payload Payload;
        public Mods Mods;
        public float FireRate;
        public int ProjectileCount;
        public float SpreadDeg;
        public float ProjectileSpeed;
        public float Damage;
        public int PierceCount;
        public int BounceCount;
        public int ChainCount;
        public float SplashRadius;
        public int Body;
        public int Barrel;
        public Color Color;
        public ProjectileShape Shape;
        public bool Trail;
        public string SfxPrompt;

        public bool Has(Mods mod) => (Mods & mod) != 0;

        /// <summary>Primitive keys used by the combat log, resistances and the mothership.</summary>
        public IEnumerable<string> Primitives()
        {
            yield return Payload.ToString().ToLowerInvariant();
            if (FireMode != FireMode.Projectile) yield return FireMode.ToString().ToLowerInvariant();
            foreach (Mods mod in Enum.GetValues(typeof(Mods)))
                if (mod != Mods.None && Has(mod)) yield return mod.ToString().ToLowerInvariant();
        }
    }
}
