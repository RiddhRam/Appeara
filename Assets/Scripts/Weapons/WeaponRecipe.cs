using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A JSON-serializable description of a weapon. An LLM only needs to choose from
/// the primitive ids in <see cref="WeaponPrimitiveIds"/>; this object can then be
/// composed at runtime without creating a new prefab or C# type.
/// </summary>
[Serializable]
public sealed class WeaponRecipe
{
    public string id = "unnamed_weapon";
    public string displayName = "Unnamed Weapon";
    // press or hold. Input ownership remains with the caller of WeaponRuntime.TryUse.
    public string trigger = WeaponPrimitiveIds.TriggerPress;
    public WeaponPresentation presentation = new WeaponPresentation();
    public WeaponDelivery delivery = new WeaponDelivery();
    public WeaponPayload[] payloads = { new WeaponPayload() };
    public WeaponModifier[] modifiers = Array.Empty<WeaponModifier>();

    /// <summary>Deserializes an LLM-produced recipe and returns readable validation failures.</summary>
    public static bool TryParse(string json, out WeaponRecipe recipe, out string[] errors)
    {
        recipe = null;
        var validation = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = new[] { "Weapon JSON is empty." };
            return false;
        }

        try
        {
            recipe = JsonUtility.FromJson<WeaponRecipe>(json);
        }
        catch (ArgumentException exception)
        {
            errors = new[] { "Weapon JSON could not be read: " + exception.Message };
            return false;
        }

        if (recipe == null)
        {
            errors = new[] { "Weapon JSON did not produce a recipe." };
            return false;
        }

        recipe.Normalize();
        recipe.Validate(validation);
        errors = validation.ToArray();
        return errors.Length == 0;
    }

    public void Normalize()
    {
        id = string.IsNullOrWhiteSpace(id) ? "unnamed_weapon" : id.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        delivery ??= new WeaponDelivery();
        payloads ??= Array.Empty<WeaponPayload>();
        modifiers ??= Array.Empty<WeaponModifier>();
        presentation ??= new WeaponPresentation();
    }

    public void Validate(List<string> errors)
    {
        if (!WeaponVisuals.IsKnown(presentation.asset)) errors.Add("Unknown visual asset: " + presentation.asset);
        Check(presentation.scale, 0.05f, 3, "visual scale", errors);
        Check(presentation.spin, -1440, 1440, "spin", errors);
        Check(presentation.trailTime, 0, 2, "trail lifetime", errors);
        if (!ColorUtility.TryParseHtmlString(presentation.color, out _)) errors.Add("Invalid HTML color.");
        Check(delivery.range, 0.1f, 100, "range", errors);
        Check(delivery.cooldown, 0.05f, 10, "cooldown", errors);
        Check(delivery.radius, 0.01f, 5, "radius", errors);
        Check(delivery.speed, 0.1f, 100, "speed", errors);
        Check(delivery.lifetime, 0.1f, 10, "lifetime", errors);
        Check(delivery.arcDegrees, 1, 360, "arc", errors);
        if (payloads.Length > 8 || modifiers.Length > 5) errors.Add("Too many primitives.");
        if (!WeaponPrimitiveIds.IsTrigger(trigger))
            errors.Add("Unknown trigger primitive '" + trigger + "'.");
        if (!WeaponPrimitiveIds.IsDelivery(delivery.primitive))
            errors.Add("Unknown delivery primitive '" + delivery.primitive + "'.");
        if (delivery.range <= 0f)
            errors.Add("Delivery range must be greater than zero.");
        if (delivery.cooldown < 0f)
            errors.Add("Delivery cooldown cannot be negative.");
        if (payloads.Length == 0)
            errors.Add("A weapon needs at least one payload primitive.");

        for (var i = 0; i < payloads.Length; i++)
        {
            if (payloads[i] == null || !WeaponPrimitiveIds.IsPayload(payloads[i].primitive))
                errors.Add("Unknown payload primitive at index " + i + ".");
            else { Check(payloads[i].magnitude, 0, 200, "payload magnitude", errors); Check(payloads[i].duration, 0, 10, "duration", errors); }
        }

        for (var i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i] == null || !WeaponPrimitiveIds.IsModifier(modifiers[i].primitive))
                errors.Add("Unknown modifier primitive at index " + i + ".");
            else Check(modifiers[i].value, 0, 8, "modifier value", errors);
        }
    }

    private static void Check(float value, float min, float max, string name, List<string> errors)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            errors.Add(name + " must be between " + min + " and " + max + ".");
    }
}

[Serializable]
public sealed class WeaponPresentation
{
    public string asset = "bolt";
    public string color = "#68E8FF";
    public float scale = 1;
    public float spin;
    public float trailTime = 0.2f;
}

[Serializable]
public sealed class WeaponDelivery
{
    // melee_arc, hitscan, projectile, thrown_projectile, beam, or area_pulse
    public string primitive = WeaponPrimitiveIds.DeliveryMeleeArc;
    [Min(0.01f)] public float range = 2f;
    [Min(0f)] public float cooldown = 0.5f;
    [Range(1f, 360f)] public float arcDegrees = 100f;
    [Min(0.01f)] public float radius = 0.5f;
    [Min(0.01f)] public float speed = 25f;
    [Min(0f)] public float lifetime = 3f;
    public LayerMask hitLayers = ~0;
}

[Serializable]
public sealed class WeaponPayload
{
    // damage, knockback, ignite, slow, stun, or heal
    public string primitive = WeaponPrimitiveIds.PayloadDamage;
    public float magnitude = 10f;
    [Min(0f)] public float duration;
}

[Serializable]
public sealed class WeaponModifier
{
    // multishot, pierce, bounce, explode_on_impact, or homing
    public string primitive;
    public float value = 1f;
}

/// <summary>Stable strings that an external authoring model may safely emit.</summary>
public static class WeaponPrimitiveIds
{
    public const string TriggerPress = "press";
    public const string TriggerHold = "hold";

    public const string DeliveryMeleeArc = "melee_arc";
    public const string DeliveryHitscan = "hitscan";
    public const string DeliveryProjectile = "projectile";
    public const string DeliveryThrownProjectile = "thrown_projectile";
    public const string DeliveryBeam = "beam";
    public const string DeliveryAreaPulse = "area_pulse";

    public const string PayloadDamage = "damage";
    public const string PayloadKnockback = "knockback";
    public const string PayloadIgnite = "ignite";
    public const string PayloadSlow = "slow";
    public const string PayloadStun = "stun";
    public const string PayloadHeal = "heal";

    public const string ModifierMultishot = "multishot";
    public const string ModifierPierce = "pierce";
    public const string ModifierBounce = "bounce";
    public const string ModifierExplodeOnImpact = "explode_on_impact";
    public const string ModifierHoming = "homing";

    public static bool IsDelivery(string id) => id == DeliveryMeleeArc || id == DeliveryHitscan ||
        id == DeliveryProjectile || id == DeliveryThrownProjectile || id == DeliveryBeam || id == DeliveryAreaPulse;
    public static bool IsTrigger(string id) => id == TriggerPress || id == TriggerHold;
    public static bool IsPayload(string id) => id == PayloadDamage || id == PayloadKnockback || id == PayloadIgnite ||
        id == PayloadSlow || id == PayloadStun || id == PayloadHeal;
    public static bool IsModifier(string id) => id == ModifierMultishot || id == ModifierPierce ||
        id == ModifierBounce || id == ModifierExplodeOnImpact || id == ModifierHoming;
}
