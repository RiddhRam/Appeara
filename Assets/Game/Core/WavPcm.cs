using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Armory.Core
{
    /// <summary>Mic → WAV for transcription; raw PCM16 (ElevenLabs output) → floats for AudioClips.</summary>
    public static class WavPcm
    {
        public static byte[] EncodeWav(float[] samples, int channels, int sampleRate)
        {
            using var stream = new MemoryStream(44 + samples.Length * 2);
            using var writer = new BinaryWriter(stream);
            int dataBytes = samples.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (var sample in samples)
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            return stream.ToArray();
        }

        public static float[] Pcm16ToFloats(byte[] bytes)
        {
            if (bytes == null) return Array.Empty<float>();
            var samples = new float[bytes.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 32768f;
            return samples;
        }

        /// <summary>Trims leading/trailing near-silence so short push-to-talk clips upload fast.</summary>
        public static float[] TrimSilence(float[] samples, float threshold = 0.01f, int padding = 1600)
        {
            int start = 0, end = samples.Length - 1;
            while (start < samples.Length && Mathf.Abs(samples[start]) < threshold) start++;
            while (end > start && Mathf.Abs(samples[end]) < threshold) end--;
            if (start >= samples.Length) return Array.Empty<float>();
            start = Mathf.Max(0, start - padding);
            end = Mathf.Min(samples.Length - 1, end + padding);
            var trimmed = new float[end - start + 1];
            Array.Copy(samples, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }
    }
}
