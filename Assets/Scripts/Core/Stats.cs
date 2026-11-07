using UnityEngine;

namespace Lucky
{
    /// <summary>The hero's run stats: rules.csv bases plus every passive's level × value, summed by its shop.csv `stat`.</summary>
    public class Stats
    {
        public int MaxHp, Armor, CharmBelow, CharmAbove, PotionBonus, CloverBonus, Revives;
        public float RegenFraction, PityPerMiss, GoldPerDamage, BossGoldMultiplier, Dodge;

        public static int Bonus(string stat) => Bonus(stat, i => i.Level);

        /// <summary>Sum of level × value over passives granting <paramref name="stat"/>, with levels read from <paramref name="level"/>.</summary>
        public static int Bonus(string stat, System.Func<ShopItem, int> level)
        {
            int sum = 0;
            foreach (var item in Balance.Shop)
                if (!item.Consumable && item.Stat == stat) sum += Mathf.Min(level(item), item.BaseMax) * item.Value;
            return sum;
        }

        public static Stats Current() => From(i => i.Level);

        /// <summary>Stats for any set of shop levels (the balance simulator plays loadouts that aren't saved).</summary>
        public static Stats From(System.Func<ShopItem, int> level)
        {
            int B(string stat) => Bonus(stat, level);
            return new Stats
            {
                MaxHp = Balance.HeroHp + B("max_hp"),
                Armor = B("armor"),
                CharmBelow = B("charm_below"),
                CharmAbove = B("charm_above"),
                PotionBonus = B("potion_heal"),
                CloverBonus = B("clover_bonus"),
                Revives = B("revive"),
                RegenFraction = (Balance.RegenPercent + B("regen")) / 100f,
                PityPerMiss = Balance.PityPerMiss + B("pity") / 100f,
                GoldPerDamage = Balance.GoldPerDamage * (1f + B("gold") / 100f),
                BossGoldMultiplier = 1f + B("boss_gold") / 100f,
                Dodge = (Balance.HeroDodge + B("dodge")) / 100f,
            };
        }

        /// <summary>Headline numbers for the shop's stat strip, in display order.</summary>
        public (string label, string value)[] Summary() => new[]
        {
            ("Max HP", MaxHp.ToString()),
            ("Regen", $"{Mathf.RoundToInt(RegenFraction * 100f)}%"),
            ("Armor", Armor.ToString()),
            ("Dodge", $"{Mathf.RoundToInt(Dodge * 100f)}%"),
            ("Luck", $"+{Mathf.RoundToInt(PityPerMiss * 100f)}%/miss"),
            ("Gold", $"×{GoldPerDamage:0.##}"),
        };
    }
}
