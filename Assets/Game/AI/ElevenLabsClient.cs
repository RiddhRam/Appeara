using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Armory.Core;
using UnityEngine;

namespace Armory.AI
{
    /// <summary>
    /// ElevenLabs TTS + sound-effect generation. Requests raw PCM (16-bit mono 22.05 kHz) so clips build directly
    /// without MP3 decoding. Results are cached on disk by content hash so repeated lines/weapons cost nothing.
    /// </summary>
    public sealed class ElevenLabsClient
    {
        private const int SampleRate = 22050;
        private const string Format = "pcm_22050";
        private readonly string key;
        private readonly AiSettings settings;
        private readonly string cacheDir;

        public string LastError { get; private set; }

        public ElevenLabsClient(string key, AiSettings settings)
        {
            this.key = key;
            this.settings = settings;
            cacheDir = Path.Combine(Application.temporaryCachePath, "armory-audio");
            Directory.CreateDirectory(cacheDir);
        }

        public async Awaitable<AudioClip> Speak(string text, string voiceId, float stability = 0.5f, float style = 0.3f)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string body = "{\"text\":" + Http.Quote(text) + ",\"model_id\":" + Http.Quote(settings.TtsModel) +
                          ",\"voice_settings\":{\"stability\":" + F(stability) + ",\"similarity_boost\":0.8,\"style\":" + F(style) + "}}";
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format={Format}";
            return await Fetch(url, body, "tts:" + voiceId + ":" + text, "voice", 12f);
        }

        public async Awaitable<AudioClip> SoundEffect(string prompt, float seconds)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            string body = "{\"text\":" + Http.Quote(prompt) + ",\"duration_seconds\":" + F(Mathf.Clamp(seconds, 0.5f, 5f)) + ",\"prompt_influence\":0.6}";
            string url = $"https://api.elevenlabs.io/v1/sound-generation?output_format={Format}";
            return await Fetch(url, body, "sfx:" + seconds + ":" + prompt, "sfx", 15f);
        }

        private async Awaitable<AudioClip> Fetch(string url, string body, string cacheKey, string clipName, float timeout)
        {
            string path = Path.Combine(cacheDir, Hash(cacheKey) + ".pcm");
            byte[] pcm = null;
            if (File.Exists(path)) pcm = File.ReadAllBytes(path);
            else
            {
                var request = Http.PostJson(url, body);
                request.SetRequestHeader("xi-api-key", key);
                var result = await Http.Send(request, timeout);
                if (!result.Ok || result.Data == null || result.Data.Length < 200)
                {
                    LastError = $"ElevenLabs {result.Code}: {result.Error} {result.Text}";
                    Debug.LogWarning(LastError);
                    return null;
                }
                pcm = result.Data;
                try { File.WriteAllBytes(path, pcm); } catch (IOException) { }
            }
            return ProceduralSfx.FromSamples(clipName, WavPcm.Pcm16ToFloats(pcm), SampleRate);
        }

        private static string F(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private static string Hash(string text)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
