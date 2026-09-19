using UnityEngine;

/// <summary>
/// Entry point for AI-authored weapons. Feed it validated JSON at runtime to build
/// a fully usable WeaponRuntime; no weapon-specific prefab or script is required.
/// </summary>
public static class WeaponComposer
{
    public static bool TryCompose(string recipeJson, Transform parent, out WeaponRuntime weapon, out string[] errors)
    {
        weapon = null;
        if (!WeaponRecipe.TryParse(recipeJson, out var recipe, out errors))
            return false;

        weapon = Compose(recipe, parent);
        return true;
    }

    public static WeaponRuntime Compose(WeaponRecipe recipe, Transform parent = null)
    {
        if (recipe == null) throw new System.ArgumentNullException(nameof(recipe));
        recipe.Normalize();
        var errors = new System.Collections.Generic.List<string>();
        recipe.Validate(errors);
        if (errors.Count > 0) throw new System.ArgumentException(string.Join("\n", errors));
        var objectName = "Weapon - " + recipe.displayName;
        var weaponObject = new GameObject(objectName);
        if (parent != null) weaponObject.transform.SetParent(parent, false);
        var weapon = weaponObject.AddComponent<WeaponRuntime>();
        weapon.Initialize(recipe);
        return weapon;
    }
}
