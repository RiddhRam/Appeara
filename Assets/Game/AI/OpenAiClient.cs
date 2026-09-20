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
    public sealed class WeaponCounterReply
    {
        public string defense;
        public string tactic;
        public string taunt;
    }

    /// <summary>
    /// OpenAI: speech → text, text → WeaponSpec (strict structured output), combat log → mothership counters.
    /// Every method returns null on any failure; callers fall back to local rules.
    /// </summary>
    public sealed class OpenAiClient
    {
        private const string OpenAiBaseUrl = "https://api.openai.com/v1";

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
- fireMode, the thing the player physically does:
  'projectile' guns and launchers; 'beam' lasers, rays, flamethrowers, continuous streams; 'thrown' grenades and
  lobbed mines; 'melee' anything swung by hand (sword, katana, axe, hammer, claws, whip) - the player swings the
  controller and damage scales with swing speed; 'bow' anything drawn and released (bow, crossbow, slingshot,
  railgun that charges) - the player holds the trigger to draw and releases to loose, and a full draw hits hardest.
  Pick melee or bow whenever the request names such a weapon; do not turn a sword into a gun.
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
            "\"fireMode\":{\"type\":\"string\",\"enum\":[\"projectile\",\"beam\",\"thrown\",\"melee\",\"bow\"]}," +
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
        /// <param name="sketchPng">Optional base64 PNG the player drew; the model sees it next to the request.</param>
        public async Awaitable<string> InterpretWeapon(string request, string battleContext, string sketchPng = null, FabricationTrace trace = null)
        {
            string user = $"Player request: \"{request}\"\nBattle context: {battleContext}";
            if (sketchPng != null) user += SketchNote;
            return await Chat(settings.WeaponModel, WeaponSystemPrompt, user, "weapon_spec", WeaponSchema, settings.WeaponTimeoutSeconds, trace, "openai.weapon_spec", sketchPng);
        }

        private static readonly string WeaponCounterSystemPrompt =
            "You are the alien Mothership studying a newly fabricated human weapon. Pick exactly one defensive counter and one tactical counter. " +
            CounterCatalog.PromptDescription + " " +
            "The defense must counter a trait the weapon actually has. Never use resist against a trait absent from the supplied trait list. " +
            "Return a menacing but funny taunt of at most 18 words that references the weapon.";

        private static string WeaponCounterSchema()
        {
            var defenses = new List<string> { "armor", "shield", "reflect" };
            defenses.AddRange(CounterCatalog.PrimitiveNames.Select(p => "resist:" + p));
            string defenseEnums = string.Join(",", defenses.Select(Http.Quote));
            string tacticEnums = string.Join(",", new[] { "dodge", "teleport", "spread", "rush", "intercept" }.Select(Http.Quote));
            return "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"defense\",\"tactic\",\"taunt\"],\"properties\":{" +
                   "\"defense\":{\"type\":\"string\",\"enum\":[" + defenseEnums + "]}," +
                   "\"tactic\":{\"type\":\"string\",\"enum\":[" + tacticEnums + "]}," +
                   "\"taunt\":{\"type\":\"string\"}}}";
        }

        /// <summary>Chooses a bounded counter package for a validated runtime weapon.</summary>
        public async Awaitable<WeaponCounterReply> AnalyzeWeaponCounters(ParsedWeapon weapon, string currentCounters, string enemies, IArmoryTrace trace = null)
        {
            if (weapon == null) return null;
            string traits = string.Join(", ", weapon.Primitives());
            string user = $"Weapon: {weapon.Name}\nMode: {weapon.FireMode}\nPayload: {weapon.Payload}\nTraits: {traits}\n" +
                          $"Rate: {weapon.FireRate:0.##}/s; count: {weapon.ProjectileCount}; spread: {weapon.SpreadDeg:0.#}; speed: {weapon.ProjectileSpeed:0.#}\n" +
                          $"Current counters: {currentCounters}\nCurrent enemies: {enemies}";
            string json = await Chat(settings.MothershipModel, WeaponCounterSystemPrompt, user, "weapon_counter", WeaponCounterSchema(),
                settings.MothershipTimeoutSeconds, trace, "openai.counter_select");
            if (json == null) return null;
            try { return JsonUtility.FromJson<WeaponCounterReply>(json); }
            catch (Exception error) { LastError = error.Message; return null; }
        }

        [Serializable] private sealed class ImageResponse { public ImageData[] data; }
        [Serializable] private sealed class ImageData { public string b64_json; }
        private const float ImageTimeoutSeconds = 35f;
        private bool imagePromptRejected;

        /// <summary>
        /// Blueprint art for a weapon. Framed as whimsical game-prop concept art: literal "weapon schematic" prompts
        /// are (reasonably) refused by the image safety system. Retries once with a nameless prompt if refused.
        /// </summary>
        public async Awaitable<byte[]> GenerateBlueprint(string weaponName, string designPrompt, FireMode fireMode, Payload payload, string colorName, string flavour, FabricationTrace trace = null)
        {
            var span = trace?.StartSpan("openai.image", "Generate weapon blueprint");
            string design = DesignDirection(designPrompt, fireMode, payload);
            string style = " Show one readable side-view silhouette facing right, constructed from 8 to 16 large simple forms. " +
                           "Use a charcoal structure, one coloured shell, and the specified emissive energy accent. Every component connects to the main body. " +
                           "No floating pieces, fine tubing, tiny greebles, or intricate shapes. Keep labels and callouts outside the prop. " +
                           "Glowing cyan and white blueprint lines on a solid black background, decorative grid, cute stat bars, stylized and cartoonish game UI hologram.";
            string prompt = $"Holographic blueprint-style concept art for a whimsical sci-fi video game prop called '{weaponName}': " +
                           $"a readable stylized retro-futuristic prop with glowing {colorName} energy cells, {flavour}. {design}." + style;
            try
            {
                span?.SetTag("ai.model", settings.ImageModel);
                var png = await Image(prompt, span);
                if (png != null) return png;
                if (!imagePromptRejected) return null;
                span?.SetTag("image.retry", "safety_fallback");
                string fallback = $"Holographic blueprint-style concept art of a stylized retro-futuristic sci-fi gadget with glowing {colorName} energy cells. {design}." + style;
                return await Image(fallback, span);
            }
            catch (Exception error) { trace?.FinishSpan(span, error); throw; }
            finally { trace?.FinishSpan(span); }
        }

        /// <summary>
        /// Transparent side-view art of the same weapon, used as the held model. Same safety framing as the
        /// blueprint: a game prop, not a schematic.
        /// </summary>
        public async Awaitable<byte[]> GenerateWeaponArt(string weaponName, string colorName, string flavour, FabricationTrace trace = null)
        {
            var span = trace?.StartSpan("openai.image", "Generate weapon art");
            const string style = " Side view, facing right, the whole prop centred and fully visible, thick clean outlines, " +
                                 "flat stylised game-asset shading, soft rim light, transparent background, no text, no labels, " +
                                 "no grid, no background scenery, no hands.";
            string prompt = $"Game asset sprite of a whimsical sci-fi video game prop called '{weaponName}': " +
                            $"a chunky, toy-like retro-futuristic gadget with glowing {colorName} energy cells and playful rounded shapes, {flavour}." + style;
            try
            {
                span?.SetTag("ai.model", settings.ImageModel);
                var png = await Image(prompt, span, transparent: true);
                if (png != null) return png;
                if (!imagePromptRejected) return null;
                span?.SetTag("image.retry", "safety_fallback");
                string fallback = $"Game asset sprite of a whimsical, toy-like retro-futuristic sci-fi gadget with glowing {colorName} energy cells." + style;
                return await Image(fallback, span, transparent: true);
            }
            catch (Exception error) { trace?.FinishSpan(span, error); throw; }
            finally { trace?.FinishSpan(span); }
        }

        private const string MeshSystemPrompt =
            "You are the station fabricator's geometry stage. You are shown the blueprint you just drew for a weapon, " +
            "and you rebuild it as real geometry out of simple primitives. Return a parts list only. " +
            "Axes, in metres: +Z points forward out of the barrel, +Y is up, +X is right. The grip sits near the origin; " +
            "the weapon extends forward to at most 0.6 m, and stays within 0.22 m left-right and 0.3 m up-down. " +
            "Use 8 to 20 substantial parts. Preserve the dominant silhouette, grip and emitter; ignore labels and tiny blueprint detail. " +
            "Every part must visibly touch or overlap the main body: never create floating decorations. Use clean rotations in 15-degree increments " +
            "and make every dimension at least 0.01 m. shape is box, cylinder, sphere, capsule, cone or disc. Cylinders and cones point " +
            "along their own Y axis, so set rx to 90 to lay one along the barrel. Scale is the full size of the part. " +
            "Use dark charcoal structure, one coloured shell and one bright accent. glow is true only for at most four energy cells, emitters or lights. " +
            "muzzle is the point the shot leaves, at the very front of the barrel.";

        private static readonly string MeshSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"parts\",\"muzzleX\",\"muzzleY\",\"muzzleZ\"],\"properties\":{" +
            "\"parts\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"shape\",\"x\",\"y\",\"z\",\"rx\",\"ry\",\"rz\",\"sx\",\"sy\",\"sz\",\"color\",\"glow\"],\"properties\":{" +
            "\"shape\":{\"type\":\"string\",\"enum\":[\"box\",\"cylinder\",\"sphere\",\"capsule\",\"cone\",\"disc\"]}," +
            "\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}," +
            "\"rx\":{\"type\":\"number\"},\"ry\":{\"type\":\"number\"},\"rz\":{\"type\":\"number\"}," +
            "\"sx\":{\"type\":\"number\"},\"sy\":{\"type\":\"number\"},\"sz\":{\"type\":\"number\"}," +
            "\"color\":{\"type\":\"string\"},\"glow\":{\"type\":\"boolean\"}}}}," +
            "\"muzzleX\":{\"type\":\"number\"},\"muzzleY\":{\"type\":\"number\"},\"muzzleZ\":{\"type\":\"number\"}}}";

        /// <summary>Reads the blueprint it just produced and returns the weapon as primitives Unity can build.</summary>
        public async Awaitable<string> DescribeWeaponMesh(string weaponName, string designPrompt, FireMode fireMode, Payload payload, string traits, byte[] blueprintPng, FabricationTrace trace = null)
        {
            string user = $"Weapon: {weaponName}. Player concept: {designPrompt}. Traits: {traits}. {DesignDirection(designPrompt, fireMode, payload)}. Rebuild the weapon in the blueprint as primitives.";
            string image = blueprintPng != null ? Convert.ToBase64String(blueprintPng) : null;
            return await Chat(settings.WeaponModel, MeshSystemPrompt, user, "weapon_mesh", MeshSchema, settings.WeaponTimeoutSeconds,
                trace, "openai.weapon_mesh", image);
        }

        private static string DesignDirection(string weaponName, FireMode fireMode, Payload payload) =>
            ArchetypeDirection(weaponName, fireMode, payload) + ". " + ShapeLanguage(weaponName);

        private static string ArchetypeDirection(string weaponName, FireMode fireMode, Payload payload)
        {
            string name = weaponName?.ToLowerInvariant() ?? "";
            if (name.Contains("pistol") || name.Contains("handgun") || name.Contains("sidearm") || name.Contains("revolver"))
                return "Use a compact one-handed pistol silhouette: short barrel, small receiver, distinct angled grip, no shoulder stock and no oversized front tube";
            if (name.Contains("launcher") || name.Contains("bazooka") || name.Contains("rocket") || name.Contains("cannon") || name.Contains("mortar"))
                return "Use a long heavy launcher silhouette dominated by one oversized hollow forward tube, a substantial payload chamber, rear shoulder stock and a small grip beneath the body; never add a rifle magazine, thin barrel or machine-gun barrel cluster";
            if (name.Contains("machine gun") || name.Contains("machinegun") || name.Contains("minigun") || name.Contains("gatling"))
                return "Use a long low machine-gun silhouette with a narrow barrel or barrel cluster, rectangular receiver, obvious box or drum magazine, rear stock and forward support; never use one oversized hollow launcher tube";

            switch (fireMode)
            {
                case FireMode.Beam: return "Build a continuous-energy tool with a clear grip, focusing chamber and forward emitter";
                case FireMode.Thrown: return "Build a compact hand-held device with an obvious grasp area, payload body and forward or upward detonator";
                case FireMode.Melee: return "Build a balanced hand weapon with a clear hilt, guard and blade or striking head; do not add a gun barrel";
                case FireMode.Bow: return "Build a bow-like hand weapon with a central grip, two readable limbs and an energy nock; do not turn it into a rifle";
                default: return payload == Payload.Explosive
                    ? "Build a shoulder-fired explosive launcher with a large bore, long payload body and rear stock; avoid a compact pistol silhouette"
                    : "Build a readable ranged tool with a clear grip, main body and forward muzzle; let the weapon name determine whether it is compact, rifle-length or heavy";
            }
        }

        /// <summary>A stable name-derived art direction adds variety without changing between repeated generations.</summary>
        private static string ShapeLanguage(string weaponName)
        {
            int hash = 17;
            foreach (char character in (weaponName ?? "weapon").ToLowerInvariant())
                hash = unchecked(hash * 31 + character);

            switch ((hash & int.MaxValue) % 5)
            {
                case 0:
                    return "Rounded orbital design language: spheres, capsules, a circular energy core and a soft continuous silhouette; avoid fins and boxy rails";
                case 1:
                    return "Angular wedge design language: stepped box forms, tapered cones and two bold fins creating a sharp triangular silhouette; avoid round central bodies";
                case 2:
                    return "Industrial tubular design language: one dominant cylinder, exposed tanks and thick collar rings with a practical heavy silhouette";
                case 3:
                    return "Skeletal rail design language: two separated parallel rails, deliberate open gaps and a slim energy core joined by a few strong bridge pieces";
                default:
                    return "Connected asymmetric design language: one large side-mounted cell or drum and an offset top module producing an intentionally uneven silhouette";
            }
        }

        private async Awaitable<byte[]> Image(string prompt, Sentry.ISpan span = null, bool transparent = false)
        {
            imagePromptRejected = false;
            string body = "{\"model\":" + Http.Quote(settings.ImageModel) + ",\"prompt\":" + Http.Quote(prompt) +
                          ",\"size\":\"1024x1024\",\"quality\":" + Http.Quote(settings.ImageQuality) + ",\"n\":1" +
                          (transparent ? ",\"background\":\"transparent\",\"output_format\":\"png\"" : "") + "}";
            var request = Http.PostJson(Api("/images/generations"), body);
            Authorize(request);
            var result = await Http.Send(request, ImageTimeoutSeconds, span, settings.UsesGateway);
            span?.SetTag("http.status_code", result.Code.ToString());
            if (!result.Ok)
            {
                string response = result.Text ?? "";
                imagePromptRejected = result.Code == 400 &&
                    (response.IndexOf("safety", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     response.IndexOf("content_policy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     response.IndexOf("moderation", StringComparison.OrdinalIgnoreCase) >= 0);
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

        private const string SketchNote = " The player also sketched the weapon; use the drawing for its shape, silhouette and parts.";

        private async Awaitable<string> Chat(string model, string system, string user, string schemaName, string schema, float timeout,
            IArmoryTrace trace = null, string operation = "openai.chat", string imagePng = null)
        {
            var span = trace?.StartSpan(operation, schemaName);
            span?.SetTag("ai.model", model);
            var body = new StringBuilder();
            body.Append("{\"model\":").Append(Http.Quote(model));
            if (!string.IsNullOrEmpty(settings.ReasoningEffort)) body.Append(",\"reasoning_effort\":").Append(Http.Quote(settings.ReasoningEffort));
            body.Append(",\"messages\":[{\"role\":\"system\",\"content\":").Append(Http.Quote(system))
                .Append("},{\"role\":\"user\",\"content\":");
            // A sketch rides along as vision input so the drawing shapes the weapon.
            if (imagePng == null) body.Append(Http.Quote(user));
            else
                body.Append("[{\"type\":\"text\",\"text\":").Append(Http.Quote(user))
                    .Append("},{\"type\":\"image_url\",\"image_url\":{\"url\":")
                    .Append(Http.Quote("data:image/png;base64," + imagePng)).Append("}}]");
            body.Append("}]");
            body.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":").Append(Http.Quote(schemaName))
                .Append(",\"strict\":true,\"schema\":").Append(schema).Append("}}}");

            try
            {
                var request = Http.PostJson(Api("/chat/completions"), body.ToString());
                Authorize(request);
                float started = Time.realtimeSinceStartup;
                var result = await Http.Send(request, timeout, span, settings.UsesGateway);
                LastLatency = Time.realtimeSinceStartup - started;
                span?.SetTag("http.status_code", result.Code.ToString());
                if (!result.Ok)
                {
                    LastError = $"OpenAI {result.Code}: {result.Error} {Truncate(result.Text)}";
                    span?.SetTag("outcome", "error");
                    Debug.LogWarning(LastError);
                    return null;
                }
                var response = JsonUtility.FromJson<ChatResponse>(result.Text);
                var message = response?.choices?.FirstOrDefault()?.message;
                if (message == null || !string.IsNullOrEmpty(message.refusal))
                {
                    LastError = "refused: " + message?.refusal;
                    span?.SetTag("outcome", "refused");
                    return null;
                }
                span?.SetTag("outcome", "ok");
                return message.content;
            }
            catch (Exception error)
            {
                LastError = "OpenAI parse: " + error.Message;
                span?.SetTag("outcome", "exception");
                return null;
            }
            finally { trace?.FinishSpan(span); }
        }

        public async Awaitable<string> Transcribe(byte[] wav, FabricationTrace trace = null)
        {
            var span = trace?.StartSpan("openai.transcribe", "Speech to weapon request");
            span?.SetTag("ai.model", settings.TranscribeModel);
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model", settings.TranscribeModel),
                new MultipartFormDataSection("prompt", "A player asking a starship AI to build a weapon: shotgun, railgun, plasma, sticky mines, homing missiles, cryo, EMP, bouncing grenade, lightning."),
                new MultipartFormFileSection("file", wav, "speech.wav", "audio/wav"),
            };
            try
            {
                var request = UnityWebRequest.Post(Api("/audio/transcriptions"), form);
                Authorize(request);
                float started = Time.realtimeSinceStartup;
                var result = await Http.Send(request, 10f, span, settings.UsesGateway);
                LastLatency = Time.realtimeSinceStartup - started;
                span?.SetTag("http.status_code", result.Code.ToString());
                if (!result.Ok)
                {
                    LastError = $"Transcribe {result.Code}: {result.Error} {Truncate(result.Text)}";
                    Debug.LogWarning(LastError);
                    return null;
                }
                return JsonUtility.FromJson<TranscriptResponse>(result.Text)?.text?.Trim();
            }
            catch (Exception error) { LastError = error.Message; span?.SetTag("outcome", "exception"); return null; }
            finally { trace?.FinishSpan(span); }
        }

        private string Api(string path) => settings.UsesGateway ? settings.GatewayBaseUrl + "/openai/v1" + path : OpenAiBaseUrl + path;

        private void Authorize(UnityWebRequest request)
        {
            if (settings.UsesGateway)
            {
                if (!string.IsNullOrEmpty(settings.GatewayToken)) request.SetRequestHeader("X-Armory-Gateway-Key", settings.GatewayToken);
            }
            else if (!string.IsNullOrEmpty(key)) request.SetRequestHeader("Authorization", "Bearer " + key);
        }

        private static string Truncate(string text) => text == null ? "" : text.Length > 300 ? text.Substring(0, 300) : text;
    }
}
