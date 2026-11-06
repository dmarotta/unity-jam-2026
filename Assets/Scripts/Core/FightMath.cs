namespace Lucky
{
    public enum HitOutcome { Hit, Jackpot, Bust }

    public struct HitResult
    {
        public HitOutcome Outcome;
        public int Damage, HpBefore, HpAfter, Overshoot;
    }

    /// <summary>The exact-zero rule: land on the boss's remaining HP to kill; overshoot wraps back from max.</summary>
    public static class FightMath
    {
        public static HitResult Apply(int hp, int maxHp, int damage)
        {
            var r = new HitResult { Damage = damage, HpBefore = hp };
            if (damage == hp)
            {
                r.Outcome = HitOutcome.Jackpot;
                r.HpAfter = 0;
                return r;
            }
            if (damage < hp)
            {
                r.Outcome = HitOutcome.Hit;
                r.HpAfter = hp - damage;
                return r;
            }
            r.Outcome = HitOutcome.Bust;
            r.Overshoot = damage - hp;
            int wrapped = maxHp - r.Overshoot % maxHp;
            r.HpAfter = wrapped <= 0 ? maxHp : wrapped;
            return r;
        }

        /// <summary>
        /// Lucky charms: a landed value within <paramref name="below"/> under or <paramref name="above"/> over the
        /// boss's HP snaps onto it (Loaded Die: below 1; Lucky Horseshoe: both 1).
        /// </summary>
        public static int Charm(int value, int hp, int below, int above) => value >= hp - below && value <= hp + above ? hp : value;

        /// <summary>Weighted slice pick. The exact-kill slice gains <paramref name="pityPerMiss"/> weight per earlier miss.</summary>
        public static int PickSlice(System.Random rng, int[] values, int hp, int misses, float pityPerMiss)
        {
            float total = 0f;
            var weights = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                weights[i] = 1f + (values[i] == hp ? pityPerMiss * misses : 0f);
                total += weights[i];
            }
            double roll = rng.NextDouble() * total;
            for (int i = 0; i < values.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return i;
            }
            return values.Length - 1;
        }
    }
}
