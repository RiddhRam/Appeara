using System;
using System.Collections.Generic;
using Armory.Core;
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

        public static CounterAnalysisTrace StartCounterAnalysis(int revision, int wave, IEnumerable<string> traits, bool offline, bool gateway)
        {
            string safeTraits = traits == null ? "none" : string.Join(",", traits);
            var transaction = SentrySdk.StartTransaction("armory.counter_analysis", "ai.counter");
            transaction.SetTag("counter.revision", revision.ToString());
            transaction.SetTag("wave", wave.ToString());
            transaction.SetTag("weapon.traits", safeTraits);
            transaction.SetTag("offline_fallback", offline ? "true" : "false");
            transaction.SetTag("gateway", gateway ? "true" : "false");
            SentrySdk.AddBreadcrumb("Counter analysis started", "armory.counter", "info",
                new Dictionary<string, string>
                {
                    { "revision", revision.ToString() }, { "wave", wave.ToString() }, { "traits", safeTraits }
                });
            return new CounterAnalysisTrace(transaction, revision);
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

    /// <summary>Minimal trace surface shared by provider calls without exposing prompts or responses.</summary>
    public interface IArmoryTrace
    {
        string TraceId { get; }
        ISpan StartSpan(string operation, string description = null);
        void FinishSpan(ISpan span, Exception error = null);
    }

    /// <summary>Owns a transaction until the visible fabrication and all requested asset jobs have completed.</summary>
    public sealed class FabricationTrace : IArmoryTrace
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

    /// <summary>Owns one counter-analysis transaction from weapon equip through mutation or cancellation.</summary>
    public sealed class CounterAnalysisTrace : IArmoryTrace
    {
        private readonly ITransactionTracer transaction;
        private readonly int revision;
        private readonly ISpan graceSpan;
        private bool finished;

        internal CounterAnalysisTrace(ITransactionTracer transaction, int revision)
        {
            this.transaction = transaction;
            this.revision = revision;
            graceSpan = transaction?.StartChild("counter.grace_period", "Combat-time analysis window");
        }

        public string TraceId => transaction?.GetTraceHeader().TraceId.ToString() ?? "disabled";

        public ISpan StartSpan(string operation, string description = null) => transaction?.StartChild(operation, description ?? operation);

        public void FinishSpan(ISpan span, Exception error = null)
        {
            if (span == null) return;
            if (error == null) span.Finish(); else span.Finish(error);
        }

        public void RecordModelResult(bool received, bool defenseAccepted, bool tacticAccepted)
        {
            if (finished) return;
            transaction?.SetTag("model.response_received", received ? "true" : "false");
            transaction?.SetTag("model.defense_accepted", defenseAccepted ? "true" : "false");
            transaction?.SetTag("model.tactic_accepted", tacticAccepted ? "true" : "false");
        }

        public void MarkFallback(string reason)
        {
            if (finished) return;
            transaction?.SetTag("offline_fallback", "true");
            transaction?.SetTag("fallback.reason", reason ?? "unknown");
            SentrySdk.Logger.LogWarning(log =>
            {
                log.SetAttribute("counter.revision", revision);
                log.SetAttribute("fallback.reason", reason ?? "unknown");
                log.SetAttribute("armory.trace_id", TraceId);
            }, "Counter analysis fallback: {0}", reason ?? "unknown");
        }

        public void MarkStaleResponse()
        {
            SentrySdk.AddBreadcrumb("Stale counter response discarded", "armory.counter", "info",
                new Dictionary<string, string> { { "revision", revision.ToString() }, { "trace_id", TraceId } });
        }

        public void Complete(CounterPackage package, string source, int appliedEnemies, float combatSeconds)
        {
            if (finished) return;
            graceSpan?.SetTag("combat.seconds", combatSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            graceSpan?.Finish();
            transaction?.SetTag("counter.outcome", "applied");
            transaction?.SetTag("counter.source", source ?? "unknown");
            transaction?.SetTag("counter.defense", package?.Defense.ToString() ?? "none");
            transaction?.SetTag("counter.tactic", package?.Tactic.ToString() ?? "none");
            transaction?.SetTag("counter.applied_enemy_count", appliedEnemies.ToString());
            SentrySdk.Logger.LogInfo(log =>
            {
                log.SetAttribute("counter.revision", revision);
                log.SetAttribute("counter.source", source ?? "unknown");
                log.SetAttribute("counter.defense", package?.Defense.ToString() ?? "none");
                log.SetAttribute("counter.tactic", package?.Tactic.ToString() ?? "none");
                log.SetAttribute("counter.applied_enemy_count", appliedEnemies);
                log.SetAttribute("counter.combat_seconds", combatSeconds);
                log.SetAttribute("armory.trace_id", TraceId);
            }, "Counter package applied: {0} + {1}", package?.Defense.ToString() ?? "none", package?.Tactic.ToString() ?? "none");
            finished = true;
            transaction?.Finish();
        }

        public void FinishCancelled(string reason)
        {
            if (finished) return;
            graceSpan?.Finish();
            transaction?.SetTag("counter.outcome", reason ?? "cancelled");
            finished = true;
            transaction?.Finish();
        }
    }
}
