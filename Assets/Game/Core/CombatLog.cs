using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Armory.Core
{
    /// <summary>Damage dealt per primitive; what the mothership studies to adapt.</summary>
    public sealed class CombatLog
    {
        private readonly Dictionary<string, float> damage = new Dictionary<string, float>();
        private readonly Dictionary<string, int> weaponsUsed = new Dictionary<string, int>();

        public float Total { get; private set; }
        public IReadOnlyDictionary<string, float> Damage => damage;

        public void Record(ParsedWeapon weapon, float amount)
        {
            if (weapon == null || amount <= 0f) return;
            Total += amount;
            foreach (var primitive in weapon.Primitives())
                damage[primitive] = (damage.TryGetValue(primitive, out var value) ? value : 0f) + amount;
            weaponsUsed[weapon.Name] = (weaponsUsed.TryGetValue(weapon.Name, out var count) ? count : 0) + 1;
        }

        /// <summary>Most damaging primitive, or null when nothing was logged.</summary>
        public string TopPrimitive(ICollection<string> exclude = null) =>
            damage.Where(pair => exclude == null || !exclude.Contains(pair.Key))
                  .OrderByDescending(pair => pair.Value)
                  .Select(pair => pair.Key)
                  .FirstOrDefault();

        public string Summary()
        {
            if (damage.Count == 0) return "no damage dealt";
            var builder = new StringBuilder();
            foreach (var pair in damage.OrderByDescending(p => p.Value))
                builder.Append(pair.Key).Append(": ").Append(Total > 0 ? (int)(100 * pair.Value / Total) : 0).Append("%, ");
            builder.Append("weapons: ").Append(string.Join(", ", weaponsUsed.Keys));
            return builder.ToString();
        }

        public void Clear()
        {
            damage.Clear();
            weaponsUsed.Clear();
            Total = 0f;
        }
    }
}
