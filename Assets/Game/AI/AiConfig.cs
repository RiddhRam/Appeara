using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Armory.AI
{
    /// <summary>Model and voice choices. Defaults verified against the team's keys on 2026-09-19.</summary>
    [Serializable]
    public sealed class AiSettings
    {
        [Tooltip("Force offline mock AI (keyword interpreter, rule-based mothership, subtitles only).")]
        public bool Offline;
        public string WeaponModel = "gpt-5.4-mini";
        public string MothershipModel = "gpt-5.4-mini";
        [Tooltip("Leave empty for non-reasoning models such as gpt-4.1-mini.")]
        public string ReasoningEffort = "none";
        public string TranscribeModel = "gpt-4o-mini-transcribe";
        public string TtsModel = "eleven_flash_v2_5";
        [Tooltip("ElevenLabs 'Alice - Clear, Engaging Educator' (British, crisp: onboard computer).")]
        public string ShipVoiceId = "Xb7hH8MSUJpSbSDYk0k2";
        [Tooltip("ElevenLabs 'Callum - Husky Trickster', pitched down in Unity for the alien hive mind.")]
        public string MothershipVoiceId = "N2lVS1w4EtoT3dr4eOWO";
        public float MothershipPitch = 0.82f;
        public bool GenerateWeaponSfx = true;
        [Tooltip("AI-generated blueprint hologram per weapon. gpt-image-2 ≈ 20 s (best), gpt-image-1-mini ≈ 8 s.")]
        public bool GenerateBlueprints = true;
        public string ImageModel = "gpt-image-2";
        public string ImageQuality = "low";
        public bool Speak = true;
        [Tooltip("Optional armory gateway base URL, e.g. https://armory.example.com. When present, provider keys stay on the gateway.")]
        public string GatewayUrl = "";
        [Tooltip("Optional shared demo token for the gateway. This is not a provider key; leave empty only on a private LAN.")]
        public string GatewayToken = "";
        [Tooltip("Force a microphone whose name contains this text; empty = auto (headset first, then any working device).")]
        public string MicDeviceContains = "";
        public float WeaponTimeoutSeconds = 10f;
        public float MothershipTimeoutSeconds = 8f;

        public bool UsesGateway => !string.IsNullOrWhiteSpace(GatewayUrl);
        public string GatewayBaseUrl => GatewayUrl?.TrimEnd('/');
    }

    /// <summary>Loads API keys from UserSettings/ArmoryKeys.json (gitignored). Never logged.</summary>
    [Serializable]
    public sealed class ArmoryKeys
    {
        public string openai;
        public string elevenlabs;
        // DSNs identify a telemetry project, not a secret. Keeping this in UserSettings still lets each demo team
        // point at its own project without baking configuration into a build.
        public string sentry;
        public string sentryEnvironment = "demo";

        public bool HasOpenAI => !string.IsNullOrEmpty(openai) && !openai.StartsWith("PASTE_");
        public bool HasElevenLabs => !string.IsNullOrEmpty(elevenlabs) && !elevenlabs.StartsWith("PASTE_");
        public bool HasSentry => !string.IsNullOrEmpty(sentry) && !sentry.StartsWith("PASTE_");

        public static ArmoryKeys Load()
        {
            string[] candidates =
            {
                Path.Combine(Application.dataPath, "..", "UserSettings", "ArmoryKeys.json"),
                Path.Combine(Application.dataPath, "..", "ArmoryKeys.json"),
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var keys = JsonUtility.FromJson<ArmoryKeys>(File.ReadAllText(path));
                    keys.openai = keys.openai?.Trim();
                    keys.elevenlabs = keys.elevenlabs?.Trim();
                    keys.sentry = keys.sentry?.Trim();
                    keys.sentryEnvironment = string.IsNullOrWhiteSpace(keys.sentryEnvironment) ? "demo" : keys.sentryEnvironment.Trim();
                    return keys;
                }
                catch (Exception error)
                {
                    Debug.LogWarning("ArmoryKeys.json is not valid JSON: " + error.Message);
                }
            }
            return new ArmoryKeys();
        }
    }

    public readonly struct HttpResult
    {
        public readonly bool Ok;
        public readonly long Code;
        public readonly string Text;
        public readonly byte[] Data;
        public readonly string Error;

        public HttpResult(bool ok, long code, string text, byte[] data, string error)
        {
            Ok = ok; Code = code; Text = text; Data = data; Error = error;
        }
    }

    public static class Http
    {
        public static async Awaitable<HttpResult> Send(UnityWebRequest request, float timeoutSeconds, Sentry.ISpan span = null, bool propagateTrace = false)
        {
            using (request)
            {
                // Only the gateway receives tracing headers. Sending them to model providers would not continue the
                // trace and would unnecessarily disclose deployment metadata to third parties.
                if (propagateTrace && span != null)
                {
                    request.SetRequestHeader("sentry-trace", span.GetTraceHeader().ToString());
                    var baggage = Sentry.Unity.SentrySdk.GetBaggage();
                    if (baggage != null) request.SetRequestHeader("baggage", baggage.ToString());
                }
                request.timeout = Mathf.CeilToInt(timeoutSeconds);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Awaitable.NextFrameAsync();
                bool ok = request.result == UnityWebRequest.Result.Success;
                var handler = request.downloadHandler;
                string text = handler != null && !(handler is DownloadHandlerAudioClip) ? SafeText(handler) : null;
                return new HttpResult(ok, request.responseCode, text, handler?.data, ok ? null : request.error);
            }
        }

        public static UnityWebRequest PostJson(string url, string json)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private static string SafeText(DownloadHandler handler)
        {
            try { return handler.text; }
            catch { return null; }
        }

        /// <summary>JSON string literal with escaping (request bodies are hand-built; no Newtonsoft).</summary>
        public static string Quote(string value)
        {
            if (value == null) return "null";
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}
