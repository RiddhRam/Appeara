using System.Collections.Generic;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Synthesised placeholder sounds so every weapon is audible before ElevenLabs audio arrives.</summary>
    public static class ProceduralSfx
    {
        private const int Rate = 22050;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static AudioClip Shot(Payload payload) => Get("shot_" + payload, () => Synth(payload));
        public static AudioClip Boom => Get("boom", () => Make("boom", 0.6f, t => Noise() * Mathf.Exp(-t * 6f) * (0.6f + 0.4f * Mathf.Sin(t * 180f))));
        public static AudioClip Hit => Get("hit", () => Make("hit", 0.06f, t => Noise() * Mathf.Exp(-t * 60f) * 0.6f));
        public static AudioClip Zap => Get("zap", () => Make("zap", 0.25f, t => Mathf.Sign(Mathf.Sin(t * 900f + Mathf.Sin(t * 70f) * 8f)) * Mathf.Exp(-t * 10f) * 0.35f));
        public static AudioClip Blip => Get("blip", () => Make("blip", 0.12f, t => Mathf.Sin(t * 2 * Mathf.PI * (880f + t * 3000f)) * Mathf.Exp(-t * 20f) * 0.4f));
        public static AudioClip Fabricate => Get("fab", () => Make("fab", 0.8f, t => Mathf.Sin(t * 2 * Mathf.PI * (200f + t * 900f)) * 0.25f * (1f - t / 0.8f)));

        private static AudioClip Synth(Payload payload)
        {
            switch (payload)
            {
                case Payload.Plasma: return Make("plasma", 0.18f, t => Mathf.Sin(t * 2 * Mathf.PI * (1400f - t * 6000f)) * Mathf.Exp(-t * 14f) * 0.5f);
                case Payload.Electric: return Make("electric", 0.15f, t => Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 320f)) * Noise() * Mathf.Exp(-t * 18f) * 0.4f);
                case Payload.Cryo: return Make("cryo", 0.2f, t => Mathf.Sin(t * 2 * Mathf.PI * 2600f) * Mathf.Sin(t * 2 * Mathf.PI * 31f) * Mathf.Exp(-t * 10f) * 0.35f);
                case Payload.Explosive: return Make("launcher", 0.2f, t => (Noise() * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 90f)) * Mathf.Exp(-t * 16f) * 0.5f);
                default: return Make("kinetic", 0.1f, t => Noise() * Mathf.Exp(-t * 40f) * 0.6f);
            }
        }

        private static AudioClip Get(string key, System.Func<AudioClip> build)
        {
            if (!clips.TryGetValue(key, out var clip) || clip == null) clips[key] = clip = build();
            return clip;
        }

        private static float Noise() => Random.value * 2f - 1f;

        private static AudioClip Make(string name, float seconds, System.Func<float, float> wave)
        {
            int count = (int)(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = wave(i / (float)Rate);
            return FromSamples(name, data, Rate);
        }

        public static AudioClip FromSamples(string name, float[] samples, int rate)
        {
            var clip = AudioClip.Create(name, Mathf.Max(1, samples.Length), 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("Sfx");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.minDistance = 3f;
            source.maxDistance = 80f;
            source.pitch = Random.Range(0.92f, 1.08f);
            source.Play();
            Object.Destroy(go, clip.length / source.pitch + 0.1f);
        }
    }
}
