using System.Collections.Generic;
using System.Linq;
using Armory.AI;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Alien hive mind. Every fabricated weapon receives one replaceable defensive counter and one tactical counter
    /// after twenty seconds of active combat. Model output is bounded by the shared counter catalog and rules fallback.
    /// </summary>
    public sealed class Mothership : MonoBehaviour
    {
        public static Mothership Instance { get; private set; }
        public const float WeaponAdaptSeconds = 20f;

        public readonly HashSet<string> Resistances = new HashSet<string>();
        public readonly HashSet<CounterKind> Counters = new HashSet<CounterKind>();
        public readonly List<Counter> History = new List<Counter>();

        public CounterPackage ActivePackage { get; private set; }
        public string ActiveDefendedPrimitive { get; private set; }
        public string PendingWeaponName => pendingWeapon?.Name;
        public float AnalysisRemaining => analysisClock.Remaining;
        public bool WaitingForModel { get; private set; }
        public bool HasPendingAnalysis => pendingWeapon != null;
        public bool Thinking => WaitingForModel;

        private ParsedWeapon pendingWeapon;
        private CounterPackage proposedPackage;
        private string proposedSource;
        private int weaponRevision;
        private readonly CounterAnalysisClock analysisClock = new CounterAnalysisClock();
        private CounterAnalysisTrace counterTrace;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (pendingWeapon == null) return;
            var director = WaveDirector.Instance;
            if (director == null || !analysisClock.Advance(Time.deltaTime, director.CombatActive)) return;

            var weapon = pendingWeapon;
            var package = proposedPackage;
            string source = proposedSource;
            if (package == null)
            {
                counterTrace?.MarkFallback(WaitingForModel ? "model_timeout" : "missing_package");
                package = AdaptationRules.ForWeapon(weapon, null, null, null, weaponRevision);
                source = WaitingForModel ? "rules_timeout" : "rules_fallback";
            }
            pendingWeapon = null;
            proposedPackage = null;
            proposedSource = null;
            WaitingForModel = false;
            int appliedEnemies = Activate(package, weapon);
            counterTrace?.Complete(package, source, appliedEnemies, WeaponAdaptSeconds);
            counterTrace = null;
        }

        public bool Has(CounterKind kind) => Counters.Contains(kind);

        /// <summary>Starts a new analysis revision. Existing counters remain active for the entire grace period.</summary>
        public void BeginWeaponAnalysis(ParsedWeapon weapon)
        {
            if (weapon == null) return;
            counterTrace?.FinishCancelled("superseded");
            weaponRevision++;
            pendingWeapon = weapon;
            proposedPackage = null;
            proposedSource = null;
            analysisClock.Start(WeaponAdaptSeconds);

            var ai = ShipAI.Instance;
            WaitingForModel = ai != null && ai.OpenAI != null;
            int wave = WaveDirector.Instance != null ? WaveDirector.Instance.WaveIndex + 1 : 0;
            counterTrace = ArmoryTelemetry.StartCounterAnalysis(weaponRevision, wave, weapon.Primitives().Distinct(),
                !WaitingForModel, ai != null && ai.Settings.UsesGateway);
            if (WaitingForModel) _ = RequestPackage(weapon, weaponRevision, ai.OpenAI, counterTrace);
            else
            {
                counterTrace.MarkFallback("offline_mode");
                proposedPackage = AdaptationRules.ForWeapon(weapon, null, null, null, weaponRevision);
                proposedSource = "rules_offline";
            }
        }

        private async Awaitable RequestPackage(ParsedWeapon weapon, int revision, OpenAiClient client, CounterAnalysisTrace trace)
        {
            WeaponCounterReply reply = null;
            try
            {
                reply = await client.AnalyzeWeaponCounters(weapon, Describe(), EnemyDescription(), trace);
            }
            catch (System.Exception error)
            {
                trace.MarkFallback("model_exception");
                Debug.LogWarning("Mothership weapon analysis failed, using rules: " + error.Message);
            }

            // Never let an old or late network response alter the current adaptation.
            if (revision != weaponRevision || pendingWeapon == null)
            {
                trace.MarkStaleResponse();
                return;
            }
            bool defenseAccepted = reply != null && SelectionAccepted(reply.defense, CounterFamily.Defensive, weapon);
            bool tacticAccepted = reply != null && SelectionAccepted(reply.tactic, CounterFamily.Tactical, weapon);
            trace.RecordModelResult(reply != null, defenseAccepted, tacticAccepted);
            if (reply == null) trace.MarkFallback("model_unavailable");
            else if (!defenseAccepted || !tacticAccepted) trace.MarkFallback("invalid_model_selection");
            proposedPackage = AdaptationRules.ForWeapon(weapon, reply?.defense, reply?.tactic, reply?.taunt, revision);
            proposedSource = reply == null ? "rules_model_failure" : defenseAccepted && tacticAccepted ? "model" : "hybrid_fallback";
            WaitingForModel = false;
        }

        private static bool SelectionAccepted(string text, CounterFamily family, ParsedWeapon weapon) =>
            Counter.TryParse(text, out var counter) && CounterCatalog.IsValidForWeapon(counter, family, weapon);

        private int Activate(CounterPackage package, ParsedWeapon weapon)
        {
            ActivePackage = package;
            ActiveDefendedPrimitive = CounterCatalog.DefendedPrimitive(package.Defense, weapon);
            Resistances.Clear();
            Counters.Clear();
            History.Clear();
            foreach (var counter in package.Counters)
            {
                if (counter.Kind == CounterKind.Resist) Resistances.Add(counter.Target);
                else Counters.Add(counter.Kind);
                History.Add(counter);
            }

            int appliedEnemies = 0;
            foreach (var enemy in new List<Enemy>(Enemy.All))
            {
                if (enemy == null || !enemy.Alive || enemy.Avatar != null || enemy.ExternallyDriven) continue;
                enemy.RefreshCounterPackage();
                Effects.Flash(enemy.Center, new Color(1f, 0.08f, 0.12f), enemy.Radius * 2.4f);
                appliedEnemies++;
            }

            if (HiveAvatar.Active != null)
            {
                HiveAvatar.Active.Adapt(ActiveDefendedPrimitive);
                appliedEnemies++;
            }

            string banner = "COUNTERMEASURE: " + package.Defense.ToString().ToUpperInvariant() + " + " + package.Tactic.ToString().ToUpperInvariant();
            ShipAI.Instance?.SayMothership(package.Taunt, banner);
            return appliedEnemies;
        }

        public void Clear()
        {
            weaponRevision++;
            counterTrace?.FinishCancelled("cleared");
            counterTrace = null;
            pendingWeapon = null;
            proposedPackage = null;
            proposedSource = null;
            ActivePackage = null;
            ActiveDefendedPrimitive = null;
            analysisClock.Cancel();
            WaitingForModel = false;
            Resistances.Clear();
            Counters.Clear();
            History.Clear();
            foreach (var enemy in new List<Enemy>(Enemy.All))
                if (enemy != null && enemy.Alive && enemy.Avatar == null && !enemy.ExternallyDriven)
                    enemy.RefreshCounterPackage();
        }

        public string Describe() => ActivePackage == null ? "none" : ActivePackage.ToString();

        private static string EnemyDescription()
        {
            var wave = WaveDirector.Instance?.Current;
            if (wave == null) return "none";
            return string.Join(", ", wave.Groups.Select(group => group.Kind.ToString()).Distinct());
        }
    }
}
