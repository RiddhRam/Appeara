using System;
using System.Collections.Generic;

namespace Armory.Core
{
    public enum CounterKind { Armor, Shield, Dodge, Teleport, Spread, Rush, Intercept, Reflect, Resist }

    /// <summary>One mothership adaptation. Resist carries the primitive it resists (e.g. "plasma").</summary>
    public readonly struct Counter : IEquatable<Counter>
    {
        public readonly CounterKind Kind;
        public readonly string Target;

        public Counter(CounterKind kind, string target = null)
        {
            Kind = kind;
            Target = kind == CounterKind.Resist ? target : null;
        }

        public static readonly string[] AllowedNames = { "armor", "shield", "dodge", "teleport", "spread", "rush", "intercept", "reflect", "resist:<primitive>" };

        public static bool TryParse(string text, out Counter counter)
        {
            counter = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().ToLowerInvariant().Split(':');
            if (!Enum.TryParse(parts[0], true, out CounterKind kind) || !Enum.IsDefined(typeof(CounterKind), kind)) return false;
            if (kind == CounterKind.Resist)
            {
                if (parts.Length < 2 || !AdaptationRules.IsPrimitive(parts[1])) return false;
                counter = new Counter(kind, parts[1]);
                return true;
            }
            counter = new Counter(kind);
            return true;
        }

        public override string ToString() => Kind == CounterKind.Resist ? "resist:" + Target : Kind.ToString().ToLowerInvariant();
        public bool Equals(Counter other) => Kind == other.Kind && Target == other.Target;
        public override bool Equals(object obj) => obj is Counter other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (Target?.GetHashCode() ?? 0);
    }

    /// <summary>Deterministic mothership brain; used when the LLM is offline and to sanity-check its picks.</summary>
    public static class AdaptationRules
    {
        public const int MaxCounters = 3;

        private static readonly HashSet<string> primitives = BuildPrimitives();

        public static bool IsPrimitive(string key) => key != null && primitives.Contains(key);

        public static List<Counter> Fallback(CombatLog log)
        {
            var result = new List<Counter>();
            string top = log.TopPrimitive();
            if (top == null) return result;
            Add(result, CounterFor(top));
            Add(result, new Counter(CounterKind.Resist, top));
            string second = log.TopPrimitive(new[] { top });
            if (second != null) Add(result, CounterFor(second));
            return result;
        }

        /// <summary>Parses LLM counters, dropping unknown ones and duplicates.</summary>
        public static List<Counter> Sanitize(IEnumerable<string> names)
        {
            var result = new List<Counter>();
            if (names == null) return result;
            foreach (var name in names)
                if (Counter.TryParse(name, out var counter)) Add(result, counter);
            return result;
        }

        public static Counter CounterFor(string primitive)
        {
            switch (primitive)
            {
                case "explosive":
                case "splash":
                case "piercing":
                    return new Counter(CounterKind.Spread);
                case "beam":
                case "plasma":
                    return new Counter(CounterKind.Reflect);
                case "kinetic":
                    return new Counter(CounterKind.Armor);
                case "homing":
                    return new Counter(CounterKind.Teleport);
                case "cryo":
                case "slow":
                    return new Counter(CounterKind.Rush);
                case "sticky":
                case "proximity":
                case "thrown":
                    return new Counter(CounterKind.Intercept);
                case "electric":
                case "chain":
                    return new Counter(CounterKind.Spread);
                default:
                    return new Counter(CounterKind.Dodge);
            }
        }

        private static void Add(List<Counter> list, Counter counter)
        {
            if (list.Count < MaxCounters && !list.Contains(counter)) list.Add(counter);
        }

        private static HashSet<string> BuildPrimitives()
        {
            var set = new HashSet<string> { "beam", "thrown" };
            foreach (Payload payload in Enum.GetValues(typeof(Payload))) set.Add(payload.ToString().ToLowerInvariant());
            foreach (Mods mod in Enum.GetValues(typeof(Mods)))
                if (mod != Mods.None) set.Add(mod.ToString().ToLowerInvariant());
            return set;
        }
    }
}
