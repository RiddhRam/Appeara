using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Maths for the two archetypes the player performs with their arm rather than the trigger: swinging a blade
    /// and drawing a bow. Pure so the feel can be tuned and tested without the headset.
    /// </summary>
    public static class SwingAndDraw
    {
        // Melee
        /// <summary>Below this the controller is being carried, not swung.</summary>
        public const float SwingStart = 1.6f;
        /// <summary>A swing at or above this speed does full damage.</summary>
        public const float SwingFull = 4.5f;
        public const float WeakSwing = 0.35f;
        /// <summary>Blade reach measured from the grip, in metres.</summary>
        public const float Reach = 1.35f;
        public const float SwingCooldown = 0.35f;

        // Bow
        public const float FullDrawSeconds = 0.9f;
        public const float MinDrawRelease = 0.15f;
        public const float MinDrawPower = 0.35f;
        public const float FullDrawDamage = 2.6f;
        public const float FullDrawSpeed = 1.5f;

        /// <summary>0 when the controller is barely moving, 1 at a committed swing.</summary>
        public static float SwingStrength(float controllerSpeed)
        {
            if (controllerSpeed < SwingStart) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(SwingStart, SwingFull, controllerSpeed));
        }

        /// <summary>Damage for a swing: a slow poke does little, a committed slash does full damage.</summary>
        public static float SwingDamage(float baseDamage, float controllerSpeed)
        {
            float strength = SwingStrength(controllerSpeed);
            return strength <= 0f ? 0f : baseDamage * Mathf.Lerp(WeakSwing, 1f, strength);
        }

        /// <summary>How far back the bow is drawn after holding the trigger for this long, 0-1.</summary>
        public static float DrawPower(float heldSeconds) => Mathf.Clamp01(heldSeconds / FullDrawSeconds);

        /// <summary>A twitch on the trigger should not loose an arrow.</summary>
        public static bool CanRelease(float heldSeconds) => heldSeconds >= MinDrawRelease;

        /// <summary>Damage multiplier from the draw: a half pull is weak, a full pull hits hard.</summary>
        public static float DrawDamageMultiplier(float heldSeconds) =>
            Mathf.Lerp(MinDrawPower, FullDrawDamage, DrawPower(heldSeconds));

        /// <summary>Arrow speed multiplier from the draw.</summary>
        public static float DrawSpeedMultiplier(float heldSeconds) =>
            Mathf.Lerp(0.5f, FullDrawSpeed, DrawPower(heldSeconds));
    }
}
