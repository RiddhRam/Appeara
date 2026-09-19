using System.Collections.Generic;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Area damage, chain arcs and short-lived placeholder visuals.</summary>
    public static class Effects
    {
        private static readonly List<Enemy> scratch = new List<Enemy>();

        /// <summary>Applies a weapon's on-hit effects after a direct hit (or at a detonation point when <paramref name="direct"/> is null).</summary>
        public static void OnHit(ParsedWeapon weapon, Vector3 point, Enemy direct, float damage)
        {
            if (direct != null)
            {
                direct.TakeHit(weapon, damage);
                if (weapon.Has(Mods.Slow)) direct.ApplySlow(0.45f, 2.5f);
            }
            if (weapon.Has(Mods.Splash)) Explode(weapon, point, weapon.SplashRadius, damage * 0.8f, direct);
            if (weapon.Has(Mods.Chain)) Chain(weapon, direct != null ? direct.Center : point, direct, weapon.ChainCount, damage * 0.6f);
        }

        public static void Explode(ParsedWeapon weapon, Vector3 point, float radius, float damage, Enemy skip = null)
        {
            Burst(point, weapon.Color, radius * 2f);
            ProceduralSfx.PlayAt(ProceduralSfx.Boom, point, 0.9f);
            scratch.Clear();
            scratch.AddRange(Enemy.All);
            foreach (var enemy in scratch)
            {
                if (enemy == null || enemy == skip || !enemy.Alive) continue;
                float distance = Vector3.Distance(enemy.Center, point);
                if (distance > radius + enemy.Radius) continue;
                float falloff = Mathf.Lerp(1f, 0.4f, distance / (radius + enemy.Radius));
                enemy.TakeHit(weapon, damage * falloff);
                if (weapon.Has(Mods.Slow) && enemy.Alive) enemy.ApplySlow(0.45f, 2.5f);
            }
        }

        public static void Chain(ParsedWeapon weapon, Vector3 from, Enemy first, int jumps, float damage)
        {
            var hit = new HashSet<Enemy>();
            if (first != null) hit.Add(first);
            Vector3 origin = from;
            for (int i = 0; i < jumps; i++)
            {
                var next = Enemy.Nearest(origin, 7f, null, hit);
                if (next == null) break;
                hit.Add(next);
                Lightning(origin, next.Center, weapon.Color);
                origin = next.Center;
                next.TakeHit(weapon, damage);
            }
            if (hit.Count > (first != null ? 1 : 0)) ProceduralSfx.PlayAt(ProceduralSfx.Zap, from, 0.5f);
        }

        public static void Lightning(Vector3 from, Vector3 to, Color color)
        {
            var line = Mats.Line(null, Color.Lerp(color, Color.white, 0.4f), 0.04f);
            const int points = 8;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                Vector3 jitter = (i == 0 || i == points - 1) ? Vector3.zero : Random.insideUnitSphere * 0.35f;
                line.SetPosition(i, Vector3.Lerp(from, to, t) + jitter);
            }
            Object.Destroy(line.gameObject, 0.15f);
        }

        public static void Burst(Vector3 point, Color color, float size)
        {
            var sphere = Mats.Shape(PrimitiveType.Sphere, null, point, Vector3.one * 0.2f, Mats.Glow(color, 0.55f), name: "Burst");
            sphere.AddComponent<Expander>().Size = size;
        }

        public static void Flash(Vector3 point, Color color, float size) => Burst(point, Color.Lerp(color, Color.white, 0.5f), size);

        private sealed class Expander : MonoBehaviour
        {
            public float Size;
            private float age;

            private void Update()
            {
                age += Time.deltaTime;
                float t = age / 0.25f;
                transform.localScale = Vector3.one * Mathf.Lerp(0.2f, Size, 1f - (1f - t) * (1f - t));
                if (t >= 1f) Destroy(gameObject);
            }
        }
    }
}
