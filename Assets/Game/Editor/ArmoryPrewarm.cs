using System.IO;
using UnityEditor;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>
    /// Fabricates every canned prompt once, in play mode, so the slow artefacts are on disk before the live demo.
    /// Venue Wi-Fi is the biggest risk we carry: a cold blueprint costs ~20 s and a weapon spec ~2 s, while the
    /// ElevenLabs voice line and weapon SFX are just as fragile. Everything ShipAI generates caches under
    /// Application.temporaryCachePath, so one pre-warm pass turns the demo run into disk reads.
    /// Run the menu item again to stop; exiting play mode stops it too.
    /// </summary>
    public static class ArmoryPrewarm
    {
        // The three on-disk caches: blueprint PNGs, generated weapon art, and ElevenLabs voice/SFX PCM.
        private static readonly string[] CacheFolders = { "armory-blueprints", "armory-weapon-art", "armory-audio" };

        // ShipAI.Fabricate returns as soon as the weapon is equipped; the blueprint, mesh, SFX and voice line land
        // afterwards on their own tasks. Rather than guess a fixed wait, treat a prompt as done once the caches
        // have stopped growing for a while - with a floor and a ceiling so one stuck request can't hang the pass.
        private const double PollSeconds = 0.5;
        private const double QuietSeconds = 6.0;
        private const double MinPromptSeconds = 10.0;
        private const double MaxPromptSeconds = 90.0;
        private const double GapSeconds = 1.5;

        private static bool running;
        private static bool inFlight;
        private static int index;
        private static int cachedFiles;
        private static string current;
        private static double nextPoll;
        private static double startedAt;
        private static double promptStartedAt;
        private static double quietUntil;
        private static double nextPromptAt;

        [MenuItem("Armory/Pre-warm Demo Cache")]
        public static void Run()
        {
            if (running) { Finish("stopped from the menu"); return; }
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Pre-warm: enter play mode first - this drives the real fabrication path, not a stub.");
                return;
            }
            if (ShipAI.Instance == null)
            {
                Debug.LogWarning("Pre-warm: no ShipAI in the scene yet. Wait for the arena to finish building.");
                return;
            }

            running = true;
            inFlight = false;
            index = -1;
            current = null;
            cachedFiles = CountCachedFiles();
            startedAt = EditorApplication.timeSinceStartup;
            nextPromptAt = startedAt;
            nextPoll = 0.0;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Debug.Log($"Pre-warm: {ShipAI.CannedPrompts.Length} prompts to fabricate. {cachedFiles} files already cached under {Application.temporaryCachePath}. Run the menu item again to stop.");
        }

        private static void Tick()
        {
            if (!running) return;
            if (!EditorApplication.isPlaying || ShipAI.Instance == null) { Finish("play mode exited"); return; }

            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + PollSeconds;
            var ai = ShipAI.Instance;

            if (!inFlight)
            {
                // ShipAI.Fabricate silently drops requests while it is busy, so never overlap prompts.
                if (now < nextPromptAt || ai.Busy) return;
                index++;
                if (index >= ShipAI.CannedPrompts.Length) { Finish("all prompts fabricated"); return; }
                current = ShipAI.CannedPrompts[index];
                cachedFiles = CountCachedFiles();
                promptStartedAt = now;
                quietUntil = now + QuietSeconds;
                inFlight = true;
                Debug.Log($"Pre-warm [{index + 1}/{ShipAI.CannedPrompts.Length}] fabricating \"{current}\"");
                _ = ai.Fabricate(current);
                return;
            }

            int files = CountCachedFiles();
            if (files != cachedFiles)
            {
                Debug.Log($"Pre-warm [{index + 1}/{ShipAI.CannedPrompts.Length}] cached {files - cachedFiles} new file(s), {files} total.");
                cachedFiles = files;
                quietUntil = now + QuietSeconds;
            }
            if (ai.Busy) quietUntil = now + QuietSeconds;

            double elapsed = now - promptStartedAt;
            bool settled = now > quietUntil && elapsed > MinPromptSeconds;
            if (!settled && elapsed < MaxPromptSeconds) return;
            if (settled) Debug.Log($"Pre-warm [{index + 1}/{ShipAI.CannedPrompts.Length}] \"{current}\" settled after {elapsed:0.0}s.");
            else Debug.LogWarning($"Pre-warm [{index + 1}/{ShipAI.CannedPrompts.Length}] \"{current}\" still running after {elapsed:0}s; moving on. Its artefacts may not be cached.");
            inFlight = false;
            nextPromptAt = now + GapSeconds;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) Finish("play mode exited");
        }

        private static void Finish(string reason)
        {
            if (!running) return;
            running = false;
            inFlight = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            int done = Mathf.Clamp(index, 0, ShipAI.CannedPrompts.Length);
            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            Debug.Log($"Pre-warm finished ({reason}): {done}/{ShipAI.CannedPrompts.Length} prompts, {CountCachedFiles()} files cached, {elapsed:0}s elapsed.");
        }

        private static int CountCachedFiles()
        {
            int total = 0;
            foreach (var folder in CacheFolders)
            {
                string dir = Path.Combine(Application.temporaryCachePath, folder);
                if (Directory.Exists(dir)) total += Directory.GetFiles(dir).Length;
            }
            return total;
        }
    }
}
