using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WeaponRecipe
{
    public int version;
    public string id = "drawing", displayName = "Drawn weapon";
    public float cooldown = 0.35f;
    public bool repeatWhileHeld = true;
    public WeaponPresentation presentation = new WeaponPresentation();
    public WeaponBehavior[] behaviors = Array.Empty<WeaponBehavior>();
    public void Normalize()
    {
        presentation ??= new WeaponPresentation(); behaviors ??= Array.Empty<WeaponBehavior>();
        // Migrate older saved recipes: activation is now universal, not model-selected.
        repeatWhileHeld = true;
    }
    public static bool TryParse(string json, out WeaponRecipe recipe, out string[] errors)
    {
        recipe = null;
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 32000) throw new ArgumentException("Missing or oversized recipe.");
            recipe = WeaponJson.FromJson<WeaponRecipe>(json);
            if (recipe == null) throw new ArgumentException("Missing recipe.");
            recipe.Normalize(); var list = new List<string>(); recipe.Validate(list); errors = list.ToArray();
            return errors.Length == 0;
        }
        catch (Exception e) { errors = new[] { e.Message }; return false; }
    }
    public void Validate(List<string> errors)
    {
        if (version != 2) errors.Add("This recipe uses the old weapon system. Reinterpret the drawing to create a version 2 weapon.");
        Check(cooldown,0.05f,10,"cooldown",errors);
        if (!WeaponVisuals.IsKnown(presentation.asset)) errors.Add("Unknown visual asset.");
        if (!ColorUtility.TryParseHtmlString(presentation.color,out _)) errors.Add("Invalid color.");
        Check(presentation.scale,0.05f,3,"scale",errors);
        Check(presentation.spin,-1440,1440,"spin",errors);
        Check(presentation.trailTime,0,2,"trail time",errors);
        Check(presentation.drawingForwardDegrees,0,360,"drawing direction",errors);
        if (behaviors.Length == 0) errors.Add("Choose at least one behavior.");
        var count = 0; ValidateBehaviors(behaviors,0,ref count,errors);
    }
    private static void ValidateBehaviors(WeaponBehavior[] items,int depth,ref int count,List<string> errors)
    {
        if (items == null) return;
        if (depth > 3 || items.Length > 16) { errors.Add("Behavior graph too large."); return; }
        foreach (var b in items)
        {
            if (++count > 16) { errors.Add("At most 16 behavior nodes."); return; }
            if (b == null || !WeaponPrimitiveIds.IsBehavior(b.type)) { errors.Add("Unknown behavior."); continue; }
            Check(b.damage,0,200,"damage",errors); Check(b.range,0.1f,100,"range",errors);
            Check(b.radius,0.01f,10,"radius",errors); Check(b.speed,0.1f,100,"speed",errors);
            Check(b.lifetime,0.1f,30,"lifetime",errors); Check(b.duration,0.1f,10,"duration",errors);
            Check(b.force,0,100,"force",errors); Check(b.arcDegrees,1,360,"arc",errors);
            if (b.type == "apply_status" && b.status != "burning" && b.status != "freezing" && b.status != "stunned") errors.Add("Unknown status.");
            if (b.type == "spawn_object" && b.objectType != "mine") errors.Add("Unknown spawned object.");
            if ((b.type == "apply_force" || b.type == "apply_status") && b.onHit != null && b.onHit.Length > 0) errors.Add("Force/status cannot have onHit children.");
            ValidateBehaviors(b.onHit,depth+1,ref count,errors);
        }
    }
    private static void Check(float value,float min,float max,string name,List<string> errors)
    { if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) errors.Add(name+" must be between "+min+" and "+max+"."); }
}

[Serializable]
public sealed class WeaponBehavior
{
    public string type = "projectile";
    public float damage = 15, range = 60, radius = 0.12f, speed = 28, lifetime = 4;
    public float force = 8, duration = 3, arcDegrees = 100;
    public string status = "burning", objectType = "mine";
    public WeaponBehavior[] onHit = Array.Empty<WeaponBehavior>();
}

[Serializable]
public sealed class WeaponPresentation
{
    public string asset = "bolt", color = "#68E8FF";
    public float scale = 1, spin, trailTime = 0.2f;
    public bool useDrawingAsProjectile;
    public float drawingForwardDegrees;
}

public static class WeaponPrimitiveIds
{
    public static readonly string[] All = { "projectile", "beam", "explosion", "spawn_object", "apply_force", "apply_status", "melee" };
    public static bool IsBehavior(string id) => Array.IndexOf(All,id) >= 0;
}
