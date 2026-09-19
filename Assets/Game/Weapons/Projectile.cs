using System.Collections.Generic;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Collider-free projectile: sphere-casts along its path each frame. Behaviour is composed from the weapon's
    /// modifiers (homing, piercing, bouncing, sticky, proximity) and fire mode (thrown = gravity + detonate).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public static readonly List<Projectile> All = new List<Projectile>();
        /// <summary>Debug: which non-enemy colliders projectiles hit (bridge "state").</summary>
        public static readonly Dictionary<string, int> SurfaceHits = new Dictionary<string, int>();
        public static int EnemyHits;
        public const int MaxMines = 40;
        private static readonly Queue<Projectile> mines = new Queue<Projectile>();

        public ParsedWeapon Weapon;
        public Vector3 Velocity;
        public bool Stuck { get; private set; }

        private const float Gravity = 9.81f;
        private const float CastRadius = 0.12f;
        private const float ProximityRadius = 2.8f;
        private float life;
        private float maxLife = 4f;
        private int piercesLeft;
        private int bouncesLeft;
        private float fuseAt = -1f;
        private Enemy stuckTo;
        private Vector3 stuckOffset;
        private readonly HashSet<Enemy> alreadyHit = new HashSet<Enemy>();

        private bool Thrown => Weapon.FireMode == FireMode.Thrown;
        /// <summary>Mines and sticky payloads are physical objects: they arc and land even from a "gun".</summary>
        private float GravityScale => Thrown ? 1f : Weapon.Has(Mods.Sticky) || Weapon.Shape == ProjectileShape.Mine ? 0.6f : 0f;
        private bool Detonates => Thrown || Weapon.Has(Mods.Sticky) || Weapon.Has(Mods.Proximity) || Weapon.Payload == Payload.Explosive;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void Launch(ParsedWeapon weapon, Vector3 velocity)
        {
            Weapon = weapon;
            Velocity = velocity;
            piercesLeft = weapon.Has(Mods.Piercing) ? weapon.PierceCount : 0;
            bouncesLeft = weapon.Has(Mods.Bouncing) ? weapon.BounceCount : 0;
            // Airborne life is short; landing as a mine extends it (see BecomeMine).
            maxLife = Thrown || weapon.Has(Mods.Sticky) || weapon.Has(Mods.Proximity) ? 6f : 4f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            if (life > maxLife) { Finish(transform.position, null); return; }
            if (fuseAt > 0f && Time.time >= fuseAt) { Finish(transform.position, null); return; }

            if (Stuck)
            {
                if (stuckTo != null && stuckTo.Alive) transform.position = stuckTo.transform.TransformPoint(stuckOffset);
                else if (stuckTo != null && fuseAt < 0f) fuseAt = Time.time + 0.1f; // host died: pop soon
                if (Weapon.Has(Mods.Proximity) && AnyEnemyNear(ProximityRadius, stuckTo)) Finish(transform.position, null);
                return;
            }

            if (Weapon.Has(Mods.Homing)) Steer(dt);
            else Velocity += Vector3.down * (Gravity * GravityScale * dt);

            if (Weapon.Has(Mods.Proximity) && !Weapon.Has(Mods.Sticky) && life > 0.15f && AnyEnemyNear(ProximityRadius, null))
            {
                Finish(transform.position, null);
                return;
            }

            Vector3 step = Velocity * dt;
            float distance = step.magnitude;
            if (distance > 0f && Physics.SphereCast(transform.position, CastRadius, step / distance, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                var enemy = hit.collider.GetComponentInParent<Enemy>();
                if (enemy != null) { if (HitEnemy(enemy, hit.point)) return; }
                else if (HitSurface(hit)) return;
            }
            transform.position += step;
            if (Velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(Velocity);
        }

        private void Steer(float dt)
        {
            var target = Enemy.Nearest(transform.position + Velocity.normalized * 8f, 30f);
            if (target == null) { Velocity += Vector3.down * (Gravity * GravityScale * dt); return; }
            Vector3 desired = (target.Center - transform.position).normalized * Mathf.Max(Velocity.magnitude, 12f);
            Velocity = Vector3.RotateTowards(Velocity, desired, Mathf.Deg2Rad * 240f * dt, 30f * dt);
        }

        /// <returns>true when the projectile is finished or stuck.</returns>
        private bool HitEnemy(Enemy enemy, Vector3 point)
        {
            if (alreadyHit.Contains(enemy)) return false;
            alreadyHit.Add(enemy);
            EnemyHits++;

            if (Weapon.Has(Mods.Sticky))
            {
                StickTo(enemy, point);
                return true;
            }
            if (Detonates) { Finish(point, enemy); return true; }

            Effects.OnHit(Weapon, point, enemy, Weapon.Damage);
            if (piercesLeft-- > 0)
            {
                WorldText.Popup(point, "PIERCE", new Color(0.8f, 0.9f, 1f), 0.03f);
                return false;
            }
            Kill();
            return true;
        }

        private bool HitSurface(RaycastHit hit)
        {
            string key = hit.collider.name;
            SurfaceHits[key] = (SurfaceHits.TryGetValue(key, out var count) ? count : 0) + 1;
            if (bouncesLeft > 0)
            {
                bouncesLeft--;
                Velocity = Vector3.Reflect(Velocity, hit.normal) * 0.8f;
                transform.position = hit.point + hit.normal * (CastRadius + 0.02f);
                ProceduralSfx.PlayAt(ProceduralSfx.Blip, hit.point, 0.4f);
                return true;
            }
            if (Weapon.Has(Mods.Sticky) || (Weapon.Has(Mods.Proximity) && Thrown))
            {
                transform.position = hit.point + hit.normal * 0.05f;
                BecomeMine();
                return true;
            }
            if (Detonates) { Finish(hit.point, null); return true; }
            Kill();
            return true;
        }

        private void BecomeMine()
        {
            Stuck = true;
            if (!Weapon.Has(Mods.Proximity)) { fuseAt = Time.time + 1.2f; return; }
            maxLife = life + 20f;
            mines.Enqueue(this);
            // Cap the minefield: oldest mines fizzle so spam can't tank the frame rate.
            while (mines.Count > MaxMines)
            {
                var oldest = mines.Dequeue();
                if (oldest != null && oldest.enabled) oldest.Kill();
            }
        }

        private void StickTo(Enemy enemy, Vector3 point)
        {
            Stuck = true;
            stuckTo = enemy;
            stuckOffset = enemy.transform.InverseTransformPoint(point);
            WorldText.Popup(point, "STUCK", Weapon.Color, 0.035f);
            // Sticky + proximity waits for a second alien; plain sticky blows on a short fuse.
            fuseAt = Time.time + (Weapon.Has(Mods.Proximity) ? 2.5f : 1f);
        }

        private bool AnyEnemyNear(float radius, Enemy ignore)
        {
            var enemy = Enemy.Nearest(transform.position, radius, ignore);
            return enemy != null;
        }

        private void Finish(Vector3 point, Enemy direct)
        {
            if (Detonates && !Weapon.Has(Mods.Splash))
            {
                // Mines/grenades without splash still need an area pop to feel right.
                Effects.Explode(Weapon, point, 1.5f, Weapon.Damage, direct);
                if (direct != null) direct.TakeHit(Weapon, Weapon.Damage, point);
                if (Weapon.Has(Mods.Chain)) Effects.Chain(Weapon, point, direct, Weapon.ChainCount, Weapon.Damage * 0.6f);
            }
            else Effects.OnHit(Weapon, point, direct != null && direct.Alive ? direct : null, Weapon.Damage);
            Kill();
        }

        public void Kill()
        {
            if (!enabled) return;
            enabled = false;
            All.Remove(this);
            Destroy(gameObject);
        }
    }
}
