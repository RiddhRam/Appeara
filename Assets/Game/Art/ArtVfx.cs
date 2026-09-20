using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Spawns the authored particle prefabs and, crucially, takes them away again. A wave-two swarm is 45 aliens
    /// with muzzle flashes, impacts and death bursts, so every effect is pooled by key and recycled once its
    /// particles finish: instantiating and destroying them per hit is what would cost the headset its frame rate.
    /// Every lookup fails soft - no library row, or no prefab, means the gameplay still runs with no art.
    /// </summary>
    public static class ArtVfx
    {
        /// <summary>Hard cap per key, so a pathological frame cannot grow the pool without bound.</summary>
        public const int PoolLimit = 24;
        private const float FallbackLifetime = 2f;

        private static EffectLibrary library;
        private static bool libraryLoaded;
        private static Transform root;
        private static readonly Dictionary<string, Stack<GameObject>> Pools = new Dictionary<string, Stack<GameObject>>();
        private static readonly Dictionary<string, float> Lifetimes = new Dictionary<string, float>();

        public static EffectLibrary Library
        {
            get
            {
                if (!libraryLoaded)
                {
                    library = Resources.Load<EffectLibrary>("EffectLibrary");
                    libraryLoaded = true;
                    if (library == null) Debug.LogWarning("ArtVfx: no EffectLibrary asset; run Armory > Art > Build Effect Library.");
                }
                return library;
            }
        }

        public static bool Has(string key) => Library != null && Library.Find(key) != null;

        /// <summary>Plays an effect at a point. Returns the live instance, or null when the key has no art.</summary>
        public static GameObject Play(string key, Vector3 position, Quaternion rotation, float scale = 1f, Transform parent = null)
        {
            var prefab = Library != null ? Library.Find(key) : null;
            if (prefab == null) return null;

            var instance = Take(key, prefab);
            if (instance == null) return null;
            var t = instance.transform;
            t.SetParent(parent, true);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one * scale;
            instance.SetActive(true);
            Restart(instance);
            VfxLifetime.Attach(instance, key, LifetimeOf(key, instance));
            return instance;
        }

        public static GameObject Play(string key, Vector3 position, float scale = 1f) =>
            Play(key, position, Quaternion.identity, scale);

        /// <summary>Points the effect along a direction; used for muzzle flashes and directional impacts.</summary>
        public static GameObject PlayDirected(string key, Vector3 position, Vector3 forward, float scale = 1f, Transform parent = null)
        {
            var rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;
            return Play(key, position, rotation, scale, parent);
        }

        private static GameObject Take(string key, GameObject prefab)
        {
            if (Pools.TryGetValue(key, out var pool))
                while (pool.Count > 0)
                {
                    var pooled = pool.Pop();
                    // Scene reloads destroy pooled instances behind our back; skip the corpses.
                    if (pooled != null) return pooled;
                }
            var spawned = Object.Instantiate(prefab, Root());
            spawned.name = key;
            return spawned;
        }

        /// <summary>Called by VfxLifetime once the particles have finished.</summary>
        internal static void Recycle(string key, GameObject instance)
        {
            if (instance == null) return;
            if (!Pools.TryGetValue(key, out var pool)) Pools[key] = pool = new Stack<GameObject>();
            if (pool.Count >= PoolLimit) { Object.Destroy(instance); return; }
            instance.SetActive(false);
            instance.transform.SetParent(Root(), false);
            pool.Push(instance);
        }

        private static void Restart(GameObject instance)
        {
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Clear(true);
                system.Play(true);
            }
            foreach (var trail in instance.GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
        }

        /// <summary>Longest particle duration plus its lifetime, measured once per key and cached.</summary>
        private static float LifetimeOf(string key, GameObject instance)
        {
            if (Lifetimes.TryGetValue(key, out var cached)) return cached;
            float longest = 0f;
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
            }
            if (longest <= 0.01f) longest = FallbackLifetime;
            Lifetimes[key] = longest;
            return longest;
        }

        private static Transform Root()
        {
            if (root != null) return root;
            var holder = new GameObject("Art VFX Pool");
            Object.DontDestroyOnLoad(holder);
            root = holder.transform;
            return root;
        }

        /// <summary>Drops the pool; used by tests and when a run restarts so stale instances do not linger.</summary>
        public static void Clear()
        {
            foreach (var pool in Pools.Values)
                while (pool.Count > 0)
                {
                    var pooled = pool.Pop();
                    if (pooled != null) Object.Destroy(pooled);
                }
            Pools.Clear();
            Lifetimes.Clear();
            libraryLoaded = false;
            library = null;
        }
    }

    /// <summary>Returns one spawned effect to the pool when its particles are done.</summary>
    public sealed class VfxLifetime : MonoBehaviour
    {
        private string key;
        private float due;

        internal static void Attach(GameObject instance, string key, float lifetime)
        {
            var timer = instance.GetComponent<VfxLifetime>();
            if (timer == null) timer = instance.AddComponent<VfxLifetime>();
            timer.key = key;
            timer.due = Time.time + lifetime;
            timer.enabled = true;
        }

        private void Update()
        {
            if (Time.time < due) return;
            enabled = false;
            ArtVfx.Recycle(key, gameObject);
        }
    }
}
