using System.Collections.Generic;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Placeholder alien. Walks to the station core; behaviour and defences come from its kind + mothership counters.</summary>
    public sealed class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        public EnemyKind Kind;
        public float MaxHealth;
        public float Health;
        public float ShieldHealth;
        public float Speed;
        public float CoreDamage;
        public float Radius = 0.6f;
        public Vector3 Target;
        public Color BaseColor;

        private Renderer[] renderers;
        private GameObject shieldBubble;
        private float slowUntil;
        private float slowFactor = 1f;
        private float flashUntil;
        private float zigPhase;
        private float nextDodgeCheck;
        private float dodgeCooldown;
        private Vector3 dodgeVelocity;
        private float nextTeleport;
        private float nextIntercept;
        private Vector3 lateral;

        public bool Alive => Health > 0f;
        public bool ShieldUp => ShieldHealth > 0f;
        public Vector3 Center => transform.position + Vector3.up * (Radius + 0.2f);

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void Init(EnemyKind kind, Vector3 target)
        {
            Kind = kind;
            Target = target;
            renderers = GetComponentsInChildren<Renderer>();
            zigPhase = Random.value * 10f;
            var ms = Mothership.Instance;
            if (ms != null && ms.Has(CounterKind.Armor)) { MaxHealth *= 1.5f; Health = MaxHealth; }
            if (ms != null && ms.Has(CounterKind.Rush)) Speed *= 1.5f;
            if (ms != null && ms.Has(CounterKind.Shield) && ShieldHealth <= 0f) ShieldHealth = MaxHealth * 0.4f;
            if (ms != null && ms.Has(CounterKind.Teleport)) nextTeleport = Time.time + Random.Range(1.5f, 3f);
            if (ShieldHealth > 0f) CreateShieldBubble();
            if (ms != null && ms.Has(CounterKind.Reflect))
                Mats.Shape(PrimitiveType.Sphere, transform, Vector3.up * (Radius + 0.2f), Vector3.one * (Radius * 2.6f), Mats.Glow(new Color(0.9f, 0.9f, 1f), 0.18f), name: "Reflective Sheen");
        }

        private void CreateShieldBubble()
        {
            if (shieldBubble != null) return;
            shieldBubble = Mats.Shape(PrimitiveType.Sphere, transform, Vector3.up * (Radius + 0.2f), Vector3.one * (Radius * 3.2f), Mats.Glow(new Color(0.3f, 0.6f, 1f), 0.3f), name: "Shield");
        }

        private void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            var ms = Mothership.Instance;

            Vector3 toTarget = Target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            float reach = StationCore.Instance != null ? StationCore.Instance.ReachRadius : 1.5f;
            if (distance < reach + Radius)
            {
                StationCore.Instance?.TakeDamage(CoreDamage);
                Die(false);
                return;
            }

            Vector3 forward = toTarget / distance;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float speed = Speed * (Time.time < slowUntil ? slowFactor : 1f);
            Vector3 velocity = forward * speed;
            if (Kind == EnemyKind.Fast) velocity += side * (Mathf.Sin(Time.time * 3f + zigPhase) * speed * 0.8f);
            if (Kind == EnemyKind.Swarm) velocity += side * (Mathf.Sin(Time.time * 5f + zigPhase) * 1.5f);
            if (ms != null && ms.Has(CounterKind.Spread)) velocity += SeparationFrom(All) * 3f;

            if (ms != null && ms.Has(CounterKind.Dodge)) UpdateDodge(side);
            velocity += dodgeVelocity;
            dodgeVelocity = Vector3.MoveTowards(dodgeVelocity, Vector3.zero, 20f * dt);

            if (ms != null && ms.Has(CounterKind.Teleport) && Time.time > nextTeleport)
            {
                nextTeleport = Time.time + Random.Range(2.5f, 4f);
                Vector3 jump = forward * Mathf.Min(4f, distance - 3f) + side * Random.Range(-3f, 3f);
                Effects.Flash(Center, BaseColor, 1.2f);
                transform.position += jump;
                Effects.Flash(Center, BaseColor, 1.2f);
            }

            if (ms != null && ms.Has(CounterKind.Intercept) && Time.time > nextIntercept) TryIntercept();

            transform.position += velocity * dt;
            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(velocity.x, 0f, velocity.z)), 8f * dt);

            if (flashUntil > 0f && Time.time > flashUntil)
            {
                flashUntil = 0f;
                SetTint(BaseColor);
            }
        }

        private Vector3 SeparationFrom(List<Enemy> others)
        {
            Vector3 push = Vector3.zero;
            foreach (var other in others)
            {
                if (other == this) continue;
                Vector3 away = transform.position - other.transform.position;
                away.y = 0f;
                float d = away.magnitude;
                if (d > 0.01f && d < 4f) push += away / d * (1f - d / 4f);
            }
            return push;
        }

        private void UpdateDodge(Vector3 side)
        {
            if (Time.time < nextDodgeCheck || Time.time < dodgeCooldown) return;
            nextDodgeCheck = Time.time + 0.1f;
            foreach (var projectile in Projectile.All)
            {
                if (projectile.Weapon != null && projectile.Weapon.Has(Mods.Homing)) continue;
                Vector3 toMe = Center - projectile.transform.position;
                if (toMe.sqrMagnitude > 64f) continue;
                if (Vector3.Dot(projectile.Velocity.normalized, toMe.normalized) < 0.9f) continue;
                dodgeVelocity = side * (Random.value < 0.5f ? -9f : 9f);
                dodgeCooldown = Time.time + 1.2f;
                return;
            }
        }

        private void TryIntercept()
        {
            nextIntercept = Time.time + 0.25f;
            foreach (var projectile in Projectile.All)
            {
                if (projectile.Velocity.magnitude > 30f && !projectile.Stuck) continue;
                if ((projectile.transform.position - Center).sqrMagnitude > 16f) continue;
                if (Random.value > 0.5f) continue;
                Effects.Lightning(Center, projectile.transform.position, new Color(1f, 0.3f, 0.3f));
                WorldText.Popup(projectile.transform.position, "INTERCEPTED", new Color(1f, 0.4f, 0.4f), 0.04f);
                projectile.Kill();
                return;
            }
        }

        /// <summary>All damage flows through here so matchups, adaptations and the combat log stay consistent.</summary>
        public float TakeHit(ParsedWeapon weapon, float baseDamage)
        {
            if (!Alive || weapon == null) return 0f;
            var ms = Mothership.Instance;
            bool shield = ShieldUp;
            float multiplier = DamageTable.Multiplier(Kind, weapon, shield, ms != null ? ms.Resistances : null);
            if (ms != null && ms.Has(CounterKind.Reflect) && (weapon.FireMode == FireMode.Beam || weapon.Payload == Payload.Plasma)) multiplier *= 0.25f;
            float amount = baseDamage * multiplier;

            if (shield)
            {
                ShieldHealth -= amount;
                if (ShieldHealth <= 0f)
                {
                    ShieldHealth = 0f;
                    if (shieldBubble != null) Destroy(shieldBubble);
                    WorldText.Popup(Center + Vector3.up, "SHIELD DOWN", new Color(0.4f, 0.7f, 1f), 0.06f);
                    ProceduralSfx.PlayAt(ProceduralSfx.Zap, Center);
                }
            }
            else Health -= amount;

            ArmoryGame.Instance?.RecordDamage(weapon, amount);
            Flash();
            if (multiplier >= 1.4f) WorldText.Popup(Center + Vector3.up * 0.8f, "WEAK!", new Color(1f, 0.85f, 0.2f));
            else if (multiplier <= 0.45f) WorldText.Popup(Center + Vector3.up * 0.8f, shield ? "SHIELDED" : "RESISTED", new Color(0.6f, 0.6f, 0.7f));

            if (Health <= 0f) Die(true);
            return amount;
        }

        public void ApplySlow(float factor, float seconds)
        {
            slowFactor = Mathf.Min(slowFactor, factor);
            if (Time.time > slowUntil) slowFactor = factor;
            slowUntil = Time.time + seconds;
            SetTint(Color.Lerp(BaseColor, new Color(0.6f, 0.95f, 1f), 0.7f));
            flashUntil = slowUntil;
        }

        private void Flash()
        {
            if (flashUntil > Time.time + 0.2f) return;
            SetTint(Color.white);
            flashUntil = Time.time + 0.06f;
        }

        private void SetTint(Color color)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r != null && r.gameObject != shieldBubble && r.name != "Reflective Sheen") r.sharedMaterial = Mats.Lit(color, 0.25f);
        }

        public void Die(bool killed)
        {
            if (!enabled) return;
            Health = 0f;
            enabled = false;
            All.Remove(this);
            if (killed)
            {
                Effects.Burst(Center, BaseColor, Radius * 2f);
                ProceduralSfx.PlayAt(ProceduralSfx.Hit, Center, 0.8f);
            }
            WaveDirector.Instance?.OnEnemyRemoved(this, killed);
            Destroy(gameObject);
        }

        public static Enemy Nearest(Vector3 point, float maxDistance, Enemy exclude = null, ICollection<Enemy> excludeSet = null)
        {
            Enemy best = null;
            float bestDistance = maxDistance * maxDistance;
            foreach (var enemy in All)
            {
                if (enemy == exclude || !enemy.Alive || (excludeSet != null && excludeSet.Contains(enemy))) continue;
                float d = (enemy.Center - point).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = enemy; }
            }
            return best;
        }
    }
}
