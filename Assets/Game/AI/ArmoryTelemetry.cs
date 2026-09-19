using System;
using System.Collections.Generic;
using Sentry;
using Sentry.Unity;
using UnityEngine;

namespace Armory.AI
{
    /// <summary>
    /// Sentry instrumentation for the player-facing fabrication loop. We deliberately never send a transcript,
    /// API key, raw audio, or prompt: the trace describes system behaviour, not player speech.
    /// </summary>
    public static class ArmoryTelemetry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var keys = ArmoryKeys.Load();
            if (!keys.HasSentry)
            {
                Debug.LogWarning("Sentry: no DSN found in UserSettings/ArmoryKeys.json; telemetry is disabled.");
                return;
            }

            if (!SentrySdk.IsEnabled) SentrySdk.Init(options =>
            {
                options.Dsn = keys.sentry;
                options.Environment = keys.sentryEnvironment;
                options.Release = Application.version;
                // The hackathon build needs complete evidence. Lower this before a public launch.
                options.TracesSampleRate = 1.0;
                options.ProfilesSampleRate = 1.0;
                options.EnableLogs = true;
                options.SetBeforeSendLog(log => log.Message != null && log.Message.StartsWith("Sensitive:") ? null : log);
            });
            if (!SentrySdk.IsEnabled)
            {
                Debug.LogWarning("Sentry: SDK did not enable after initialization. Check the DSN in ArmoryKeys.json.");
                return;
            }
            SentrySdk.SetTag("game.mode", "armory-vr");
            SentrySdk.SetTag("unity.version", Application.unityVersion);
        }

        public static FabricationTrace StartFabrication(string inputMode, int wave, bool offline, bool gateway)
        {
            var transaction = SentrySdk.StartTransaction("armory.fabricate", "ai.pipeline");
            transaction.SetTag("input.mode", inputMode);
            transaction.SetTag("wave", wave.ToString());
            transaction.SetTag("offline_fallback", offline ? "true" : "false");
            transaction.SetTag("gateway", gateway ? "true" : "false");
            SentrySdk.AddBreadcrumb("Fabrication started", "armory.fabrication", "info",
                new Dictionary<string, string> { { "input.mode", inputMode }, { "wave", wave.ToString() } });
            return new FabricationTrace(transaction);
        }

        /// <summary>A wave transaction gives the Unity profiler a gameplay-sized window, especially useful for swarms.</summary>
        public static ITransactionTracer StartWave(int waveIndex, string waveName, int enemyCount)
        {
            var transaction = SentrySdk.StartTransaction("armory.wave", "gameplay.wave");
            transaction.SetTag("wave", waveIndex.ToString());
            transaction.SetTag("wave.name", waveName ?? "unknown");
            transaction.SetTag("wave.enemy_count", enemyCount.ToString());
            SentrySdk.AddBreadcrumb("Wave started", "armory.wave", "info",
                new Dictionary<string, string> { { "wave", waveIndex.ToString() }, { "enemy_count", enemyCount.ToString() } });
            return transaction;
        }

        public static void FinishWave(ITransactionTracer transaction, string outcome)
        {
            if (transaction == null) return;
            transaction.SetTag("wave.outcome", outcome);
            transaction.Finish();
        }

        public static void MicLog(FabricationTrace trace, string device, float peak, float rms, string verdict, float seconds)
        {
            SentrySdk.Logger.LogInfo(log =>
            {
                log.SetAttribute("mic.device", device ?? "default");
                log.SetAttribute("mic.peak", peak);
                log.SetAttribute("mic.rms", rms);
                log.SetAttribute("mic.verdict", verdict ?? "unknown");
                log.SetAttribute("mic.duration_seconds", seconds);
                if (trace != null) log.SetAttribute("armory.trace_id", trace.TraceId);
            }, "Mic capture verdict: {0}", verdict ?? "unknown");
        }

        public static void FallbackLog(FabricationTrace trace, string reason)
        {
            SentrySdk.Logger.LogWarning(log =>
            {
                log.SetAttribute("fallback.reason", reason ?? "unknown");
                if (trace != null) log.SetAttribute("armory.trace_id", trace.TraceId);
            }, "Fabrication fallback: {0}", reason ?? "unknown");
        }
    }

    /// <summary>Owns a transaction until the visible fabrication and all requested asset jobs have completed.</summary>
    public sealed class FabricationTrace
    {
        private readonly ITransactionTracer transaction;
        private int pendingWork = 1;
        private bool finished;

        internal FabricationTrace(ITransactionTracer transaction) => this.transaction = transaction;

        public string TraceId => transaction?.GetTraceHeader().TraceId.ToString() ?? "disabled";

        public ISpan StartSpan(string operation, string description = null)
        {
            var span = transaction?.StartChild(operation, description ?? operation);
            return span;
        }

        public void AddAsyncWork()
        {
            if (!finished) pendingWork++;
        }

        public void FinishSpan(ISpan span, Exception error = null)
        {
            if (span == null) return;
            if (error == null) span.Finish(); else span.Finish(error);
        }

        public void Complete()
        {
            if (finished) return;
            pendingWork--;
            if (pendingWork > 0) return;
            finished = true;
            transaction?.Finish();
        }

        public void MarkFallback(string reason)
        {
            transaction?.SetTag("offline_fallback", "true");
            transaction?.SetTag("fallback.reason", reason ?? "unknown");
            ArmoryTelemetry.FallbackLog(this, reason);
        }
    }
}
