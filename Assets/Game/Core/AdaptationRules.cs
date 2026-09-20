using System;
using System.Collections.Generic;
using System.Linq;

namespace Armory.Core
{
    public enum CounterKind { Armor, Shield, Dodge, Teleport, Spread, Rush, Intercept, Reflect, Resist }
    public enum CounterFamily { Defensive, Tactical }

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
                if (parts.Length != 2 || !AdaptationRules.IsPrimitive(parts[1])) return false;
                counter = new Counter(kind, parts[1]);
                return true;
            }
            if (parts.Length != 1) return false;
            counter = new Counter(kind);
            return true;
        }

        public override string ToString() => Kind == CounterKind.Resist ? "resist:" + Target : Kind.ToString().ToLowerInvariant();
        public bool Equals(Counter other) => Kind == other.Kind && Target == other.Target;
        public override bool Equals(object obj) => obj is Counter other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (Target?.GetHashCode() ?? 0);
    }

    /// <summary>The complete, replaceable response to one fabricated weapon.</summary>
    public sealed class CounterPackage
    {
        public readonly Counter Defense;
        public readonly Counter Tactic;
        public readonly string Taunt;
        public readonly int WeaponRevision;
        public readonly string WeaponName;

        public CounterPackage(Counter defense, Counter tactic, string taunt, int weaponRevision, string weaponName)
        {
            Defense = defense;
            Tactic = tactic;
            Taunt = taunt;
            WeaponRevision = weaponRevision;
            WeaponName = weaponName;
        }

        public IEnumerable<Counter> Counters
        {
            get { yield return Defense; yield return Tactic; }
        }

        public override string ToString() => Defense + " + " + Tactic;
    }

    /// <summary>Deterministic combat-time clock; armory and paused frames do not consume the grace period.</summary>
    public sealed class CounterAnalysisClock
    {
        public float Remaining { get; private set; }
        public bool Running { get; private set; }

        public void Start(float seconds)
        {
            Remaining = Math.Max(0f, seconds);
            Running = Remaining > 0f;
        }

        public void Cancel()
        {
            Remaining = 0f;
            Running = false;
        }

        /// <returns>True exactly once when active combat time reaches zero.</returns>
        public bool Advance(float deltaSeconds, bool combatActive)
        {
            if (!Running || !combatActive || deltaSeconds <= 0f) return false;
            Remaining = Math.Max(0f, Remaining - deltaSeconds);
            if (Remaining > 0f) return false;
            Running = false;
            return true;
        }
    }

    /// <summary>Single source of truth shared by validation, gameplay, HUD and the model prompt.</summary>
    public static class CounterCatalog
    {
        public static readonly string[] PrimitiveNames =
        {
            "kinetic", "explosive", "plasma", "electric", "cryo", "beam", "thrown",
            "homing", "piercing", "bouncing", "sticky", "proximity", "splash", "chain", "slow"
        };

        public const string PromptDescription =
            "Defensive counters: armor (more health), shield (energy barrier; electric pierces), " +
            "reflect (mirror plating against beams/plasma), resist:<weapon trait> (70% less damage from that exact trait). " +
            "Tactical counters: dodge (sidestep unguided shots), teleport (blink against homing/continuous aim), " +
            "spread (loose formation against splash/chain/piercing), rush (speed against cryo/slow), " +
            "intercept (destroy grenades, mines and slow projectiles).";

        public static CounterFamily Family(CounterKind kind) =>
            kind == CounterKind.Armor || kind == CounterKind.Shield || kind == CounterKind.Reflect || kind == CounterKind.Resist
                ? CounterFamily.Defensive
                : CounterFamily.Tactical;

        public static bool IsValidForWeapon(Counter counter, CounterFamily family, ParsedWeapon weapon)
        {
            if (weapon == null || Family(counter.Kind) != family) return false;
            if (counter.Kind != CounterKind.Resist) return true;
            return weapon.Primitives().Contains(counter.Target);
        }

        public static string DefendedPrimitive(Counter defense, ParsedWeapon weapon)
        {
            if (defense.Kind == CounterKind.Resist) return defense.Target;
            if (defense.Kind == CounterKind.Reflect && weapon.FireMode == FireMode.Beam) return "beam";
            return weapon.Payload.ToString().ToLowerInvariant();
        }
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

        /// <summary>Builds a complete package, replacing invalid or missing model choices independently.</summary>
        public static CounterPackage ForWeapon(ParsedWeapon weapon, string defense, string tactic, string taunt, int revision)
        {
            Counter defensive = FallbackDefense(weapon);
            Counter tactical = FallbackTactic(weapon);
            if (Counter.TryParse(defense, out var proposedDefense) &&
                CounterCatalog.IsValidForWeapon(proposedDefense, CounterFamily.Defensive, weapon))
                defensive = proposedDefense;
            if (Counter.TryParse(tactic, out var proposedTactic) &&
                CounterCatalog.IsValidForWeapon(proposedTactic, CounterFamily.Tactical, weapon))
                tactical = proposedTactic;
            if (string.IsNullOrWhiteSpace(taunt))
                taunt = $"We have solved your {weapon.Name}. Our drones now wield {defensive} and {tactical}.";
            else if (taunt.Length > 160) taunt = taunt.Substring(0, 157) + "...";
            return new CounterPackage(defensive, tactical, taunt, revision, weapon.Name);
        }

        public static Counter FallbackDefense(ParsedWeapon weapon)
        {
            if (weapon.FireMode == FireMode.Beam || weapon.Payload == Payload.Plasma) return new Counter(CounterKind.Reflect);
            if (weapon.Payload == Payload.Kinetic) return new Counter(CounterKind.Armor);
            if (weapon.Payload == Payload.Electric || weapon.Payload == Payload.Explosive || weapon.Payload == Payload.Cryo)
                return new Counter(CounterKind.Resist, weapon.Payload.ToString().ToLowerInvariant());
            return new Counter(CounterKind.Shield);
        }

        public static Counter FallbackTactic(ParsedWeapon weapon)
        {
            if (weapon.Has(Mods.Homing)) return new Counter(CounterKind.Teleport);
            if (weapon.FireMode == FireMode.Thrown || weapon.Shape == ProjectileShape.Mine ||
                weapon.Has(Mods.Sticky) || weapon.Has(Mods.Proximity) || weapon.ProjectileSpeed <= 20f)
                return new Counter(CounterKind.Intercept);
            if (weapon.Has(Mods.Splash) || weapon.Has(Mods.Chain) || weapon.Has(Mods.Piercing) ||
                weapon.ProjectileCount >= 4 || weapon.SpreadDeg >= 10f)
                return new Counter(CounterKind.Spread);
            if (weapon.Has(Mods.Slow) || weapon.Payload == Payload.Cryo) return new Counter(CounterKind.Rush);
            if (weapon.FireMode == FireMode.Projectile) return new Counter(CounterKind.Dodge);
            return new Counter(CounterKind.Teleport);
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
