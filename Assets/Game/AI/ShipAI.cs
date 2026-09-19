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
        public bool Listening => recording;
        public string MicDevice => micDevice;
        public AudioClip MicClip => micClip;
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

        // Always-on looping mic; push-to-talk just marks start/end positions, so the first word is never clipped.
        private string micDevice;
        private AudioClip micClip;
        private const int MicRate = 16000;
        private const int MicSeconds = 30;
        private bool recording;
        private int recordStart;
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
            Equip(WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(DefaultWeapon)), announce: false);
            SayShip(OpenAI != null ? "Fabricator online. Hold your left grip and tell me what to build." : "Fabricator in offline mode. Keyword fabrication only.", "ARIA ONLINE");
            StartCoroutine(PlayVoices());
        }

        private void StartMic()
        {
            if (Microphone.devices.Length == 0) { Status = "No microphone found. Press T to type."; return; }
            micDevice = null;
            foreach (var device in Microphone.devices)
            {
                string lower = device.ToLowerInvariant();
                if (lower.Contains("oculus") || lower.Contains("quest") || lower.Contains("headset")) { micDevice = device; break; }
            }
            micClip = Microphone.Start(micDevice, true, MicSeconds, MicRate);
            Debug.Log("ShipAI mic: " + (micDevice ?? "default device") + " | available: " + string.Join(", ", Microphone.devices));
        }

        private void Update()
        {
            if (Rig == null) return;
            Rig.TextEntryActive = textEntryOpen;

            if (Current != null)
            {
                if (DebugAutoFire) AutoFire();
                else Current.Tick(Rig.FireHeld, Rig.Aim);
            }

            if (Rig.TalkHeld && !recording && !Busy) BeginRecording();
            else if (!Rig.TalkHeld && recording) EndRecording();

            if (Rig.CannedPromptPressed >= 0 && !Busy) _ = Fabricate(CannedPrompts[Rig.CannedPromptPressed]);
            if (Rig.DropPressed && !Busy) Equip(WeaponSpecParser.Parse(MockWeaponInterpreter.InterpretJson(DefaultWeapon)), announce: false);
            if (Rig.TypePressed && !Busy) { textEntryOpen = true; typed = ""; }

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
            if (micClip == null) { Status = "No microphone. Press T to type."; return; }
            recording = true;
            recordStart = Microphone.GetPosition(micDevice);
            Status = "LISTENING...";
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, Rig.Head.transform.position, 0.5f);
        }

        private void EndRecording()
        {
            recording = false;
            int end = Microphone.GetPosition(micDevice);
            int length = (end - recordStart + micClip.samples) % micClip.samples;
            if (length < MicRate / 3) { Status = "Too short - hold the grip while you speak."; return; }
            var samples = new float[length];
            // GetData wraps around the looping buffer for us.
            micClip.GetData(samples, recordStart);
            samples = WavPcm.TrimSilence(samples, 0.015f, MicRate / 8);
            if (samples.Length < MicRate / 4) { Status = "I didn't hear anything. Check the mic."; return; }
            _ = TranscribeAndFabricate(WavPcm.EncodeWav(samples, 1, MicRate));
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

        public async Awaitable Fabricate(string request)
        {
            if (Busy) return;
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
            if (micClip != null) Microphone.End(micDevice);
        }
    }
}
