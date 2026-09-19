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
                else Write("unknown command: " + command);
            }
            catch (Exception error)
            {
                Write("error: " + error);
                Debug.LogException(error);
            }
        }

        private static void Write(string text) => File.WriteAllText(ResultPath, text);

        private static void Shot()
        {
            var camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (!EditorApplication.isPlaying && SceneView.lastActiveSceneView != null) camera = SceneView.lastActiveSceneView.camera;
            if (camera == null) { Write("no camera"); return; }
            var target = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousEye = camera.stereoTargetEye;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
            camera.stereoTargetEye = previousEye;
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
