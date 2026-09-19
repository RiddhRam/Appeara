namespace Armory.Core
{
    public enum HiveOrgan { LeftClaw, RightClaw, SporeSac, Crest }
    public enum HiveAttack { SweepLeft, SweepRight, Spores, Wreck }

    /// <summary>Encounter rules, separate from animation and Unity timing.</summary>
    public sealed class HiveAvatarState
    {
        public const float OrganHealth = 180f;
        public string Plating { get; private set; }
        public int BrokenCount { get; private set; }
        private readonly float[] health = { OrganHealth, OrganHealth, OrganHealth, OrganHealth };
        public bool IsBroken(HiveOrgan organ) => health[(int)organ] <= 0f;
        public bool HitOrgan(HiveOrgan organ, float damage)
        {
            if (IsBroken(organ) || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return false;
            health[(int)organ] = System.Math.Max(0f, health[(int)organ] - damage);
            if (!IsBroken(organ)) return false;
            BrokenCount++;
            return true;
        }
        public bool CanAttack(HiveAttack attack) => !IsBroken((HiveOrgan)attack);
        public void Adapt(string primitive)
        {
            if (AdaptationRules.IsPrimitive(primitive)) Plating = primitive;
        }
        public float DamageScale(ParsedWeapon weapon)
        {
            if (weapon != null && Plating != null)
                foreach (string primitive in weapon.Primitives())
                    if (primitive == Plating) return DamageTable.ResistFactor;
            return 1f;
        }
    }
}
