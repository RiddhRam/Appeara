using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime weapon assembled from a <see cref="WeaponRecipe"/>.</summary>
public sealed class WeaponRuntime : MonoBehaviour
{
    public WeaponRecipe Recipe { get; private set; }
    public event Action<WeaponHitContext> Hit;

    private float nextUseTime;

    public void Initialize(WeaponRecipe recipe)
    {
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        Recipe.Normalize();
    }

    /// <summary>Uses this weapon from the supplied world-space origin and direction.</summary>
    public bool TryUse(Vector3 origin, Vector3 direction)
    {
        if (Recipe == null || Time.time < nextUseTime)
            return false;

        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        nextUseTime = Time.time + Recipe.delivery.cooldown;
        var shots = Mathf.Max(1, Mathf.RoundToInt(GetModifierValue(WeaponPrimitiveIds.ModifierMultishot, 1f)));
        for (var shot = 0; shot < shots; shot++)
            Deliver(origin, SpreadDirection(direction, shot, shots));
        return true;
    }

    private void Deliver(Vector3 origin, Vector3 direction)
    {
        var delivery = Recipe.delivery;
        switch (delivery.primitive)
        {
            case WeaponPrimitiveIds.DeliveryMeleeArc:
                DeliverMeleeArc(origin, direction);
                break;
            case WeaponPrimitiveIds.DeliveryAreaPulse:
                DeliverArea(origin);
                break;
            case WeaponPrimitiveIds.DeliveryProjectile:
            case WeaponPrimitiveIds.DeliveryThrownProjectile:
                RuntimeProjectile.Create(this, origin, direction, delivery);
                break;
            case WeaponPrimitiveIds.DeliveryHitscan:
            case WeaponPrimitiveIds.DeliveryBeam:
                if (Physics.Raycast(origin, direction, out var hit, delivery.range, delivery.hitLayers, QueryTriggerInteraction.Ignore))
                    ReportHit(hit.collider, hit.point, hit.normal, direction);
                break;
        }
    }

    private void DeliverMeleeArc(Vector3 origin, Vector3 direction)
    {
        var delivery = Recipe.delivery;
        foreach (var collider in Physics.OverlapSphere(origin, delivery.range, delivery.hitLayers, QueryTriggerInteraction.Ignore))
        {
            var toTarget = collider.ClosestPoint(origin) - origin;
            if (toTarget.sqrMagnitude < 0.0001f || Vector3.Angle(direction, toTarget) <= delivery.arcDegrees * 0.5f)
                ReportHit(collider, collider.ClosestPoint(origin), -direction, direction);
        }
    }

    private void DeliverArea(Vector3 origin)
    {
        var delivery = Recipe.delivery;
        foreach (var collider in Physics.OverlapSphere(origin, delivery.radius, delivery.hitLayers, QueryTriggerInteraction.Ignore))
            ReportHit(collider, collider.ClosestPoint(origin), Vector3.up, Vector3.zero);
    }

    internal void ReportHit(Collider target, Vector3 point, Vector3 normal, Vector3 direction)
    {
        if (target == null)
            return;

        Hit?.Invoke(new WeaponHitContext(this, target, point, normal, direction, Recipe.payloads));
    }

    internal void Explode(Vector3 origin, float radius, Collider directHit)
    {
        foreach (var collider in Physics.OverlapSphere(origin, radius, Recipe.delivery.hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (collider != directHit)
                ReportHit(collider, collider.ClosestPoint(origin), Vector3.up, Vector3.zero);
        }
    }

    internal float GetModifierValue(string primitive, float fallback)
    {
        foreach (var modifier in Recipe.modifiers)
            if (modifier.primitive == primitive)
                return modifier.value;
        return fallback;
    }

    private static Vector3 SpreadDirection(Vector3 direction, int index, int count)
    {
        if (count == 1) return direction;
        var angle = Mathf.Lerp(-6f, 6f, index / (float)(count - 1));
        return Quaternion.AngleAxis(angle, Vector3.up) * direction;
    }
}

/// <summary>Gameplay can consume this event to apply health, status, VFX, and audio.</summary>
public readonly struct WeaponHitContext
{
    public readonly WeaponRuntime weapon;
    public readonly Collider target;
    public readonly Vector3 point;
    public readonly Vector3 normal;
    public readonly Vector3 direction;
    public readonly WeaponPayload[] payloads;

    public WeaponHitContext(WeaponRuntime weapon, Collider target, Vector3 point, Vector3 normal, Vector3 direction, WeaponPayload[] payloads)
    {
        this.weapon = weapon;
        this.target = target;
        this.point = point;
        this.normal = normal;
        this.direction = direction;
        this.payloads = payloads;
    }
}

internal sealed class RuntimeProjectile : MonoBehaviour
{
    private WeaponRuntime weapon;
    private WeaponDelivery delivery;
    private Vector3 velocity;
    private float expiresAt;
    private int remainingPierces;
    private int remainingBounces;
    private float homingStrength;
    private readonly HashSet<Collider> hitTargets = new HashSet<Collider>();

    public static void Create(WeaponRuntime weapon, Vector3 origin, Vector3 direction, WeaponDelivery delivery)
    {
        var projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectileObject.name = weapon.Recipe.displayName + " Projectile";
        projectileObject.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        projectileObject.transform.localScale = Vector3.one * Mathf.Max(0.05f, delivery.radius * 2f);
        var projectile = projectileObject.AddComponent<RuntimeProjectile>();
        projectile.weapon = weapon;
        projectile.delivery = delivery;
        projectile.velocity = direction * delivery.speed;
        projectile.expiresAt = Time.time + delivery.lifetime;
        projectile.remainingPierces = Mathf.Max(0, Mathf.FloorToInt(weapon.GetModifierValue(WeaponPrimitiveIds.ModifierPierce, 0f)));
        projectile.remainingBounces = Mathf.Max(0, Mathf.FloorToInt(weapon.GetModifierValue(WeaponPrimitiveIds.ModifierBounce, 0f)));
        projectile.homingStrength = Mathf.Max(0f, weapon.GetModifierValue(WeaponPrimitiveIds.ModifierHoming, 0f));
    }

    private void Update()
    {
        SteerTowardsNearestTarget();
        var distance = velocity.magnitude * Time.deltaTime;
        if (TryGetNewHit(distance, out var hit))
        {
            transform.position = hit.point;
            weapon.ReportHit(hit.collider, hit.point, hit.normal, velocity.normalized);
            hitTargets.Add(hit.collider);
            var explosionRadius = weapon.GetModifierValue(WeaponPrimitiveIds.ModifierExplodeOnImpact, 0f);
            if (explosionRadius > 0f)
                weapon.Explode(hit.point, explosionRadius, hit.collider);

            if (remainingPierces > 0)
            {
                remainingPierces--;
                transform.position += velocity.normalized * 0.02f;
                return;
            }

            if (remainingBounces > 0)
            {
                remainingBounces--;
                velocity = Vector3.Reflect(velocity, hit.normal);
                transform.position += hit.normal * 0.02f;
                return;
            }

            Destroy(gameObject);
            return;
        }

        transform.position += velocity * Time.deltaTime;
        if (Time.time >= expiresAt) Destroy(gameObject);
    }

    private bool TryGetNewHit(float distance, out RaycastHit selectedHit)
    {
        selectedHit = default;
        var hits = Physics.SphereCastAll(transform.position, delivery.radius, velocity.normalized, distance, delivery.hitLayers, QueryTriggerInteraction.Ignore);
        var nearestDistance = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hitTargets.Contains(hit.collider) || hit.distance >= nearestDistance)
                continue;
            selectedHit = hit;
            nearestDistance = hit.distance;
        }
        return nearestDistance < float.MaxValue;
    }

    private void SteerTowardsNearestTarget()
    {
        if (homingStrength <= 0f || velocity.sqrMagnitude < 0.0001f)
            return;

        Collider closest = null;
        var closestDistance = float.MaxValue;
        foreach (var candidate in Physics.OverlapSphere(transform.position, delivery.range, delivery.hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hitTargets.Contains(candidate)) continue;
            var sqrDistance = (candidate.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
            if (sqrDistance < closestDistance) { closest = candidate; closestDistance = sqrDistance; }
        }
        if (closest == null) return;

        var desired = (closest.ClosestPoint(transform.position) - transform.position).normalized * velocity.magnitude;
        velocity = Vector3.RotateTowards(velocity, desired, homingStrength * Time.deltaTime, 0f);
        transform.rotation = Quaternion.LookRotation(velocity);
    }
}
