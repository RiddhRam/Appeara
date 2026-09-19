using UnityEngine;

namespace Armory
{
    /// <summary>What the aliens want. Hits from aliens that reach it drain HP; at zero the wave restarts (demo-safe, no game over).</summary>
    public sealed class StationCore : MonoBehaviour
    {
        public static StationCore Instance { get; private set; }
        public float MaxHealth = 100f;
        public float ReachRadius = 1.5f;
        public bool BuildVisual = true;
        public float Health { get; private set; }

        private Transform spinner;

        private void Awake()
        {
            Instance = this;
            Health = MaxHealth;
            if (!BuildVisual)
            {
                // The station's own centrepiece is the core; no collider so shots across the arena pass through.
                spinner = new GameObject("Core Marker").transform;
                spinner.SetParent(transform, false);
                return;
            }
            Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.3f, 2.4f), Mats.Lit(new Color(0.2f, 0.22f, 0.26f)), collider: true, name: "Core Base");
            Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 2f, 0f), new Vector3(0.7f, 1.6f, 0.7f), Mats.Lit(new Color(0.2f, 0.9f, 1f), 1.5f), collider: true, name: "Core Pillar");
            spinner = Mats.Shape(PrimitiveType.Cube, transform, new Vector3(0f, 4.2f, 0f), Vector3.one * 0.8f, Mats.Lit(new Color(0.3f, 1f, 1f), 2f), name: "Core Crystal").transform;
        }

        private void Update()
        {
            spinner.Rotate(20f * Time.deltaTime, 45f * Time.deltaTime, 0f);
        }

        public void TakeDamage(float amount)
        {
            if (Health <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            Effects.Flash(transform.position + Vector3.up * 2f, new Color(1f, 0.3f, 0.2f), 3f);
            ProceduralSfx.PlayAt(ProceduralSfx.Boom, transform.position, 0.5f);
            if (Health <= 0f) WaveDirector.Instance?.OnCoreDestroyed();
        }

        public void Repair() => Health = MaxHealth;

        /// <summary>Gradual repair used by the armory phase between waves.</summary>
        public void Repair(float amount) => Health = Mathf.Min(MaxHealth, Health + Mathf.Max(0f, amount));
    }
}
