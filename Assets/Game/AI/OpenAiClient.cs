using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Armory.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Armory.AI
{
    [Serializable]
    public sealed class MothershipReply
    {
        public string[] counters;
        public string taunt;
    }

    /// <summary>
    /// OpenAI: speech → text, text → WeaponSpec (strict structured output), combat log → mothership counters.
    /// Every method returns null on any failure; callers fall back to local rules.
    /// </summary>
    public sealed class OpenAiClient
    {
        private const string ChatUrl = "https://api.openai.com/v1/chat/completions";
        private const string TranscribeUrl = "https://api.openai.com/v1/audio/transcriptions";

        private readonly string key;
        private readonly AiSettings settings;

        public string LastError { get; private set; }
        public float LastLatency { get; private set; }

        public OpenAiClient(string key, AiSettings settings)
        {
            this.key = key;
            this.settings = settings;
        }

        private const string WeaponSystemPrompt =
@"You are ARIA, the fabrication AI of a space station under alien siege. The player describes a weapon out loud.
Convert it into a WeaponSpec the station's modular fabricator can build. Be faithful to the request; be creative with the name and visuals.
- fireMode: 'projectile' (guns, launchers), 'beam' (lasers, rays, flamethrowers, continuous streams), 'thrown' (grenades, lobbed mines, anything arcing).
- payload: the damage type. kinetic=bullets/slugs, explosive=rockets/grenades/bombs, plasma=energy bolts/fire, electric=lightning/EMP/tesla, cryo=ice/freeze.
- modifiers (delivery): homing, piercing, bouncing, sticky, proximity. Only include what the request implies.
- onHit: splash (area), chain (arcs to nearby enemies), slow.
- fireRate shots/sec 0.5-15. projectileCount per shot 1-12 (shotguns 6-10). spreadDeg 0-45. projectileSpeed m/s (thrown 8-20, bullets 30-80).
- damage per projectile 1-100. The fabricator auto-balances total power, so pick values that fit the fantasy rather than maxing out.
- pierceCount/bounceCount/chainCount 1-5 (3 if unspecified). splashRadius 1-6 m.
- visual.body and visual.barrel: 0-5, vary them. primaryColor: hex like #FF7A1A that fits the payload. projectileShape orb|bolt|disc|mine. trail true for fast shots.
- shipAILine: one witty sentence (max 20 words) you say while handing it over. If the weapon is a poor match for the current enemies or adaptations, warn briefly.
- sfxPrompt: max 12 words describing the firing sound for a sound-effect generator (no music).
If the request is vague or not a weapon, build the closest fun weapon anyway.";

        private static readonly string WeaponSchema =
            "{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"name\",\"shipAILine\",\"fireMode\",\"payload\",\"modifiers\",\"onHit\",\"fireRate\",\"projectileCount\",\"spreadDeg\",\"projectileSpeed\",\"damage\",\"pierceCount\",\"bounceCount\",\"chainCount\",\"splashRadius\",\"visual\",\"sfxPrompt\"]," +
            "\"properties\":{" +
            "\"name\":{\"type\":\"string\"},\"shipAILine\":{\"type\":\"string\"}," +
            "\"fireMode\":{\"type\":\"string\",\"enum\":[\"projectile\",\"beam\",\"thrown\"]}," +
            "\"payload\":{\"type\":\"string\",\"enum\":[\"kinetic\",\"explosive\",\"plasma\",\"electric\",\"cryo\"]}," +
            "\"modifiers\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"homing\",\"piercing\",\"bouncing\",\"sticky\",\"proximity\"]}}," +
            "\"onHit\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"splash\",\"chain\",\"slow\"]}}," +
            "\"fireRate\":{\"type\":\"number\"},\"projectileCount\":{\"type\":\"integer\"},\"spreadDeg\":{\"type\":\"number\"}," +
            "\"projectileSpeed\":{\"type\":\"number\"},\"damage\":{\"type\":\"number\"}," +
            "\"pierceCount\":{\"type\":\"integer\"},\"bounceCount\":{\"type\":\"integer\"},\"chainCount\":{\"type\":\"integer\"},\"splashRadius\":{\"type\":\"number\"}," +
            "\"visual\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"body\",\"barrel\",\"primaryColor\",\"projectileShape\",\"trail\"],\"properties\":{" +
            "\"body\":{\"type\":\"integer\"},\"barrel\":{\"type\":\"integer\"},\"primaryColor\":{\"type\":\"string\"}," +
            "\"projectileShape\":{\"type\":\"string\",\"enum\":[\"orb\",\"bolt\",\"disc\",\"mine\"]},\"trail\":{\"type\":\"boolean\"}}}," +
            "\"sfxPrompt\":{\"type\":\"string\"}}}";

        /// <returns>Raw WeaponSpec JSON, or null.</returns>
        public async Awaitable<string> InterpretWeapon(string request, string battleContext)
        {
            string user = $"Player request: \"{request}\"\nBattle context: {battleContext}";
            return await Chat(settings.WeaponModel, WeaponSystemPrompt, user, "weapon_spec", WeaponSchema, settings.WeaponTimeoutSeconds);
        }

        private static readonly string MothershipSystemPrompt =
            "You are the alien Mothership hive mind besieging a human space station. After each wave you study which weapon traits hurt you most and evolve counters.\n" +
            "Counters: armor (tougher hides, +HP), shield (energy shields only electric pierces), dodge (sidestep unguided projectiles), teleport (blink forward; beats homing and slow), " +
            "spread (loose formation; beats splash, chain, piercing), rush (faster; beats slow and cryo), intercept (shoot down slow projectiles, mines, grenades), " +
            "reflect (mirror plating vs beams and plasma), resist:<trait> (70% less damage from that trait).\n" +
            "Pick 1-3 counters aimed at the traits that dealt the most damage. Avoid re-picking counters already active.\n" +
            "taunt: one menacing but slightly funny sentence (max 18 words) addressed to the humans, referencing what they used.";

        private static string MothershipSchema()
        {
            var options = new List<string> { "armor", "shield", "dodge", "teleport", "spread", "rush", "intercept", "reflect" };
            options.AddRange(new[] { "kinetic", "explosive", "plasma", "electric", "cryo", "beam", "thrown", "homing", "piercing", "bouncing", "sticky", "proximity", "splash", "chain", "slow" }.Select(p => "resist:" + p));
            string enumList = string.Join(",", options.Select(Http.Quote));
            return "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"counters\",\"taunt\"],\"properties\":{" +
                   "\"counters\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[" + enumList + "]}}," +
                   "\"taunt\":{\"type\":\"string\"}}}";
        }

        public async Awaitable<MothershipReply> Adapt(string combatSummary, string activeCounters, string nextWave)
        {
            string user = $"Damage share by trait this wave: {combatSummary}\nAlready active counters: {activeCounters}\nNext wave: {nextWave}";
            string json = await Chat(settings.MothershipModel, MothershipSystemPrompt, user, "mothership_adaptation", MothershipSchema(), settings.MothershipTimeoutSeconds);
            if (json == null) return null;
            try { return JsonUtility.FromJson<MothershipReply>(json); }
            catch (Exception error) { LastError = error.Message; return null; }
        }

        [Serializable] private sealed class ImageResponse { public ImageData[] data; }
        [Serializable] private sealed class ImageData { public string b64_json; }

        /// <summary>
        /// Blueprint art for a weapon. Framed as whimsical game-prop concept art: literal "weapon schematic" prompts
        /// are (reasonably) refused by the image safety system. Retries once with a nameless prompt if refused.
        /// </summary>
        public async Awaitable<byte[]> GenerateBlueprint(string weaponName, string colorName, string flavour)
        {
            string style = " Side view line drawing in glowing cyan and white lines on a solid black background, decorative grid, " +
                           "made-up annotation labels and cute stat bars, stylized and cartoonish, like a game UI hologram.";
            string prompt = $"Holographic blueprint-style concept art for a whimsical sci-fi video game prop called '{weaponName}': " +
                            $"a chunky, toy-like retro-futuristic gadget with glowing {colorName} energy cells and playful rounded shapes, {flavour}." + style;
            var png = await Image(prompt);
            if (png != null) return png;
            string fallback = $"Holographic blueprint-style concept art of a whimsical, toy-like retro-futuristic sci-fi gadget with glowing {colorName} energy cells." + style;
            return await Image(fallback);
        }

        private async Awaitable<byte[]> Image(string prompt)
        {
            string body = "{\"model\":" + Http.Quote(settings.ImageModel) + ",\"prompt\":" + Http.Quote(prompt) +
                          ",\"size\":\"1024x1024\",\"quality\":" + Http.Quote(settings.ImageQuality) + ",\"n\":1}";
            var request = Http.PostJson("https://api.openai.com/v1/images/generations", body);
            request.SetRequestHeader("Authorization", "Bearer " + key);
            var result = await Http.Send(request, 60f);
            if (!result.Ok)
            {
                LastError = $"Image {result.Code}: {Truncate(result.Text)}";
                Debug.LogWarning(LastError);
                return null;
            }
            try
            {
                var b64 = JsonUtility.FromJson<ImageResponse>(result.Text)?.data?.FirstOrDefault()?.b64_json;
                return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
            }
            catch (Exception error) { LastError = "Image parse: " + error.Message; return null; }
        }

        [Serializable] private sealed class ChatResponse { public Choice[] choices; }
        [Serializable] private sealed class Choice { public ChatMessage message; }
        [Serializable] private sealed class ChatMessage { public string content; public string refusal; }
        [Serializable] private sealed class TranscriptResponse { public string text; }

        private async Awaitable<string> Chat(string model, string system, string user, string schemaName, string schema, float timeout)
        {
            var body = new StringBuilder();
            body.Append("{\"model\":").Append(Http.Quote(model));
            if (!string.IsNullOrEmpty(settings.ReasoningEffort)) body.Append(",\"reasoning_effort\":").Append(Http.Quote(settings.ReasoningEffort));
            body.Append(",\"messages\":[{\"role\":\"system\",\"content\":").Append(Http.Quote(system))
                .Append("},{\"role\":\"user\",\"content\":").Append(Http.Quote(user)).Append("}]");
            body.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":").Append(Http.Quote(schemaName))
                .Append(",\"strict\":true,\"schema\":").Append(schema).Append("}}}");

            var request = Http.PostJson(ChatUrl, body.ToString());
            request.SetRequestHeader("Authorization", "Bearer " + key);
            float started = Time.realtimeSinceStartup;
            var result = await Http.Send(request, timeout);
            LastLatency = Time.realtimeSinceStartup - started;
            if (!result.Ok)
            {
                LastError = $"OpenAI {result.Code}: {result.Error} {Truncate(result.Text)}";
                Debug.LogWarning(LastError);
                return null;
            }
            try
            {
                var response = JsonUtility.FromJson<ChatResponse>(result.Text);
                var message = response?.choices?.FirstOrDefault()?.message;
                if (message == null || !string.IsNullOrEmpty(message.refusal)) { LastError = "refused: " + message?.refusal; return null; }
                return message.content;
            }
            catch (Exception error)
            {
                LastError = "OpenAI parse: " + error.Message;
                return null;
            }
        }

        public async Awaitable<string> Transcribe(byte[] wav)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model", settings.TranscribeModel),
                new MultipartFormDataSection("prompt", "A player asking a starship AI to build a weapon: shotgun, railgun, plasma, sticky mines, homing missiles, cryo, EMP, bouncing grenade, lightning."),
                new MultipartFormFileSection("file", wav, "speech.wav", "audio/wav"),
            };
            var request = UnityWebRequest.Post(TranscribeUrl, form);
            request.SetRequestHeader("Authorization", "Bearer " + key);
            float started = Time.realtimeSinceStartup;
            var result = await Http.Send(request, 10f);
            LastLatency = Time.realtimeSinceStartup - started;
            if (!result.Ok)
            {
                LastError = $"Transcribe {result.Code}: {result.Error} {Truncate(result.Text)}";
                Debug.LogWarning(LastError);
                return null;
            }
            try { return JsonUtility.FromJson<TranscriptResponse>(result.Text)?.text?.Trim(); }
            catch (Exception error) { LastError = error.Message; return null; }
        }

        private static string Truncate(string text) => text == null ? "" : text.Length > 300 ? text.Substring(0, 300) : text;
    }
}
