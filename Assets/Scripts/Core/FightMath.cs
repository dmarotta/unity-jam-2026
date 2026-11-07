namespace Lucky
{
    public enum HitOutcome { Hit, Jackpot, Bust, Miss }

    public struct HitResult
    {
        public HitOutcome Outcome;
        public int Damage, HpBefore, HpAfter, Overshoot;
    }

    /// <summary>The exact-zero rule: land on the boss's remaining HP to kill; overshoot wraps back from max.</summary>
    public static class FightMath
    {
        /// <summary>Resolve a hit. Without <paramref name="wrap"/> (Justice) an overshoot is a harmless Miss instead of a Bust.</summary>
        public static HitResult Apply(int hp, int maxHp, int damage, bool wrap = true)
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
            r.Overshoot = damage - hp;
            if (!wrap)
            {
                r.Outcome = HitOutcome.Miss;
                r.Damage = 0;
                r.HpAfter = hp;
                return r;
            }
            r.Outcome = HitOutcome.Bust;
            int wrapped = maxHp - r.Overshoot % maxHp;
            r.HpAfter = wrapped <= 0 ? maxHp : wrapped;
            return r;
        }

        /// <summary>
        /// Lucky charms: a landed value within <paramref name="below"/> under or <paramref name="above"/> over the
        /// boss's HP snaps onto it (Loaded Die: below 1; Lucky Horseshoe: both 1).
        /// </summary>
        public static int Charm(int value, int hp, int below, int above) => value >= hp - below && value <= hp + above ? hp : value;

        /// <summary>Wheel of Fortune keeps the better of two results: a kill, then the bigger hit, then a miss, then the smaller bust.</summary>
        public static bool Better(HitResult a, HitResult b) => Rank(a) > Rank(b);

        static int Rank(HitResult r) => r.Outcome switch
        {
            HitOutcome.Jackpot => 1000,
            HitOutcome.Hit => 100 + r.Damage,
            HitOutcome.Miss => 50,
            _ => -r.Overshoot,
        };

        /// <summary>Weighted slice pick. The exact-kill slice gains <paramref name="killBonus"/> weight (pity per miss, clover).</summary>
        public static int PickSlice(System.Random rng, int[] values, int hp, float killBonus)
        {
            float total = 0f;
            var weights = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                weights[i] = 1f + (values[i] == hp ? killBonus : 0f);
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
