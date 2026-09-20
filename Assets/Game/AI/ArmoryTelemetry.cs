using System;
using System.Collections.Generic;
using Armory.Core;
using Sentry;
using Sentry.Unity;
using UnityEngine;

namespace Armory.AI
{
    /// <summary>
    /// Sentry instrumentation for player-facing AI and gameplay loops. We deliberately never send a transcript,
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

        public static MothershipSpawnTrace StartMothershipSpawn(int wave, float spawnDistance, bool inheritedCounter)
        {
            var transaction = SentrySdk.StartTransaction("armory.mothership_spawn", "gameplay.boss_spawn");
            transaction.SetTag("wave", wave.ToString());
            transaction.SetTag("spawn.distance", spawnDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            transaction.SetTag("counter.inherited", inheritedCounter ? "true" : "false");
            SentrySdk.AddBreadcrumb("Mothership Avatar spawn requested", "armory.mothership_spawn", "info",
                new Dictionary<string, string>
                {
                    { "wave", wave.ToString() },
                    { "spawn_distance", spawnDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) }
                });
            return new MothershipSpawnTrace(transaction, wave);
        }

        public static ITransactionTracer StartPerformanceWindow(int wave)
        {
            var transaction = SentrySdk.StartTransaction("armory.frame_window", "performance.frame_time");
            transaction.SetTag("wave", wave.ToString());
            return transaction;
        }

        public static void FinishPerformanceWindow(ITransactionTracer transaction, float averageMs, float p95Ms, float maxMs,
            int enemySpawns, int enemyDespawns, int projectileSpawns, int projectileDespawns)
        {
            if (transaction == null) return;
            transaction.SetTag("frame.avg_ms", Invariant(averageMs));
            transaction.SetTag("frame.p95_ms", Invariant(p95Ms));
            transaction.SetTag("frame.max_ms", Invariant(maxMs));
            transaction.SetTag("churn.enemy_spawn", enemySpawns.ToString());
            transaction.SetTag("churn.enemy_despawn", enemyDespawns.ToString());
            transaction.SetTag("churn.projectile_spawn", projectileSpawns.ToString());
            transaction.SetTag("churn.projectile_despawn", projectileDespawns.ToString());
            transaction.Finish();
            SentrySdk.Logger.LogInfo(log =>
            {
                log.SetAttribute("frame.avg_ms", averageMs);
                log.SetAttribute("frame.p95_ms", p95Ms);
                log.SetAttribute("frame.max_ms", maxMs);
                log.SetAttribute("churn.enemy_spawn", enemySpawns);
                log.SetAttribute("churn.enemy_despawn", enemyDespawns);
                log.SetAttribute("churn.projectile_spawn", projectileSpawns);
                log.SetAttribute("churn.projectile_despawn", projectileDespawns);
            }, "Frame window p95 {0} ms, max {1} ms", p95Ms, maxMs);
        }

        public static void FrameSpikeLog(float frameMs, float rollingAverageMs, int wave, int enemies, int projectiles,
            string recentActivity, float activityAge)
        {
            var data = new Dictionary<string, string>
            {
                { "frame_ms", Invariant(frameMs) }, { "average_ms", Invariant(rollingAverageMs) },
                { "wave", wave.ToString() }, { "enemies", enemies.ToString() }, { "projectiles", projectiles.ToString() },
                { "recent_activity", recentActivity ?? "none" }, { "activity_age", Invariant(activityAge) }
            };
            SentrySdk.AddBreadcrumb("Frame-time spike", "armory.performance", "warning", data);
            SentrySdk.Logger.LogWarning(log =>
            {
                log.SetAttribute("frame.ms", frameMs);
                log.SetAttribute("frame.rolling_average_ms", rollingAverageMs);
                log.SetAttribute("wave", wave);
                log.SetAttribute("objects.enemies", enemies);
                log.SetAttribute("objects.projectiles", projectiles);
                log.SetAttribute("recent_activity", recentActivity ?? "none");
                log.SetAttribute("recent_activity_age_seconds", activityAge);
            }, "Frame-time spike: {0} ms after {1}", frameMs, recentActivity ?? "unknown activity");
        }

        private static string Invariant(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>Tracks final-boss construction, presentation initialization, arrival and readiness.</summary>
    public sealed class MothershipSpawnTrace : IArmoryTrace
    {
        private readonly ITransactionTracer transaction;
        private readonly int wave;
        private bool finished;

        internal MothershipSpawnTrace(ITransactionTracer transaction, int wave)
        {
            this.transaction = transaction;
            this.wave = wave;
        }

        public string TraceId => transaction?.GetTraceHeader().TraceId.ToString() ?? "disabled";
        public ISpan StartSpan(string operation, string description = null) => transaction?.StartChild(operation, description ?? operation);

        public void FinishSpan(ISpan span, Exception error = null)
        {
            if (span == null) return;
            if (error == null) span.Finish(); else span.Finish(error);
        }

        public void RecordPresentation(bool modelLoaded, bool animationAssetsLoaded)
        {
            if (finished) return;
            transaction?.SetTag("presentation.model", modelLoaded ? "prefab" : "fallback");
            transaction?.SetTag("presentation.animations", animationAssetsLoaded ? "loaded" : "missing");
        }

        public void Ready(float arrivalSeconds, int targetCount, bool inheritedCounter)
        {
            if (finished) return;
            transaction?.SetTag("spawn.outcome", "ready");
            transaction?.SetTag("arrival.seconds", arrivalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            transaction?.SetTag("target.count", targetCount.ToString());
            transaction?.SetTag("counter.inherited", inheritedCounter ? "true" : "false");
            SentrySdk.Logger.LogInfo(log =>
            {
                log.SetAttribute("wave", wave);
                log.SetAttribute("arrival.seconds", arrivalSeconds);
                log.SetAttribute("target.count", targetCount);
                log.SetAttribute("counter.inherited", inheritedCounter);
                log.SetAttribute("armory.trace_id", TraceId);
            }, "Mothership Avatar ready after {0} seconds", arrivalSeconds);
            SentrySdk.AddBreadcrumb("Mothership Avatar ready", "armory.mothership_spawn", "info",
                new Dictionary<string, string> { { "trace_id", TraceId }, { "wave", wave.ToString() } });
            finished = true;
            transaction?.Finish();
        }

        public void Cancel(string reason)
        {
            if (finished) return;
            transaction?.SetTag("spawn.outcome", reason ?? "cancelled");
            SentrySdk.Logger.LogWarning(log =>
            {
                log.SetAttribute("wave", wave);
                log.SetAttribute("spawn.outcome", reason ?? "cancelled");
                log.SetAttribute("armory.trace_id", TraceId);
            }, "Mothership Avatar spawn ended early: {0}", reason ?? "cancelled");
            finished = true;
            transaction?.Finish();
        }

        public void Fail(string stage, Exception error)
        {
            if (finished) return;
            transaction?.SetTag("spawn.outcome", "failed");
            transaction?.SetTag("failure.stage", stage ?? "unknown");
            if (error != null) SentrySdk.CaptureException(error);
            SentrySdk.Logger.LogWarning(log =>
            {
                log.SetAttribute("wave", wave);
                log.SetAttribute("failure.stage", stage ?? "unknown");
                log.SetAttribute("exception.type", error?.GetType().Name ?? "unknown");
                log.SetAttribute("armory.trace_id", TraceId);
            }, "Mothership Avatar spawn failed during {0}", stage ?? "unknown");
            finished = true;
            transaction?.Finish();
        }
    }

    public enum PerformanceObjectKind { Enemy, Projectile }

    /// <summary>Low-allocation frame sampler feeding Sentry profiles with object-churn context.</summary>
    public static class ArmoryPerformance
    {
        private static int enemySpawns, enemyDespawns, projectileSpawns, projectileDespawns;
        public static string RecentActivity { get; private set; } = "startup";
        public static float RecentActivityTime { get; private set; }

        public static void Record(PerformanceObjectKind kind, bool spawned)
        {
            if (kind == PerformanceObjectKind.Enemy)
            {
                if (spawned) enemySpawns++; else enemyDespawns++;
            }
            else
            {
                if (spawned) projectileSpawns++; else projectileDespawns++;
            }
            RecentActivity = (kind == PerformanceObjectKind.Enemy ? "enemy" : "projectile") + (spawned ? ".spawn" : ".despawn");
            RecentActivityTime = Time.realtimeSinceStartup;
        }

        public static void Drain(out int spawnedEnemies, out int despawnedEnemies, out int spawnedProjectiles, out int despawnedProjectiles)
        {
            spawnedEnemies = enemySpawns;
            despawnedEnemies = enemyDespawns;
            spawnedProjectiles = projectileSpawns;
            despawnedProjectiles = projectileDespawns;
            enemySpawns = enemyDespawns = projectileSpawns = projectileDespawns = 0;
        }
    }

    /// <summary>Creates ten-second profiled frame windows and rate-limited spike diagnostics.</summary>
    public sealed class ArmoryPerformanceMonitor : MonoBehaviour
    {
        public const float WindowSeconds = 10f;
        public const float SpikeThresholdMs = 33.3f;
        private const float SpikeCooldownSeconds = 1f;
        private readonly float[] samples = new float[2048];
        private int sampleCount;
        private int sampleCursor;
        private float sampleSum;
        private float windowStarted;
        private float nextSpikeLog;
        private ITransactionTracer transaction;

        private void OnEnable() => StartWindow();

        private void Update()
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs <= 0f || frameMs > 1000f) return;
            if (sampleCount < samples.Length)
            {
                samples[sampleCursor++] = frameMs;
                sampleCount++;
                sampleSum += frameMs;
            }
            else
            {
                if (sampleCursor >= samples.Length) sampleCursor = 0;
                sampleSum -= samples[sampleCursor];
                samples[sampleCursor++] = frameMs;
                sampleSum += frameMs;
            }

            float average = sampleCount > 0 ? sampleSum / sampleCount : frameMs;
            float now = Time.realtimeSinceStartup;
            if (frameMs >= SpikeThresholdMs && now >= nextSpikeLog)
            {
                nextSpikeLog = now + SpikeCooldownSeconds;
                int wave = WaveDirector.Instance != null ? WaveDirector.Instance.WaveIndex + 1 : 0;
                ArmoryTelemetry.FrameSpikeLog(frameMs, average, wave, Enemy.All.Count, Projectile.All.Count,
                    ArmoryPerformance.RecentActivity, Mathf.Max(0f, now - ArmoryPerformance.RecentActivityTime));
            }
            if (now - windowStarted >= WindowSeconds) FinishWindow(true);
        }

        private void StartWindow()
        {
            sampleCount = sampleCursor = 0;
            sampleSum = 0f;
            windowStarted = Time.realtimeSinceStartup;
            int wave = WaveDirector.Instance != null ? WaveDirector.Instance.WaveIndex + 1 : 0;
            transaction = ArmoryTelemetry.StartPerformanceWindow(wave);
        }

        private void FinishWindow(bool restart)
        {
            if (sampleCount > 0)
            {
                var ordered = new float[sampleCount];
                System.Array.Copy(samples, ordered, sampleCount);
                System.Array.Sort(ordered);
                float p95 = ordered[Mathf.Clamp(Mathf.CeilToInt(sampleCount * 0.95f) - 1, 0, sampleCount - 1)];
                float max = ordered[sampleCount - 1];
                ArmoryPerformance.Drain(out int enemyIn, out int enemyOut, out int projectileIn, out int projectileOut);
                ArmoryTelemetry.FinishPerformanceWindow(transaction, sampleSum / sampleCount, p95, max,
                    enemyIn, enemyOut, projectileIn, projectileOut);
            }
            else transaction?.Finish();
            transaction = null;
            if (restart) StartWindow();
        }

        private void OnDisable() => FinishWindow(false);
    }
}
