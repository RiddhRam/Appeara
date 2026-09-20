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
        private int weaponRevision;
        private readonly CounterAnalysisClock analysisClock = new CounterAnalysisClock();

        private void Awake() => Instance = this;

        private void Update()
        {
            if (pendingWeapon == null) return;
            var director = WaveDirector.Instance;
            if (director == null || !analysisClock.Advance(Time.deltaTime, director.CombatActive)) return;

            var weapon = pendingWeapon;
            var package = proposedPackage ?? AdaptationRules.ForWeapon(weapon, null, null, null, weaponRevision);
            pendingWeapon = null;
            proposedPackage = null;
            WaitingForModel = false;
            Activate(package, weapon);
        }

        public bool Has(CounterKind kind) => Counters.Contains(kind);

        /// <summary>Starts a new analysis revision. Existing counters remain active for the entire grace period.</summary>
        public void BeginWeaponAnalysis(ParsedWeapon weapon)
        {
            if (weapon == null) return;
            weaponRevision++;
            pendingWeapon = weapon;
            proposedPackage = null;
            analysisClock.Start(WeaponAdaptSeconds);

            var ai = ShipAI.Instance;
            WaitingForModel = ai != null && ai.OpenAI != null;
            if (WaitingForModel) _ = RequestPackage(weapon, weaponRevision, ai.OpenAI);
            else proposedPackage = AdaptationRules.ForWeapon(weapon, null, null, null, weaponRevision);
        }

        private async Awaitable RequestPackage(ParsedWeapon weapon, int revision, OpenAiClient client)
        {
            WeaponCounterReply reply = null;
            try
            {
                reply = await client.AnalyzeWeaponCounters(weapon, Describe(), EnemyDescription());
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Mothership weapon analysis failed, using rules: " + error.Message);
            }

            // Never let an old or late network response alter the current adaptation.
            if (revision != weaponRevision || pendingWeapon == null) return;
            proposedPackage = AdaptationRules.ForWeapon(weapon, reply?.defense, reply?.tactic, reply?.taunt, revision);
            WaitingForModel = false;
        }

        private void Activate(CounterPackage package, ParsedWeapon weapon)
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

            foreach (var enemy in new List<Enemy>(Enemy.All))
            {
                if (enemy == null || !enemy.Alive || enemy.Avatar != null || enemy.ExternallyDriven) continue;
                enemy.RefreshCounterPackage();
                Effects.Flash(enemy.Center, new Color(1f, 0.08f, 0.12f), enemy.Radius * 2.4f);
            }

            if (HiveAvatar.Active != null)
                HiveAvatar.Active.Adapt(ActiveDefendedPrimitive);

            string banner = "COUNTERMEASURE: " + package.Defense.ToString().ToUpperInvariant() + " + " + package.Tactic.ToString().ToUpperInvariant();
            ShipAI.Instance?.SayMothership(package.Taunt, banner);
        }

        public void Clear()
        {
            weaponRevision++;
            pendingWeapon = null;
            proposedPackage = null;
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
