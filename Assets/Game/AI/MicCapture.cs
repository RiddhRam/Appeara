using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Armory.Core;
using UnityEngine;

namespace Armory.AI
{
    /// <summary>
    /// Push-to-talk capture. The Quest's virtual mic can hand Unity a live but silent stream, which previously ate
    /// the player's speech with no explanation, so this class reports every attempt and falls back to another
    /// device when a capture comes back empty.
    /// </summary>
    public sealed class MicCapture
    {
        private const int BufferSeconds = 30;
        private const int UploadRate = 16000;
        private const string RememberedKey = "armory.mic.device";

        /// <summary>Null means the Windows default device.</summary>
        public string Device { get; private set; }
        public int SampleRate { get; private set; }
        public bool Ready => clip != null;
        public bool Recording { get; private set; }
        /// <summary>Smoothed peak of the last ~100 ms, 0-1, for the HUD meter.</summary>
        public float Level { get; private set; }
        public string LastReport { get; private set; } = "";
        public int SilentCaptures { get; private set; }

        private readonly string preferred;
        private AudioClip clip;
        private int startPosition;
        private string pendingDevice;
        private readonly List<string> tried = new List<string>();

        public MicCapture(string preferredNameFragment)
        {
            preferred = string.IsNullOrWhiteSpace(preferredNameFragment) ? null : preferredNameFragment.ToLowerInvariant();
        }

        /// <summary>Preference: explicit override, last device that produced audio, a headset mic, then anything else.</summary>
        public IEnumerable<string> Candidates()
        {
            var devices = Microphone.devices;
            var ordered = new List<string>();
            void Add(string device) { if (device != null && devices.Contains(device) && !ordered.Contains(device)) ordered.Add(device); }

            if (preferred != null) foreach (var d in devices) if (d.ToLowerInvariant().Contains(preferred)) Add(d);
            Add(PlayerPrefs.GetString(RememberedKey, null));
            foreach (var d in devices)
            {
                string lower = d.ToLowerInvariant();
                if (lower.Contains("oculus") || lower.Contains("quest") || lower.Contains("headset")) Add(d);
            }
            foreach (var d in devices) Add(d);
            return ordered;
        }

        public bool Start(string device = null)
        {
            Stop();
            if (Microphone.devices.Length == 0) { LastReport = "no microphone devices"; return false; }
            Device = device ?? Candidates().FirstOrDefault();
            if (Device == null) { LastReport = "no microphone devices"; return false; }
            Microphone.GetDeviceCaps(Device, out int min, out int max);
            SampleRate = min == 0 && max == 0 ? UploadRate : Mathf.Clamp(UploadRate, min, max);
            clip = Microphone.Start(Device, true, BufferSeconds, SampleRate);
            if (clip == null) { LastReport = "could not open " + Device; return false; }
            if (!tried.Contains(Device)) tried.Add(Device);
            Debug.Log($"Mic: using '{Device}' at {SampleRate} Hz. Available: {string.Join(", ", Microphone.devices)}");
            return true;
        }

        public void Stop()
        {
            if (clip != null && Device != null && Microphone.IsRecording(Device)) Microphone.End(Device);
            clip = null;
            Recording = false;
        }

        public void BeginTalk()
        {
            // A device swap after a silent capture happens here, a frame later: restarting capture inside the
            // same frame it was ended returns an empty stream on Windows.
            if (pendingDevice != null) { Start(pendingDevice); pendingDevice = null; }
            if (!Ready) return;
            startPosition = Microphone.GetPosition(Device);
            Recording = true;
        }

        /// <summary>Reads the meter every frame while talking; also keeps the HUD honest when the mic is dead.</summary>
        public void Tick()
        {
            if (!Ready) return;
            int window = SampleRate / 10;
            int position = Microphone.GetPosition(Device);
            var samples = new float[window];
            clip.GetData(samples, (position - window + clip.samples) % clip.samples);
            Level = Mathf.Lerp(Level, VoiceAudio.Peak(samples), 0.35f);
        }

        /// <summary>Returns upload-ready 16 kHz samples, or null with <see cref="LastReport"/> explaining why not.</summary>
        public float[] EndTalk(out int rate)
        {
            rate = UploadRate;
            Recording = false;
            if (!Ready) { LastReport = "microphone not running"; return null; }

            int end = Microphone.GetPosition(Device);
            int length = (end - startPosition + clip.samples) % clip.samples;
            if (length <= 0) { LastReport = "captured nothing (device stalled)"; MarkSilent(); return null; }

            var raw = new float[length];
            clip.GetData(raw, startPosition);
            float rawPeak = VoiceAudio.Peak(raw);
            var samples = VoiceAudio.Downsample(raw, SampleRate, UploadRate, out rate);
            samples = VoiceAudio.Trim(samples, rate / 8);
            var verdict = VoiceAudio.Judge(samples, rate, out string report);
            float gain = verdict == CaptureVerdict.Ok ? VoiceAudio.Normalize(samples) : 1f;
            LastReport = report;
            Debug.Log($"Mic capture: device='{Device}' {length / (float)SampleRate:0.00}s rawPeak={rawPeak:0.00000} rms={VoiceAudio.Rms(raw):0.00000} -> {verdict} ({report}) gain x{gain:0.0}");

            if (verdict == CaptureVerdict.Silent) { MarkSilent(); return null; }
            if (verdict != CaptureVerdict.Ok) return null;
            SilentCaptures = 0;
            PlayerPrefs.SetString(RememberedKey, Device);
            return samples;
        }

        /// <summary>After a silent capture, move to the next untried device so the next attempt can work.</summary>
        private void MarkSilent()
        {
            SilentCaptures++;
            var next = Candidates().FirstOrDefault(d => !tried.Contains(d));
            if (next == null)
            {
                LastReport += " | all mics silent - check Windows mic privacy + Link audio";
                tried.Clear();
                return;
            }
            LastReport += $" | next try uses '{Short(next)}'";
            pendingDevice = next;
        }

        public static string Short(string device) => device == null ? "default" : device.Length <= 24 ? device : device.Substring(0, 24) + "…";

        /// <summary>Records from every device in turn and reports levels: run this once with the headset on.</summary>
        public static IEnumerator Probe(float seconds, System.Action<string> onDone)
        {
            var report = new StringBuilder("Mic probe (speak while this runs):\n");
            string resume = null;
            foreach (var device in Microphone.devices)
            {
                Microphone.GetDeviceCaps(device, out int min, out int max);
                int rate = min == 0 && max == 0 ? 16000 : Mathf.Clamp(16000, min, max);
                var clip = Microphone.Start(device, false, Mathf.CeilToInt(seconds) + 1, rate);
                yield return new WaitForSecondsRealtime(seconds);
                int position = Microphone.GetPosition(device);
                Microphone.End(device);
                var samples = new float[Mathf.Max(1, position)];
                if (clip != null) clip.GetData(samples, 0);
                float peak = VoiceAudio.Peak(samples);
                report.AppendLine($"  {device} @{rate}Hz pos={position} peak={peak:0.00000} rms={VoiceAudio.Rms(samples):0.00000}{(peak > VoiceAudio.SilencePeak ? "  <= AUDIO" : "")}");
                if (peak > VoiceAudio.SilencePeak && resume == null) resume = device;
            }
            if (resume != null) report.AppendLine("Best device: " + resume);
            else report.AppendLine("No device produced audio: check Windows mic privacy and Link audio routing.");
            Debug.Log(report.ToString());
            onDone?.Invoke(report.ToString());
        }
    }
}
