using UnityEngine;

/// <summary>Inspectable example JSON and a minimal way to exercise the runtime system.</summary>
public sealed class WeaponRecipeExamples : MonoBehaviour
{
    [TextArea(8, 20)] [SerializeField] private string recipeJson = LaserSword;
    [SerializeField] private Transform firePoint;
    private WeaponRuntime weapon;

    public const string LaserSword = "{\"id\":\"laser_sword\",\"displayName\":\"Laser Sword\",\"delivery\":{\"primitive\":\"melee_arc\",\"range\":2.2,\"arcDegrees\":120,\"cooldown\":0.35},\"payloads\":[{\"primitive\":\"damage\",\"magnitude\":24},{\"primitive\":\"ignite\",\"magnitude\":4,\"duration\":2}]}";
    public const string PlasmaGrenade = "{\"id\":\"plasma_grenade\",\"displayName\":\"Plasma Grenade\",\"delivery\":{\"primitive\":\"thrown_projectile\",\"range\":20,\"radius\":3,\"speed\":14,\"lifetime\":2,\"cooldown\":1},\"payloads\":[{\"primitive\":\"damage\",\"magnitude\":60}],\"modifiers\":[{\"primitive\":\"explode_on_impact\",\"value\":3}]}";

    private void Awake()
    {
        if (!WeaponComposer.TryCompose(recipeJson, transform, out weapon, out var errors))
            Debug.LogError(string.Join("\n", errors), this);
    }

    private void Update()
    {
        if (weapon != null && Input.GetMouseButtonDown(0))
        {
            var origin = firePoint != null ? firePoint.position : transform.position;
            var direction = firePoint != null ? firePoint.forward : transform.forward;
            weapon.TryUse(origin, direction);
        }
    }
}
