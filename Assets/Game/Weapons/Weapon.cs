using Armory.Core;
using Unity.Profiling;
using UnityEngine;

namespace Armory
{
    /// <summary>A held, assembled weapon. Fires projectiles/thrown objects or a continuous beam from the rig's aim pose.</summary>
    public sealed class Weapon : MonoBehaviour
    {
        private static readonly ProfilerMarker ProjectileSpawnMarker = new ProfilerMarker("Armory.Projectile.Spawn");
        public ParsedWeapon Spec;
        public Transform Muzzle;
        public AudioClip FireClip;
        public AudioClip ImpactClip;

        private AudioSource source;
        /// <summary>0-1 while a bow is being drawn, for the HUD and the bow's own animation.</summary>
        public float DrawPower { get; private set; }
        private float controllerSpeed;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private bool swinging;
        private float swingEndsAt;
        private float drawStarted;
        private readonly System.Collections.Generic.HashSet<Enemy> hitThisSwing = new System.Collections.Generic.HashSet<Enemy>();
        private bool warnedMissingSpec;
        private float cooldown;
        private LineRenderer beam;
        private float beamTick;
        private int beamTicks;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0.6f;
            source.playOnAwake = false;
        }

        public void Tick(bool triggerHeld, Transform aim)
        {
            // A weapon without a spec would throw every frame and flood the editor; report it once instead.
            if (Spec == null)
            {
                if (!warnedMissingSpec) { warnedMissingSpec = true; Debug.LogWarning("Weapon '" + name + "' has no spec; ignoring it.", this); }
                return;
            }
            cooldown -= Time.deltaTime;
            TrackSpeed(aim);
            if (Spec.FireMode == FireMode.Beam) { UpdateBeam(triggerHeld, aim); return; }
            if (Spec.FireMode == FireMode.Melee) { UpdateMelee(aim); return; }
            if (Spec.FireMode == FireMode.Bow) { UpdateBow(triggerHeld, aim); return; }
            if (!triggerHeld || cooldown > 0f) return;
            cooldown = 1f / Spec.FireRate;
            Fire(aim);
        }

        /// <summary>Controller speed, smoothed a little so a single noisy frame cannot trigger a swing.</summary>
        private void TrackSpeed(Transform aim)
        {
            Vector3 position = Muzzle != null ? Muzzle.position : aim.position;
            if (Time.deltaTime > 0f && hasLastPosition)
                controllerSpeed = Mathf.Lerp(controllerSpeed, Vector3.Distance(position, lastPosition) / Time.deltaTime, 0.5f);
            lastPosition = position;
            hasLastPosition = true;
        }

        /// <summary>Swing the controller: everything the blade sweeps through takes a hit, once per swing.</summary>
        private void UpdateMelee(Transform aim)
        {
            float strength = SwingAndDraw.SwingStrength(controllerSpeed);
            if (strength <= 0f)
            {
                if (swinging && Time.time > swingEndsAt) { swinging = false; hitThisSwing.Clear(); }
                return;
            }
            if (!swinging) { swinging = true; hitThisSwing.Clear(); }
            swingEndsAt = Time.time + SwingAndDraw.SwingCooldown;

            Vector3 origin = Muzzle != null ? Muzzle.position : aim.position;
            float damage = SwingAndDraw.SwingDamage(Spec.Damage, controllerSpeed);
            foreach (var enemy in Enemy.All)
            {
                if (enemy == null || !enemy.Alive || hitThisSwing.Contains(enemy)) continue;
                if (Vector3.Distance(enemy.Center, origin) > SwingAndDraw.Reach + enemy.Radius) continue;
                hitThisSwing.Add(enemy);
                Effects.OnHit(Spec, enemy.Center, enemy, damage);
                Effects.Flash(enemy.Center, Spec.Color, 1.4f);
                source.PlayOneShot(FireClip != null ? FireClip : ProceduralSfx.Shot(Spec.Payload), 0.8f);
            }
        }

        /// <summary>Hold the trigger to draw, release to loose. A fuller draw hits harder and flies faster.</summary>
        private void UpdateBow(bool triggerHeld, Transform aim)
        {
            if (triggerHeld)
            {
                if (drawStarted <= 0f) drawStarted = Time.time;
                DrawPower = SwingAndDraw.DrawPower(Time.time - drawStarted);
                return;
            }
            if (drawStarted <= 0f) return;
            float held = Time.time - drawStarted;
            drawStarted = 0f;
            DrawPower = 0f;
            if (!SwingAndDraw.CanRelease(held) || cooldown > 0f) return;
            cooldown = 1f / Spec.FireRate;

            Vector3 origin = Muzzle != null ? Muzzle.position : aim.position;
            var arrow = SpawnProjectile(origin, aim.rotation * Vector3.forward * (Spec.ProjectileSpeed * SwingAndDraw.DrawSpeedMultiplier(held)));
            if (arrow != null) arrow.DamageScale = SwingAndDraw.DrawDamageMultiplier(held);
            source.pitch = Mathf.Lerp(1.15f, 0.85f, SwingAndDraw.DrawPower(held));
            source.PlayOneShot(FireClip != null ? FireClip : ProceduralSfx.Shot(Spec.Payload), 0.9f);
        }

        private void Fire(Transform aim)
        {
            Vector3 origin = Muzzle != null ? Muzzle.position : aim.position;
            for (int i = 0; i < Spec.ProjectileCount; i++)
            {
                Vector2 jitter = Random.insideUnitCircle * Spec.SpreadDeg;
                Vector3 direction = aim.rotation * Quaternion.Euler(jitter.y, jitter.x, 0f) * Vector3.forward;
                if (Spec.FireMode == FireMode.Thrown) direction = Vector3.Slerp(direction, Vector3.up, 0.12f);
                SpawnProjectile(origin, direction * Spec.ProjectileSpeed);
            }
            source.pitch = Random.Range(0.93f, 1.07f);
            source.PlayOneShot(FireClip != null ? FireClip : ProceduralSfx.Shot(Spec.Payload), FireClip != null ? 0.9f : 0.6f);
        }

        private Projectile SpawnProjectile(Vector3 origin, Vector3 velocity)
        {
            using var marker = ProjectileSpawnMarker.Auto();
            float size = Spec.Shape == ProjectileShape.Mine ? 0.16f : Spec.FireMode == FireMode.Thrown ? 0.14f : 0.08f;
            PrimitiveType type = Spec.Shape == ProjectileShape.Disc || Spec.Shape == ProjectileShape.Mine ? PrimitiveType.Cylinder : Spec.Shape == ProjectileShape.Bolt ? PrimitiveType.Capsule : PrimitiveType.Sphere;
            Vector3 scale = Spec.Shape == ProjectileShape.Bolt ? new Vector3(size * 0.6f, size * 2.5f, size * 0.6f)
                : type == PrimitiveType.Cylinder ? new Vector3(size * 1.6f, size * 0.3f, size * 1.6f) : Vector3.one * size;

            var go = new GameObject("Projectile");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(velocity);
            var visual = Mats.Shape(type, go.transform, Vector3.zero, scale, Mats.Glow(Spec.Color), name: "Visual");
            if (Spec.Shape == ProjectileShape.Bolt) visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (Spec.Trail)
            {
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Mats.Glow(Spec.Color);
                trail.time = 0.12f;
                trail.widthMultiplier = size * 0.8f;
                trail.endWidth = 0f;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var projectile = go.AddComponent<Projectile>();
            projectile.Launch(Spec, velocity);
            return projectile;
        }

        private void UpdateBeam(bool held, Transform aim)
        {
            if (beam == null)
            {
                beam = Mats.Line(transform, Spec.Color, 0.05f);
                beam.enabled = false;
            }
            beam.enabled = held;
            if (!held)
            {
                if (source.isPlaying && source.loop) source.Stop();
                return;
            }
            if (!source.isPlaying)
            {
                source.clip = FireClip != null ? FireClip : ProceduralSfx.Shot(Spec.Payload);
                source.loop = true;
                source.volume = 0.5f;
                source.Play();
            }

            Vector3 origin = Muzzle != null ? Muzzle.position : aim.position;
            Vector3 direction = aim.forward;
            const float range = 60f;
            int pierce = Spec.Has(Mods.Piercing) ? Spec.PierceCount + 1 : 1;
            var hits = Physics.SphereCastAll(origin, 0.15f, direction, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            float end = range;
            var targets = new System.Collections.Generic.List<(Enemy, Vector3)>();
            foreach (var hit in hits)
            {
                var enemy = hit.collider.GetComponentInParent<Enemy>();
                if (enemy == null) { end = hit.distance; break; }
                // A boss has a body and four organ colliders: one beam tick must hit it only once.
                if (targets.Exists(target => target.Item1 == enemy)) continue;
                targets.Add((enemy, hit.point));
                if (targets.Count >= pierce) { end = hit.distance; break; }
            }
            beam.widthMultiplier = 0.04f + Mathf.PingPong(Time.time * 0.3f, 0.03f);
            beam.SetPosition(0, origin);
            beam.SetPosition(1, origin + direction * end);

            beamTick -= Time.deltaTime;
            if (beamTick > 0f) return;
            beamTick = 0.1f;
            beamTicks++;
            foreach (var (enemy, point) in targets)
            {
                if (enemy == null || !enemy.Alive) continue;
                enemy.TakeHit(Spec, Spec.Damage * 0.1f, point);
                enemy.ApplyStatus(Spec.Payload, Spec.Damage * 0.1f);
                if (Spec.Has(Mods.Slow) && enemy.Alive) enemy.ApplySlow(0.45f, 1f);
                // Area effects pulse every half second so beams with splash/chain don't melt the frame.
                if (beamTicks % 5 == 0)
                {
                    if (Spec.Has(Mods.Splash)) Effects.Explode(Spec, point, Spec.SplashRadius * 0.6f, Spec.Damage * 0.3f, enemy);
                    if (Spec.Has(Mods.Chain)) Effects.Chain(Spec, point, enemy, Spec.ChainCount, Spec.Damage * 0.3f);
                }
            }
        }
    }
}
