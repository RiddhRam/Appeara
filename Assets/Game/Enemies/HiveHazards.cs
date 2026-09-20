using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Everything a boss attack leaves in the world: shootable ordnance, ground telegraphs, and the pooled
    /// effects the encounter drives by hand instead of letting the particle timer reclaim them.
    /// It exists as one object because the three lists have to be emptied together at three different moments -
    /// the boss dying, the encounter being defeated, and the wave restarting - and a laser beam or a warning
    /// ring surviving any of those would follow the player into the next wave in full view.
    /// </summary>
    public sealed class HiveHazards
    {
        private struct Pooled
        {
            public string Key;
            public GameObject Instance;
        }

        private readonly List<HiveThreat> threats = new List<HiveThreat>();
        private readonly List<HiveTelegraph> markers = new List<HiveTelegraph>();
        private readonly List<Pooled> effects = new List<Pooled>();

        public int Count => threats.Count + markers.Count + effects.Count;
        public int ThreatCount => threats.Count;
        public int MarkerCount => markers.Count;
        public int EffectCount => effects.Count;

        public HiveThreat Track(HiveThreat threat)
        {
            if (threat != null) threats.Add(threat);
            return threat;
        }

        public HiveTelegraph Track(HiveTelegraph marker)
        {
            if (marker != null) markers.Add(marker);
            return marker;
        }

        /// <summary>
        /// Takes ownership of a pooled effect. ArtVfx normally recycles on a particle timer, but the beam is a
        /// LineRenderer with no particles at all and the fireball loops, so both would be reclaimed mid-attack
        /// or never; the encounter hands these back itself.
        /// </summary>
        public GameObject Track(string key, GameObject instance)
        {
            if (instance != null) effects.Add(new Pooled { Key = key, Instance = instance });
            return instance;
        }

        public void Release(HiveTelegraph marker)
        {
            if (marker == null) return;
            markers.Remove(marker);
            Remove(marker.gameObject);
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i].Instance != instance) continue;
                ArtVfx.Recycle(effects[i].Key, instance);
                effects.RemoveAt(i);
                return;
            }
        }

        public void Clear()
        {
            foreach (var threat in threats) if (threat != null) threat.Cancel();
            threats.Clear();
            foreach (var marker in markers) if (marker != null) Remove(marker.gameObject);
            markers.Clear();
            foreach (var effect in effects) if (effect.Instance != null) ArtVfx.Recycle(effect.Key, effect.Instance);
            effects.Clear();
        }

        /// <summary>
        /// Destroy refuses to run outside play mode, where the edit-mode tests that cover this cleanup live.
        /// The encounter itself only ever runs in play mode, so the branch exists purely to keep it testable.
        /// </summary>
        private static void Remove(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }
}
