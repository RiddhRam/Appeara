using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>
    /// Lets tooling drive the open editor: write a command to Temp/armory-command.txt, read Temp/armory-result.txt.
    /// Commands: refresh | tests | menu &lt;path&gt; | play | stop | shot
    /// </summary>
    [InitializeOnLoad]
    public static class ArmoryCommandBridge
    {
        private const string CommandPath = "Temp/armory-command.txt";
        private const string ResultPath = "Temp/armory-result.txt";
        private const string CompilePath = "Temp/armory-compile.txt";
        private const string ShotPath = "Temp/armory-shot.png";

        private static TestRunnerApi runner;
        private static double nextPoll;
        private static readonly StringBuilder compileLog = new StringBuilder();

        static ArmoryCommandBridge()
        {
            EditorApplication.update += Poll;
            CompilationPipeline.compilationStarted += _ => compileLog.Clear();
            CompilationPipeline.assemblyCompilationFinished += (assembly, messages) =>
            {
                foreach (var message in messages)
                    if (message.type == CompilerMessageType.Error)
                        compileLog.AppendLine(message.message);
            };
            CompilationPipeline.compilationFinished += _ =>
                File.WriteAllText(CompilePath, compileLog.Length == 0 ? "OK " + DateTime.Now.ToString("HH:mm:ss") : compileLog.ToString());
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CommandPath) || EditorApplication.isCompiling || EditorApplication.isUpdating || runner != null) return;

            string command = File.ReadAllText(CommandPath).Trim();
            File.Delete(CommandPath);
            try
            {
                if (command == "refresh") { AssetDatabase.Refresh(); Write("refreshed " + DateTime.Now.ToString("HH:mm:ss")); }
                else if (command == "tests") RunTests();
                else if (command.StartsWith("menu ")) Write(EditorApplication.ExecuteMenuItem(command.Substring(5)) ? "menu ok" : "menu not found");
                else if (command == "play") { EditorApplication.isPlaying = true; Write("playing"); }
                else if (command == "stop") { EditorApplication.isPlaying = false; Write("stopped"); }
                else if (command == "shot") Shot();
                else if (command == "dump") Dump();
                else if (command.StartsWith("fab ")) { _ = ShipAI.Instance.Fabricate(command.Substring(4)); Write("fabricating"); }
                else if (command == "autofire") { ShipAI.Instance.DebugAutoFire = !ShipAI.Instance.DebugAutoFire; Write("autofire " + ShipAI.Instance.DebugAutoFire); }
                else if (command == "skip") { WaveDirector.Instance.SkipWave(); Write("skipped"); }
                else if (command == "boss") { WaveDirector.Instance.RestartWave(4, 1f); Write("starting Hive Avatar"); }
                else if (command == "state") Write(GameState());
                else if (command == "miclevels") { ShipAI.Instance.ProbeMics(); Write("probing mics for ~2s per device; see console"); }
                else if (command == "reset") { Mothership.Instance.Clear(); Projectile.SurfaceHits.Clear(); Projectile.EnemyHits = 0; WaveDirector.Instance.RestartWave(0, 1f); Write("reset"); }
                else Write("unknown command: " + command);
            }
            catch (Exception error)
            {
                Write("error: " + error);
                Debug.LogException(error);
            }
        }

        private static void Dump()
        {
            var builder = new StringBuilder();
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var renderers = root.GetComponentsInChildren<Renderer>();
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                builder.AppendLine($"{root.name} active={root.activeSelf} pos={root.transform.position} rot={root.transform.eulerAngles} scale={root.transform.localScale} renderers={renderers.Length} bounds.center={bounds.center} size={bounds.size}");
                foreach (Transform child in root.transform)
                {
                    var childRenderers = child.GetComponentsInChildren<Renderer>();
                    if (childRenderers.Length == 0) { builder.AppendLine($"   {child.name} pos={child.position}"); continue; }
                    var cb = childRenderers[0].bounds;
                    foreach (var r in childRenderers) cb.Encapsulate(r.bounds);
                    builder.AppendLine($"   {child.name} pos={child.position} center={cb.center} size={cb.size}");
                }
            }
            Write(builder.ToString());
        }

        private static string GameState()
        {
            if (!EditorApplication.isPlaying || ShipAI.Instance == null) return "not playing";
            var b = new StringBuilder();
            var d = WaveDirector.Instance;
            b.AppendLine($"state: {d.State} | wave {d.WaveIndex + 1} | alive {d.Alive} pending {d.PendingSpawns}");
            b.AppendLine($"core: {StationCore.Instance.Health} | xr: {ArmoryGame.Instance.Rig.IsXR} | fps: {1f / Time.smoothDeltaTime:0}");
            var ai = ShipAI.Instance;
            b.AppendLine($"ai: status='{ai.Status}' busy={ai.Busy} openai={(ai.OpenAI != null)} err='{ai.OpenAI?.LastError}' latency={ai.OpenAI?.LastLatency:0.00} eleven={(ai.ElevenLabs != null)} err='{ai.ElevenLabs?.LastError}'");
            if (ai.Current != null)
            {
                var w = ai.Current.Spec;
                b.AppendLine($"weapon: {w.Name} | {w.FireMode} {w.Payload} [{w.Mods}] rate={w.FireRate:0.#} count={w.ProjectileCount} dmg={w.Damage:0.#} speed={w.ProjectileSpeed:0} body={w.Body} barrel={w.Barrel} color={w.Color} sfx='{w.SfxPrompt}' customSfx={(ai.Current.FireClip != null)}");
            }
            b.Append("surface hits: ");
            foreach (var pair in Projectile.SurfaceHits) b.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
            b.AppendLine($"| enemy hits: {Projectile.EnemyHits} | projectiles live: {Projectile.All.Count} stuck: {Projectile.All.FindAll(p => p.Stuck).Count}");
            var mothership = Mothership.Instance;
            string analysis = mothership.HasPendingAnalysis
                ? $"analyzing '{mothership.PendingWeaponName}' {mothership.AnalysisRemaining:0.0}s model={mothership.WaitingForModel}"
                : "idle";
            b.AppendLine($"mothership: {mothership.Describe()} | {analysis} | wave log: {ArmoryGame.Instance.WaveLog.Summary()}");
            var boss = HiveAvatar.Active;
            if (boss != null) b.AppendLine($"boss: {boss.Phase} | attack {boss.CurrentAttack} | hp {boss.Body.Health}/{boss.Body.MaxHealth} | organs {boss.Rules.BrokenCount}/4 | plating {boss.Rules.Plating} | ready {boss.Ready}");
            foreach (var (speaker, text) in ai.Subtitles) b.AppendLine($"  {speaker}: {text}");
            return b.ToString();
        }

        private static void Write(string text) => File.WriteAllText(ResultPath, text);

        private static void Shot()
        {
            var camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (!EditorApplication.isPlaying && SceneView.lastActiveSceneView != null) camera = SceneView.lastActiveSceneView.camera;
            if (camera == null) { Write("no camera"); return; }
            var target = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
            RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(ShotPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            target.Release();
            Write("shot " + camera.name);
        }

        [MenuItem("Armory/Mic Levels (Play Mode)")]
        public static void MicLevels()
        {
            if (!EditorApplication.isPlaying || ShipAI.Instance == null) { Debug.LogWarning("Enter Play Mode first."); return; }
            ShipAI.Instance.ProbeMics();
        }

        [MenuItem("Armory/Run Tests")]
        public static void RunTests()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("stop play mode first"); return; }
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Armory.Tests" } }));
        }

        private sealed class Results : ICallbacks
        {
            private readonly StringBuilder failures = new StringBuilder();
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.FailCount > 0)
                    failures.AppendLine($"FAIL {result.FullName}: {result.Message}");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                string summary = $"tests: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped\n{failures}";
                Write(summary);
                Debug.Log(summary);
                UnityEngine.Object.DestroyImmediate(runner);
                runner = null;
            }
        }
    }
}
