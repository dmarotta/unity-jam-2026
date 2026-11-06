using UnityEngine;

namespace Lucky
{
    /// <summary>The hero's run stats: rules.csv bases plus every passive's level × value, summed by its shop.csv `stat`.</summary>
    public class Stats
    {
        public int MaxHp, Armor, CharmBelow, CharmAbove, PotionBonus, CloverBonus, Revives;
        public float RegenFraction, PityPerMiss, GoldPerDamage, BossGoldMultiplier;

        public static int Bonus(string stat)
        {
            int sum = 0;
            foreach (var item in Balance.Shop)
                if (!item.Consumable && item.Stat == stat) sum += Mathf.Min(item.Level, item.Max) * item.Value;
            return sum;
        }

        public static Stats Current() => new()
        {
            MaxHp = Balance.HeroHp + Bonus("max_hp"),
            Armor = Bonus("armor"),
            CharmBelow = Bonus("charm_below"),
            CharmAbove = Bonus("charm_above"),
            PotionBonus = Bonus("potion_heal"),
            CloverBonus = Bonus("clover_bonus"),
            Revives = Bonus("revive"),
            RegenFraction = (Balance.RegenPercent + Bonus("regen")) / 100f,
            PityPerMiss = Balance.PityPerMiss + Bonus("pity") / 100f,
            GoldPerDamage = Balance.GoldPerDamage * (1f + Bonus("gold") / 100f),
            BossGoldMultiplier = 1f + Bonus("boss_gold") / 100f,
        };

        /// <summary>Headline numbers for the shop's stat strip, in display order.</summary>
        public (string label, string value)[] Summary() => new[]
        {
            ("Max HP", MaxHp.ToString()),
            ("Regen", $"{Mathf.RoundToInt(RegenFraction * 100f)}%"),
            ("Armor", Armor.ToString()),
            ("Luck", $"+{PityPerMiss:0.0}/miss"),
            ("Gold", $"×{GoldPerDamage:0.##}"),
        };
    }
}
