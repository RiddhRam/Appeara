using System.Collections;
using System.Collections.Generic;
using System.Text;
using Armory.AI;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// The ship's fabrication AI. Push-to-talk → transcription → WeaponSpec → assembled weapon, plus the voices of
    /// the ship and the mothership. Every step degrades to an offline fallback; firing never waits on the network.
    /// </summary>
    public sealed class ShipAI : MonoBehaviour
    {
        public static ShipAI Instance { get; private set; }

        public ArmoryRig Rig;
        public AiSettings Settings = new AiSettings();

        public OpenAiClient OpenAI { get; private set; }
        public ElevenLabsClient ElevenLabs { get; private set; }
        public Weapon Current { get; private set; }
        public bool Busy { get; private set; }
        /// <summary>Test aid (bridge "autofire"): aims at the nearest alien and holds the trigger.</summary>
        public bool DebugAutoFire { get; set; }
        private Transform debugAim;
        public bool Listening => Mic != null && Mic.Recording;
        public MicCapture Mic { get; private set; }
        /// <summary>Live input level 0-1 while talking, for the HUD meter.</summary>
        public float MicLevel => Mic != null ? Mic.Level : 0f;
        public AudioSource ShipVoice => shipVoice;
        public AudioSource MothershipVoice => motherVoice;
        public string Status { get; private set; } = "";
        public string LastTranscript { get; private set; } = "";
        public readonly List<(string speaker, string text)> Subtitles = new List<(string, string)>();

        public static readonly string[] CannedPrompts =
        {
            "Make me a machine gun",
            "A bouncing plasma grenade launcher",
            "A railgun that pierces three enemies",
            "A shotgun that fires sticky mines which explode when aliens get close",
            "Homing lightning missiles that chain between aliens",
        };

        /// <summary>One of these is fabricated automatically when a new run begins.</summary>
        public static readonly string[] InitialWeaponPrompts =
        {
            "A donut that I throw at enemies",
            "A goose that throws eggs at enemies",
            "A machine gun",
        };

        private AudioSource shipVoice;
        private AudioSource motherVoice;
        private AudioSource fabHum;
        private readonly Queue<(AudioSource source, Awaitable<AudioClip> clip, FabricationTrace trace)> voiceQueue = new Queue<(AudioSource, Awaitable<AudioClip>, FabricationTrace)>();
        private GameObject hologram;
        private BlueprintHologram wristBlueprint;
        private BlueprintHologram coreBlueprint;
        private int blueprintRequest;

        // Always-on looping mic; push-to-talk just marks start/end positions, so the first word is never clipped.
        private bool probing;
        private bool textEntryOpen;
        private string typed = "";
        private FabricationTrace activeFabrication;
        private Sentry.ISpan micSpan;

        private void Awake()
        {
            Instance = this;
            var keys = ArmoryKeys.Load();
            if (!Settings.Offline && (keys.HasOpenAI || Settings.UsesGateway)) OpenAI = new OpenAiClient(keys.openai, Settings);
            if (!Settings.Offline && (keys.HasElevenLabs || Settings.UsesGateway)) ElevenLabs = new ElevenLabsClient(keys.elevenlabs, Settings);
            if (OpenAI == null) Debug.LogWarning("ShipAI: OpenAI offline (no key or Offline set). Using keyword interpreter.");
            if (ElevenLabs == null) Debug.LogWarning("ShipAI: ElevenLabs offline. Subtitles only.");

            shipVoice = MakeSource("Ship Voice", 1f);
            motherVoice = MakeSource("Mothership Voice", Settings.MothershipPitch);
            motherVoice.gameObject.AddComponent<AudioDistortionFilter>().distortionLevel = 0.25f;
            motherVoice.gameObject.AddComponent<AudioEchoFilter>().delay = 60f;
            fabHum = MakeSource("Fabricator", 1f);
        }

        private AudioSource MakeSource(string sourceName, float pitch)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.pitch = pitch;
            source.playOnAwake = false;
            return source;
        }

        private void Start()
        {
            StartMic();
            // Small projection above the left wrist + a big one over the station's central projector for the audience.
            wristBlueprint = BlueprintHologram.Create("Wrist Blueprint", Rig.LeftHand, new Vector3(0f, 0.36f, 0.02f), 0.26f, worldPosition: false, yawOnly: false);
            var center = ArmoryGame.Instance != null ? ArmoryGame.Instance.transform.position : Vector3.zero;
            coreBlueprint = BlueprintHologram.Create("Core Blueprint", transform, center + Vector3.up * 7f, 6f, worldPosition: true, yawOnly: true);
            StartCoroutine(PlayVoices());
            SayShip(OpenAI != null
                ? "Fabricator online. Randomizing your starter weapon."
                : "Fabricator offline. Randomizing your starter weapon.", "ARIA ONLINE");
            _ = Fabricate(PickInitialWeaponPrompt(), playerRequested: false);
        }

        public static string PickInitialWeaponPrompt() =>
            InitialWeaponPrompts[UnityEngine.Random.Range(0, InitialWeaponPrompts.Length)];

        private void StartMic()
        {
            Mic = new MicCapture(Settings.MicDeviceContains);
            if (!Mic.Start()) Status = Mic.LastReport + " - press T to type";
        }

        /// <summary>Records from every device in turn and logs levels (bridge "miclevels" / Armory menu).</summary>
        public void ProbeMics()
        {
            if (probing) return;
            probing = true;
            Status = "Testing microphones - speak now";
            StartCoroutine(Run());

            IEnumerator Run()
            {
                Mic.Stop();
                yield return MicCapture.Probe(2f, report =>
                {
                    probing = false;
                    Status = report.Contains("Best device") ? report.Substring(report.IndexOf("Best device")) : "No mic produced audio";
                    Mic.Start();
                });
            }
        }

        private void Update()
        {
            if (Rig == null) return;
            Rig.TextEntryActive = textEntryOpen;

            if (Current != null)
            {
                if (DebugAutoFire) AutoFire();
                else Current.Tick(Rig.FireHeld && !MissionDeck.Open && !UiPointer.OverButton, Rig.Aim);
            }

            if (Mic != null && Mic.Recording) Mic.Tick();
            if (Rig.TalkHeld && Mic != null && !Mic.Recording && !Busy && !probing) BeginRecording();
            else if (!Rig.TalkHeld && Mic != null && Mic.Recording) EndRecording();

            if (Rig.CannedPromptPressed >= 0 && !Busy) _ = Fabricate(CannedPrompts[Rig.CannedPromptPressed]);
            if (Rig.TypePressed && !Busy) { textEntryOpen = true; typed = ""; }
            if (Rig.ReadyPressed) WaveDirector.Instance?.RequestWaveStart();

            if (hologram != null)
            {
                hologram.transform.position = Rig.Aim.position + Rig.Aim.forward * 0.12f;
                hologram.transform.Rotate(90f * Time.deltaTime, 140f * Time.deltaTime, 0f);
            }
        }

        private void AutoFire()
        {
            if (debugAim == null) debugAim = new GameObject("Debug Aim").transform;
            var target = Enemy.Nearest(Rig.Aim.position, 80f);
            debugAim.position = Rig.Aim.position;
            if (target != null)
            {
                Vector3 lead = target.Center - Rig.Aim.position;
                // Lob thrown weapons a bit higher so they land near the target.
                if (Current.Spec.FireMode == FireMode.Thrown) lead.y += lead.magnitude * 0.35f;
                debugAim.rotation = Quaternion.LookRotation(lead);
            }
            Current.Tick(target != null, debugAim);
        }

        private void BeginRecording()
        {
            if (!Mic.Ready && !Mic.Start()) { Status = Mic.LastReport + " - press T to type"; return; }
            activeFabrication = ArmoryTelemetry.StartFabrication("voice", CurrentWave(), Settings.Offline || OpenAI == null, Settings.UsesGateway);
            micSpan = activeFabrication.StartSpan("mic.capture", "Push-to-talk microphone capture");
            micSpan?.SetTag("mic.device", Mic.Device ?? "default");
            Mic.BeginTalk();
            Status = "Listening on " + MicCapture.Short(Mic.Device);
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, Rig.Head.transform.position, 0.5f);
        }

        private void EndRecording()
        {
            var samples = Mic.EndTalk(out int rate);
            Status = Mic.LastReport;
            micSpan?.SetTag("mic.verdict", Mic.LastVerdict);
            micSpan?.SetTag("mic.peak", Mic.LastPeak.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture));
            micSpan?.SetTag("mic.rms", Mic.LastRms.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture));
            activeFabrication?.FinishSpan(micSpan);
            ArmoryTelemetry.MicLog(activeFabrication, Mic.Device, Mic.LastPeak, Mic.LastRms, Mic.LastVerdict, Mic.LastDurationSeconds);
            micSpan = null;

            var trace = activeFabrication;
            activeFabrication = null;
            if (samples == null) { trace?.Complete(); return; }
            _ = TranscribeAndFabricate(WavPcm.EncodeWav(samples, 1, rate), trace);
        }

        /// <summary>WAV bytes (any sample rate) → transcript → weapon. Mic push-to-talk ends here.</summary>
        public async Awaitable TranscribeAndFabricate(byte[] wav, FabricationTrace trace = null)
        {
            if (OpenAI == null) { Status = "Offline: press T to type or 1-5 for presets."; trace?.Complete(); return; }
            Busy = true;
            Status = "TRANSCRIBING...";
            string text = null;
            try { text = await OpenAI.Transcribe(wav, trace); }
            finally { Busy = false; }
            if (string.IsNullOrWhiteSpace(text)) { Status = "Didn't catch that. Try again."; trace?.Complete(); return; }
            await Fabricate(text, trace);
        }

        /// <summary>"Ready", "start the wave", "bring them on" - starts the wave instead of building a weapon.</summary>
        public static bool IsWaveStartPhrase(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.ToLowerInvariant().Trim().Trim('.', '!', '?', ',');
            if (t.Length > 40) return false;
            string[] phrases = { "ready", "i'm ready", "im ready", "start", "start the wave", "start wave", "begin", "bring them on", "bring it on", "send them", "let's go", "lets go", "go" };
            foreach (var phrase in phrases) if (t == phrase || t.EndsWith(" " + phrase) || t.StartsWith(phrase + " ")) return true;
            return false;
        }

        public async Awaitable Fabricate(string request, FabricationTrace trace = null, bool playerRequested = true)
        {
            if (Busy) return;
            var director = WaveDirector.Instance;
            if (director != null && director.InArmory && IsWaveStartPhrase(request))
            {
                AddSubtitle("YOU", request);
                director.RequestWaveStart();
                Status = "Wave starting";
                return;
            }
            Busy = true;
            trace ??= ArmoryTelemetry.StartFabrication(playerRequested ? "text" : "initial_loadout", CurrentWave(), Settings.Offline || OpenAI == null, Settings.UsesGateway);
            LastTranscript = request;
            if (playerRequested) AddSubtitle("YOU", request);
            Status = "FABRICATING...";
            ShowHologram(true);
            try
            {
                string json = null;
                if (OpenAI != null) json = await OpenAI.InterpretWeapon(request, BattleContext(), null, trace);
                var parsed = WeaponSpecParser.Parse(json);
                if (parsed == null)
                {
                    trace.MarkFallback(OpenAI == null ? "offline_mode" : "invalid_or_missing_model_output");
                    parsed = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(request));
                    if (OpenAI != null) parsed.ShipAILine = "Uplink failed, so I improvised: " + parsed.Name + ".";
                }
                ApplyBudget(parsed);
                var assemble = trace.StartSpan("unity.assemble", "Build weapon in player hand");
                try { Equip(parsed, announce: true, trace); }
                finally { trace.FinishSpan(assemble); }
                Status = OpenAI != null ? $"Built in {OpenAI.LastLatency:0.0}s" : "Built offline";
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                trace.MarkFallback("fabrication_exception");
                if (!playerRequested && Current == null)
                {
                    var fallback = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(request));
                    Equip(fallback, announce: true, trace);
                    Status = "Starter weapon built offline";
                }
                else Status = "Fabrication error: " + error.Message;
            }
            finally
            {
                ShowHologram(false);
                Busy = false;
                trace.Complete();
            }
        }

        /// <summary>Energy the station can spare this wave, and what the last fabricated weapon drew from it.</summary>
        public int Budget { get; private set; } = WeaponBudget.Wave1Budget;
        public int LastCost { get; private set; }

        /// <summary>
        /// The parser already caps raw DPS, but nothing stopped a request being fast AND huge AND homing at once.
        /// The wave allowance makes those compete, and ARIA says the cut out loud: a player who is told "dropped
        /// homing to fit 8 energy" learns to ask for less, where one who silently receives a worse gun than they
        /// described just thinks the model misheard them.
        /// </summary>
        private void ApplyBudget(ParsedWeapon spec)
        {
            if (spec == null) return;
            Budget = WeaponBudget.Budget(WaveDirector.Instance != null ? WaveDirector.Instance.WaveIndex : 0);
            if (WeaponBudget.Trim(spec, Budget, out string report))
            {
                string sentence = char.ToUpperInvariant(report[0]) + report.Substring(1) + ".";
                spec.ShipAILine = string.IsNullOrWhiteSpace(spec.ShipAILine)
                    ? "Fabricated: " + spec.Name + ". " + sentence
                    : spec.ShipAILine + " " + sentence;
            }
            LastCost = WeaponBudget.Cost(spec);
        }

        private void Equip(ParsedWeapon spec, bool announce, FabricationTrace trace = null)
        {
            if (spec == null) return;
            Projectile.ClearPool();
            if (Current != null) Destroy(Current.gameObject);
            Current = WeaponAssembler.Build(spec, Rig.Aim);
            if (!announce) return;
            ArmoryGame.Instance?.OnWeaponEquipped(spec);
            ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, Rig.Aim.position, 0.7f);
            SayShip(string.IsNullOrWhiteSpace(spec.ShipAILine) ? "Fabricated: " + spec.Name + "." : spec.ShipAILine, spec.Name.ToUpperInvariant(), trace);
            if (ElevenLabs != null && Settings.GenerateWeaponSfx && spec.SfxPrompt != null)
            {
                trace?.AddAsyncWork();
                _ = LoadWeaponSfx(Current, spec, trace);
            }
            if (OpenAI != null && Settings.GenerateBlueprints)
            {
                trace?.AddAsyncWork();
                _ = LoadBlueprint(spec, trace);
            }

        }

        /// <summary>Generated art becomes the held weapon once it lands; the modular parts cover the wait.</summary>
        private async Awaitable LoadWeaponArt(Weapon weapon, ParsedWeapon spec, FabricationTrace trace)
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.temporaryCachePath, "armory-weapon-art");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, Hash(Settings.ImageModel + "art" + spec.Name) + ".png");
                byte[] png = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
                if (png == null)
                {
                    png = await OpenAI.GenerateWeaponArt(spec.Name, ColorName(spec.Color), Flavour(spec.Payload), trace);
                    if (png != null) System.IO.File.WriteAllBytes(path, png);
                }
                // A newer weapon may have replaced this one while the art generated.
                if (png == null || weapon == null || Current != weapon) return;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                texture.LoadImage(png);
                WeaponAssembler.ApplyGeneratedArt(weapon, texture);
                ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, Rig.Aim.position, 0.5f);
            }
            catch (System.Exception error) { Debug.LogWarning("Weapon art failed: " + error.Message); }
            finally { trace?.Complete(); }
        }

        /// <summary>AI concept-art blueprint for the new weapon, cached on disk by name so demo repeats are instant.</summary>
        private async Awaitable LoadBlueprint(ParsedWeapon spec, FabricationTrace trace = null)
        {
            try
            {
                int request = ++blueprintRequest;
                wristBlueprint.SetPending(spec.Name);
                coreBlueprint.SetPending(spec.Name);

                string dir = System.IO.Path.Combine(Application.temporaryCachePath, "armory-blueprints");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, Hash(Settings.ImageModel + spec.Name) + ".png");
                byte[] png = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
                if (png == null)
                {
                    png = await OpenAI.GenerateBlueprint(spec.Name, ColorName(spec.Color), Flavour(spec.Payload), trace);
                    if (png != null) System.IO.File.WriteAllBytes(path, png);
                }
                // A newer weapon may have been requested while this one generated.
                if (request != blueprintRequest || png == null) return;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                texture.LoadImage(png);
                wristBlueprint.Show(texture, spec.Name);
                coreBlueprint.Show(texture, spec.Name);
                ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, coreBlueprint.transform.position, 0.8f);

                // The blueprint is the reference for the real geometry: GPT reads its own drawing and returns the
                // weapon as primitives, which replaces the hologram in the player's hand.
                await BuildMeshFromBlueprint(spec, png, request, trace);
            }
            catch (System.Exception error) { Debug.LogWarning("Blueprint generation failed: " + error.Message); }
            finally { trace?.Complete(); }
        }

        /// <summary>Second half of fabrication: turn the blueprint into primitives and build them in the hand.</summary>
        private async Awaitable BuildMeshFromBlueprint(ParsedWeapon spec, byte[] blueprintPng, int request, FabricationTrace trace)
        {
            if (!Settings.GenerateWeaponMesh) return;
            string traits = $"{spec.FireMode} · {spec.Payload} · {spec.Mods}";
            string json = await OpenAI.DescribeWeaponMesh(spec.Name, traits, blueprintPng, trace);
            if (request != blueprintRequest || Current == null || Current.Spec != spec) return;
            var parts = WeaponMesh.Parse(json, spec.Color, out var muzzle);
            if (parts.Count == 0) { Debug.LogWarning("Weapon mesh: model returned no usable parts; keeping placeholder."); return; }
            WeaponAssembler.ApplyGeneratedMesh(Current, parts, muzzle);
            Status = $"Built {parts.Count}-part model";
            ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, Rig.Aim.position, 0.6f);
        }

        private static string Flavour(Payload payload)
        {
            switch (payload)
            {
                case Payload.Explosive: return "covered in fizzing firework canisters";
                case Payload.Plasma: return "with swirling plasma bubbles in glass tubes";
                case Payload.Electric: return "with crackling tesla coils and lightning bolts";
                case Payload.Cryo: return "with frosty ice crystals and snowflake decals";
                default: return "with brass gears and chunky dials";
            }
        }

        private static string ColorName(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (s < 0.2f) return v > 0.6f ? "white" : "silver";
            float deg = h * 360f;
            if (deg < 15f || deg >= 345f) return "red";
            if (deg < 45f) return "orange";
            if (deg < 70f) return "yellow";
            if (deg < 160f) return "green";
            if (deg < 200f) return "cyan";
            if (deg < 250f) return "blue";
            if (deg < 300f) return "purple";
            return "pink";
        }

        private static string Hash(string text)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return System.BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private async Awaitable LoadWeaponSfx(Weapon weapon, ParsedWeapon spec, FabricationTrace trace = null)
        {
            try
            {
                float seconds = spec.FireMode == FireMode.Beam ? 2f : spec.FireRate > 6f ? 0.5f : 0.9f;
                string prompt = spec.SfxPrompt + (spec.FireMode == FireMode.Beam ? ", continuous loopable hum" : ", single short shot, no music");
                var clip = await ElevenLabs.SoundEffect(prompt, seconds, trace);
                if (clip != null && weapon != null) weapon.FireClip = clip;
            }
            catch (System.Exception error) { Debug.LogWarning("Weapon SFX failed: " + error.Message); }
            finally { trace?.Complete(); }
        }

        private string BattleContext()
        {
            var builder = new StringBuilder();
            var director = WaveDirector.Instance;
            if (director != null && director.Current != null)
            {
                builder.Append("Wave ").Append(director.WaveIndex + 1).Append(" '").Append(director.Current.Name).Append("' enemies: ");
                foreach (var group in director.Current.Groups) builder.Append(group.Kind).Append(' ');
            }
            builder.Append(". Matchups: Swarm weak to splash/chain; Armored weak to piercing/explosive, resists kinetic/plasma; Fast weak to homing/slow; Shielded only hurt by electric until the shield pops.");
            var ms = Mothership.Instance;
            if (ms != null) builder.Append(" Mothership adaptations: ").Append(ms.Describe()).Append('.');
            return builder.ToString();
        }

        private void ShowHologram(bool on)
        {
            if (on && hologram == null)
            {
                hologram = Mats.Shape(PrimitiveType.Cube, null, Rig.Aim.position, Vector3.one * 0.14f, Mats.Glow(new Color(0.3f, 0.9f, 1f), 0.35f), name: "Fabrication Hologram");
                fabHum.clip = ProceduralSfx.Fabricate;
                fabHum.loop = true;
                fabHum.volume = 0.3f;
                fabHum.Play();
            }
            else if (!on && hologram != null)
            {
                Destroy(hologram);
                hologram = null;
                fabHum.Stop();
            }
        }

        public void SayShip(string text, string banner = null, FabricationTrace trace = null) => Say("ARIA", text, shipVoice, Settings.ShipVoiceId, 0.55f, 0.25f, banner, trace);
        public void SayMothership(string text, string banner = null) => Say("MOTHERSHIP", text, motherVoice, Settings.MothershipVoiceId, 0.3f, 0.6f, banner);

        private void Say(string speaker, string text, AudioSource source, string voiceId, float stability, float style, string banner, FabricationTrace trace = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (banner != null) ArmoryGame.Instance?.ShowBanner(banner, speaker == "MOTHERSHIP" ? new Color(1f, 0.35f, 0.4f) : new Color(0.4f, 0.9f, 1f));
            AddSubtitle(speaker, text);
            if (ElevenLabs == null || !Settings.Speak) return;
            trace?.AddAsyncWork();
            voiceQueue.Enqueue((source, ElevenLabs.Speak(text, voiceId, stability, style, trace), trace));
        }

        private void AddSubtitle(string speaker, string text)
        {
            Subtitles.Add((speaker, text));
            if (Subtitles.Count > 4) Subtitles.RemoveAt(0);
        }

        /// <summary>Plays voice lines in the order they were requested, never overlapping.</summary>
        private IEnumerator PlayVoices()
        {
            while (true)
            {
                if (voiceQueue.Count == 0) { yield return null; continue; }
                var (source, pending, trace) = voiceQueue.Dequeue();
                var wait = WaitFor(pending, trace);
                while (wait.MoveNext()) yield return wait.Current;
                if (lastClip != null)
                {
                    source.clip = lastClip;
                    source.Play();
                    while (source.isPlaying) yield return null;
                }
            }
        }

        private AudioClip lastClip;

        private IEnumerator WaitFor(Awaitable<AudioClip> pending, FabricationTrace trace)
        {
            lastClip = null;
            bool done = false;
            Await(pending, () => done = true, trace);
            float timeout = Time.time + 15f;
            while (!done && Time.time < timeout) yield return null;
        }

        private async void Await(Awaitable<AudioClip> pending, System.Action onDone, FabricationTrace trace = null)
        {
            try { lastClip = await pending; }
            catch (System.Exception error) { Debug.LogWarning("Voice line failed: " + error.Message); }
            finally { onDone(); trace?.Complete(); }
        }

        private int CurrentWave() => WaveDirector.Instance != null ? WaveDirector.Instance.WaveIndex + 1 : 0;

        private void OnGUI()
        {
            if (!textEntryOpen) return;
            GUI.Box(new Rect(Screen.width * 0.2f, Screen.height * 0.8f, Screen.width * 0.6f, 60f), "Describe a weapon (Enter to fabricate, Esc to cancel)");
            GUI.SetNextControlName("weaponPrompt");
            typed = GUI.TextField(new Rect(Screen.width * 0.2f + 10f, Screen.height * 0.8f + 25f, Screen.width * 0.6f - 20f, 25f), typed);
            GUI.FocusControl("weaponPrompt");
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                textEntryOpen = false;
                if (!string.IsNullOrWhiteSpace(typed)) _ = Fabricate(typed.Trim());
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                textEntryOpen = false;
                e.Use();
            }
        }

        private void OnDestroy()
        {
            Mic?.Stop();
        }
    }
}
