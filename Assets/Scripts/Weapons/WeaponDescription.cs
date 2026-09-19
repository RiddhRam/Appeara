using System.Collections.Generic;

/// <summary>Manual fallback presets. No recognition or generated code.</summary>
public static class WeaponDescription
{
    public static WeaponRecipe Interpret(string description)
    {
        var text=description.ToLowerInvariant();
        var melee=(text.Contains("knife") || text.Contains("sword")) && !text.Contains("shoot") && !text.Contains("gun");
        var type=text.Contains("mine") ? "spawn_object" : text.Contains("force") ? "apply_force" : text.Contains("status") ? "apply_status" : melee ? "melee" : text.Contains("laser") ? "beam" : text.Contains("explosion") ? "explosion" : "projectile";
        var b=new WeaponBehavior { type=type, range=melee ? 3 : 60, damage=melee ? 35 : 15 };
        if(type=="explosion") b.radius=3;
        if(type=="spawn_object") { b.radius=2; b.lifetime=20; b.damage=0; b.onHit=new[] { new WeaponBehavior { type="explosion",radius=3,damage=40 } }; }
        if(text.Contains("grenade")) { b.damage=0; b.onHit=new[] { new WeaponBehavior { type="explosion",radius=3,damage=40 } }; }
        if(text.Contains("flaming")) b.onHit=new[] { new WeaponBehavior { type="apply_status",status="burning",damage=5 } };
        return new WeaponRecipe { version=2,displayName=description,behaviors=new[] { b },presentation=new WeaponPresentation {
            useDrawingAsProjectile=text.Contains("drawn projectile") || text.Contains("grenade") || (text.Contains("knives") && !melee), drawingForwardDegrees=melee ? 90 : 0
        }};
    }
}
