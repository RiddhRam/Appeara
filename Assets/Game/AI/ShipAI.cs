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

        private const string DefaultWeapon = "standard issue machine gun";
        private AudioSource shipVoice;
        private AudioSource motherVoice;
        private AudioSource fabHum;
        private readonly Queue<(AudioSource source, Awaitable<AudioClip> clip)> voiceQueue = new Queue<(AudioSource, Awaitable<AudioClip>)>();
        private GameObject hologram;
        private BlueprintHologram wristBlueprint;
        private BlueprintHologram coreBlueprint;
        private int blueprintRequest;

        // Always-on looping mic; push-to-talk just marks start/end positions, so the first word is never clipped.
        private bool probing;
        private bool textEntryOpen;
        private string typed = "";

        private void Awake()
        {
            Instance = this;
            var keys = ArmoryKeys.Load();
            if (!Settings.Offline && keys.HasOpenAI) OpenAI = new OpenAiClient(keys.openai, Settings);
            if (!Settings.Offline && keys.HasElevenLabs) ElevenLabs = new ElevenLabsClient(keys.elevenlabs, Settings);
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
            Equip(WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(DefaultWeapon)), announce: false);
            SayShip(OpenAI != null ? "Fabricator online. Hold your left grip and tell me what to build." : "Fabricator in offline mode. Keyword fabrication only.", "ARIA ONLINE");
            StartCoroutine(PlayVoices());
        }

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
                else Current.Tick(Rig.FireHeld && !MissionDeck.Open, Rig.Aim);
            }

            if (Mic != null && Mic.Recording) Mic.Tick();
            if (Rig.TalkHeld && Mic != null && !Mic.Recording && !Busy && !probing) BeginRecording();
            else if (!Rig.TalkHeld && Mic != null && Mic.Recording) EndRecording();

            if (Rig.CannedPromptPressed >= 0 && !Busy) _ = Fabricate(CannedPrompts[Rig.CannedPromptPressed]);
            if (Rig.DropPressed && !Busy) Equip(WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(DefaultWeapon)), announce: false);
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
            Mic.BeginTalk();
            Status = "Listening on " + MicCapture.Short(Mic.Device);
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, Rig.Head.transform.position, 0.5f);
        }

        private void EndRecording()
        {
            var samples = Mic.EndTalk(out int rate);
            Status = Mic.LastReport;
            if (samples == null) return;
            _ = TranscribeAndFabricate(WavPcm.EncodeWav(samples, 1, rate));
        }

        /// <summary>WAV bytes (any sample rate) → transcript → weapon. Mic push-to-talk ends here.</summary>
        public async Awaitable TranscribeAndFabricate(byte[] wav)
        {
            if (OpenAI == null) { Status = "Offline: press T to type or 1-5 for presets."; return; }
            Busy = true;
            Status = "TRANSCRIBING...";
            string text = null;
            try { text = await OpenAI.Transcribe(wav); }
            finally { Busy = false; }
            if (string.IsNullOrWhiteSpace(text)) { Status = "Didn't catch that. Try again."; return; }
            await Fabricate(text);
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

        public async Awaitable Fabricate(string request)
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
            LastTranscript = request;
            AddSubtitle("YOU", request);
            Status = "FABRICATING...";
            ShowHologram(true);
            try
            {
                string json = null;
                if (OpenAI != null) json = await OpenAI.InterpretWeapon(request, BattleContext());
                var parsed = WeaponSpecParser.Parse(json);
                if (parsed == null)
                {
                    parsed = WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(request));
                    if (OpenAI != null) parsed.ShipAILine = "Uplink failed, so I improvised: " + parsed.Name + ".";
                }
                Equip(parsed, announce: true);
                Status = OpenAI != null ? $"Built in {OpenAI.LastLatency:0.0}s" : "Built offline";
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                Status = "Fabrication error: " + error.Message;
            }
            finally
            {
                ShowHologram(false);
                Busy = false;
            }
        }

        private void Equip(ParsedWeapon spec, bool announce)
        {
            if (spec == null) return;
            if (Current != null) Destroy(Current.gameObject);
            Current = WeaponAssembler.Build(spec, Rig.Aim);
            ArmoryGame.Instance?.OnWeaponEquipped(spec);
            if (!announce) return;
            ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, Rig.Aim.position, 0.7f);
            SayShip(string.IsNullOrWhiteSpace(spec.ShipAILine) ? "Fabricated: " + spec.Name + "." : spec.ShipAILine, spec.Name.ToUpperInvariant());
            if (ElevenLabs != null && Settings.GenerateWeaponSfx && spec.SfxPrompt != null) _ = LoadWeaponSfx(Current, spec);
            if (OpenAI != null && Settings.GenerateBlueprints) _ = LoadBlueprint(spec);
        }

        /// <summary>AI concept-art blueprint for the new weapon, cached on disk by name so demo repeats are instant.</summary>
        private async Awaitable LoadBlueprint(ParsedWeapon spec)
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
                png = await OpenAI.GenerateBlueprint(spec.Name, ColorName(spec.Color), Flavour(spec.Payload));
                if (png != null) System.IO.File.WriteAllBytes(path, png);
            }
            // A newer weapon may have been requested while this one generated.
            if (request != blueprintRequest || png == null) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(png);
            wristBlueprint.Show(texture, spec.Name);
            coreBlueprint.Show(texture, spec.Name);
            ProceduralSfx.PlayAt(ProceduralSfx.Fabricate, coreBlueprint.transform.position, 0.8f);
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

        private async Awaitable LoadWeaponSfx(Weapon weapon, ParsedWeapon spec)
        {
            float seconds = spec.FireMode == FireMode.Beam ? 2f : spec.FireRate > 6f ? 0.5f : 0.9f;
            string prompt = spec.SfxPrompt + (spec.FireMode == FireMode.Beam ? ", continuous loopable hum" : ", single short shot, no music");
            var clip = await ElevenLabs.SoundEffect(prompt, seconds);
            if (clip != null && weapon != null) weapon.FireClip = clip;
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

        public void SayShip(string text, string banner = null) => Say("ARIA", text, shipVoice, Settings.ShipVoiceId, 0.55f, 0.25f, banner);
        public void SayMothership(string text, string banner = null) => Say("MOTHERSHIP", text, motherVoice, Settings.MothershipVoiceId, 0.3f, 0.6f, banner);

        private void Say(string speaker, string text, AudioSource source, string voiceId, float stability, float style, string banner)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (banner != null) ArmoryGame.Instance?.ShowBanner(banner, speaker == "MOTHERSHIP" ? new Color(1f, 0.35f, 0.4f) : new Color(0.4f, 0.9f, 1f));
            AddSubtitle(speaker, text);
            if (ElevenLabs == null || !Settings.Speak) return;
            voiceQueue.Enqueue((source, ElevenLabs.Speak(text, voiceId, stability, style)));
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
                var (source, pending) = voiceQueue.Dequeue();
                var wait = WaitFor(pending);
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

        private IEnumerator WaitFor(Awaitable<AudioClip> pending)
        {
            lastClip = null;
            bool done = false;
            Await(pending, () => done = true);
            float timeout = Time.time + 15f;
            while (!done && Time.time < timeout) yield return null;
        }

        private async void Await(Awaitable<AudioClip> pending, System.Action onDone)
        {
            try { lastClip = await pending; }
            catch (System.Exception error) { Debug.LogWarning("Voice line failed: " + error.Message); }
            finally { onDone(); }
        }

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
