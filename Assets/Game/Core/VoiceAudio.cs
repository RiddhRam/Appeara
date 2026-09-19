using System;
using UnityEngine;

namespace Armory.Core
{
    public enum CaptureVerdict { Ok, TooShort, Silent }

    /// <summary>
    /// Pure audio maths for push-to-talk. Kept out of MonoBehaviours so the decisions that silently ate the
    /// player's speech in the first headset test are unit-tested.
    /// </summary>
    public static class VoiceAudio
    {
        public const float MinSeconds = 0.2f;
        /// <summary>Below this peak the clip carries no voice at all (the Quest virtual mic returns ~3e-5).</summary>
        public const float SilencePeak = 0.002f;
        /// <summary>Trim threshold as a fraction of the clip's own peak, so quiet mics still work.</summary>
        public const float TrimFraction = 0.15f;
        public const float TargetPeak = 0.3f;

        public static float Peak(float[] samples)
        {
            float peak = 0f;
            if (samples == null) return 0f;
            foreach (var sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            return peak;
        }

        public static float Rms(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            double sum = 0d;
            foreach (var sample in samples) sum += (double)sample * sample;
            return Mathf.Sqrt((float)(sum / samples.Length));
        }

        /// <summary>Average-decimate to roughly <paramref name="targetRate"/>; transcription gains nothing above 16 kHz.</summary>
        public static float[] Downsample(float[] samples, int sourceRate, int targetRate, out int resultRate)
        {
            int factor = Mathf.Max(1, Mathf.RoundToInt(sourceRate / (float)targetRate));
            resultRate = sourceRate / factor;
            if (factor == 1 || samples == null || samples.Length == 0) return samples;
            var result = new float[samples.Length / factor];
            for (int i = 0; i < result.Length; i++)
            {
                float sum = 0f;
                for (int j = 0; j < factor; j++) sum += samples[i * factor + j];
                result[i] = sum / factor;
            }
            return result;
        }

        /// <summary>Lifts quiet captures (headset mics run low) without clipping. Returns the gain applied.</summary>
        public static float Normalize(float[] samples, float targetPeak = TargetPeak)
        {
            float peak = Peak(samples);
            if (peak <= 0.0001f || peak >= targetPeak) return 1f;
            float gain = Mathf.Min(targetPeak / peak, 12f);
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            return gain;
        }

        /// <summary>Trims near-silence relative to the clip's own peak, keeping a little padding around speech.</summary>
        public static float[] Trim(float[] samples, int padding)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<float>();
            float threshold = Mathf.Max(SilencePeak, Peak(samples) * TrimFraction);
            int start = 0, end = samples.Length - 1;
            while (start < samples.Length && Mathf.Abs(samples[start]) < threshold) start++;
            if (start >= samples.Length) return Array.Empty<float>();
            while (end > start && Mathf.Abs(samples[end]) < threshold) end--;
            start = Mathf.Max(0, start - padding);
            end = Mathf.Min(samples.Length - 1, end + padding);
            var trimmed = new float[end - start + 1];
            Array.Copy(samples, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>Why a capture was accepted or dropped — surfaced to the player instead of failing silently.</summary>
        public static CaptureVerdict Judge(float[] samples, int sampleRate, out string report)
        {
            float peak = Peak(samples);
            float seconds = samples == null || sampleRate <= 0 ? 0f : samples.Length / (float)sampleRate;
            if (peak < SilencePeak)
            {
                report = $"heard nothing (peak {peak:0.00000}) - wrong mic or Link audio";
                return CaptureVerdict.Silent;
            }
            if (seconds < MinSeconds)
            {
                report = $"too short ({seconds:0.00}s) - hold while you speak";
                return CaptureVerdict.TooShort;
            }
            report = $"sent {seconds:0.0}s (peak {peak:0.00})";
            return CaptureVerdict.Ok;
        }
    }
}
