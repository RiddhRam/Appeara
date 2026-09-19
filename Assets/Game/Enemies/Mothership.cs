using System.Collections.Generic;
using System.Linq;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Alien hive mind. Between waves it studies the combat log and adopts counters (LLM pick, rule fallback).
    /// During the boss wave it re-adapts live every <see cref="BossAdaptSeconds"/>.
    /// </summary>
    public sealed class Mothership : MonoBehaviour
    {
        public static Mothership Instance { get; private set; }
        public const float BossAdaptSeconds = 20f;

        public readonly HashSet<string> Resistances = new HashSet<string>();
        public readonly HashSet<CounterKind> Counters = new HashSet<CounterKind>();
        public readonly List<Counter> History = new List<Counter>();
        public bool Thinking { get; private set; }

        private static readonly string[] bossTaunts =
        {
            "{0}? Adorable. We have grown immune.",
            "Your {0} tickles now, little station.",
            "We have tasted your {0}. It will not work twice.",
            "More {0}? We evolve faster than you invent.",
        };

        private void Awake() => Instance = this;

        public bool Has(CounterKind kind) => Counters.Contains(kind);

        public void Apply(IEnumerable<Counter> counters)
        {
            foreach (var counter in counters)
            {
                if (counter.Kind == CounterKind.Resist) Resistances.Add(counter.Target);
                else Counters.Add(counter.Kind);
                if (!History.Contains(counter)) History.Add(counter);
            }
        }

        public void Clear()
        {
            Resistances.Clear();
            Counters.Clear();
            History.Clear();
        }

        public string Describe() => History.Count == 0 ? "none" : string.Join(", ", History.Select(c => c.ToString()));

        /// <summary>Between-wave adaptation. Always completes, even offline, within the client timeout.</summary>
        public async Awaitable AdaptAfterWave(CombatLog log, string nextWaveName)
        {
            if (log.Total <= 0f) return;
            Thinking = true;
            List<Counter> counters = null;
            string taunt = null;
            var ai = ShipAI.Instance;
            try
            {
                if (ai != null && ai.OpenAI != null)
                {
                    var result = await ai.OpenAI.Adapt(log.Summary(), Describe(), nextWaveName);
                    if (result != null)
                    {
                        counters = AdaptationRules.Sanitize(result.counters);
                        taunt = result.taunt;
                    }
                }
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Mothership LLM adaptation failed, using rules: " + error.Message);
            }
            finally
            {
                Thinking = false;
            }
            if (counters == null || counters.Count == 0)
            {
                counters = AdaptationRules.Fallback(log);
                taunt = $"Your {log.TopPrimitive()} weapons are predictable. We adapt: {string.Join(", ", counters)}.";
            }
            Apply(counters);
            ai?.SayMothership(taunt, "ADAPTED: " + string.Join(", ", counters));
        }

        /// <summary>Boss-only: instantly resist whatever the player leaned on in the last window.</summary>
        public void BossAdapt(CombatLog window)
        {
            if (HiveAvatar.Active != null)
            {
                if (!HiveAvatar.Active.Ready) return;
                string primitive = window.TopPrimitive();
                if (primitive == null) return;
                HiveAvatar.Active.Adapt(primitive);
                ShipAI.Instance?.SayMothership("We have grown plating against your " + primitive + ". Invent something else.", "AVATAR RESISTS: " + primitive);
                return;
            }
            string top = window.TopPrimitive(Resistances);
            if (top == null) return;
            Apply(new[] { new Counter(CounterKind.Resist, top) });
            ShipAI.Instance?.SayMothership(string.Format(bossTaunts[Random.Range(0, bossTaunts.Length)], top), "BOSS RESISTS: " + top);
        }
    }
}
