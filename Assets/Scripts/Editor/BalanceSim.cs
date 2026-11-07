using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Lucky
{
    /// <summary>
    /// Monte Carlo balance check over the balance sheets: plays whole careers (runs, banking gold, buying the cheapest open
    /// shop node between runs) with a scripted player and reports how long the first win takes and where runs die.
    /// The fight rules mirror GameDirector's turn order; the time model is a rough per-turn estimate of real play.
    /// </summary>
    public static class BalanceSim
    {
        const float SecondsPerTurn = 6.5f, SecondsPerDepth = 8f, SecondsPerRun = 6f;
        const int MaxRuns = 400;

        [MenuItem("Lucky/Simulate Balance")]
        static void Menu() => Debug.Log(Report(300) + "\n" + Report(300, randomShop: true));

        /// <summary>Plays <paramref name="careers"/> careers; <paramref name="randomShop"/> buys a random open node instead of the cheapest.</summary>
        public static string Report(int careers, int seed = 1, bool randomShop = false)
        {
            Balance.Load();
            var bosses = Balance.Bosses;
            var runsToWin = new List<int>();
            var minutesToWin = new List<float>();
            var firstReach = bosses.Select(_ => new List<int>()).ToArray();
            var attempts = new int[bosses.Count];
            var deaths = new int[bosses.Count];
            var turns = new long[bosses.Count];
            var fights = new int[bosses.Count];
            var shopShare = new List<float>();
            int losses = 0;

            for (int c = 0; c < careers; c++)
            {
                var career = new Career(new System.Random(seed + c * 7919), randomShop);
                float seconds = 0f;
                int runs = 0;
                bool won = false;
                while (!won && runs < MaxRuns)
                {
                    runs++;
                    var run = new Fight(career.Rng, career.Level).Play();
                    career.Gold += run.Gold;
                    seconds += SecondsPerRun + run.Turns * SecondsPerTurn + (run.Cleared + 1) * SecondsPerDepth;
                    for (int d = 0; d <= Mathf.Min(run.Cleared, bosses.Count - 1); d++)
                    {
                        attempts[d]++;
                        if (d < run.Cleared) { fights[d]++; turns[d] += run.FightTurns[d]; }
                        if (d >= career.Best && d == run.Cleared) firstReach[d].Add(runs);
                    }
                    if (run.Cleared < bosses.Count) deaths[run.Cleared]++;
                    career.Best = Mathf.Max(career.Best, run.Cleared + 1);
                    won = run.Cleared == bosses.Count;
                    career.Shop();
                }
                if (!won) { losses++; continue; }
                runsToWin.Add(runs);
                minutesToWin.Add(seconds / 60f);
                shopShare.Add(career.SpentShare());
            }

            var sb = new StringBuilder();
            sb.AppendLine($"BALANCE SIM: {careers} careers, {(randomShop ? "random" : "cheapest")}-node-first shopping");
            sb.AppendLine(runsToWin.Count == 0 ? "No career won within the run cap."
                : $"First win: runs p10/p50/p90 = {P(runsToWin, 0.1f)}/{P(runsToWin, 0.5f)}/{P(runsToWin, 0.9f)}  ·  minutes = " +
                  $"{P(minutesToWin, 0.1f):0}/{P(minutesToWin, 0.5f):0}/{P(minutesToWin, 0.9f):0}  ·  shop bought by then {P(shopShare, 0.5f):P0}");
            if (losses > 0) sb.AppendLine($"{losses} careers never won within {MaxRuns} runs");
            sb.AppendLine("boss       | first reached (median run) | death rate when fought | avg turns to kill");
            for (int d = 0; d < bosses.Count; d++)
            {
                string reach = d == 0 ? "1" : firstReach[d].Count > 0 ? P(firstReach[d], 0.5f).ToString() : "-";
                float rate = attempts[d] > 0 ? deaths[d] / (float)attempts[d] : 0f;
                float avgTurns = fights[d] > 0 ? turns[d] / (float)fights[d] : 0f;
                sb.AppendLine($"{bosses[d].Id,-10} | {reach,26} | {rate,22:P0} | {avgTurns,17:0.0}");
            }
            return sb.ToString();
        }

        static T P<T>(List<T> values, float q)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp(Mathf.FloorToInt(q * sorted.Count), 0, sorted.Count - 1)];
        }

        /// <summary>Persistent progress across runs: banked gold, best depth and shop levels.</summary>
        class Career
        {
            public readonly System.Random Rng;
            public int Gold, Best;
            readonly bool randomShop;
            readonly Dictionary<ShopItem, int> levels = new();

            public Career(System.Random rng, bool randomShop)
            {
                Rng = rng;
                this.randomShop = randomShop;
            }

            public int Level(ShopItem item) => levels.GetValueOrDefault(item);

            bool Owned(ShopItem item) => item.Gate ? Best >= item.Value : Level(item) > 0;
            int Max(ShopItem item) => item.Consumable ? item.BaseMax + Stats.Bonus("carry", Level) : item.BaseMax;

            public void Shop()
            {
                while (true)
                {
                    var open = Balance.Shop.Where(item => !item.Gate && Level(item) < Max(item) && Price(item) <= Gold
                        && item.Requires.All(r => Owned(Balance.Item(r)))).ToList();
                    if (open.Count == 0) return;
                    var pick = randomShop ? open[Rng.Next(open.Count)] : open.OrderBy(Price).First();
                    Gold -= Price(pick);
                    levels[pick] = Level(pick) + 1;
                }
            }

            int Price(ShopItem item) => item.Costs[Mathf.Min(Level(item), item.Costs.Length - 1)];

            public float SpentShare()
            {
                int spent = 0, total = 0;
                foreach (var item in Balance.Shop.Where(i => !i.Gate))
                    for (int l = 0; l < Max(item); l++)
                    {
                        int price = item.Costs[Mathf.Min(l, item.Costs.Length - 1)];
                        total += price;
                        if (l < Level(item)) spent += price;
                    }
                return total > 0 ? spent / (float)total : 0f;
            }
        }

        struct RunResult
        {
            public int Cleared, Gold, Turns;
            public int[] FightTurns;
        }

        /// <summary>One run with a fixed loadout. The scripted player uses items when they obviously help.</summary>
        class Fight
        {
            readonly System.Random rng;
            readonly Stats st;
            readonly Dictionary<string, int> charges = new();
            readonly Dictionary<string, ShopItem> items = new();
            readonly int[] wheel = Balance.Wheel;
            readonly int maxWheel = Balance.Wheel.Max();

            BossDef b;
            int hp, revives, gold, bossHp, misses, poison;
            bool warded, phylactery;
            float clover;
            int[] burnt;

            public Fight(System.Random rng, Func<ShopItem, int> level)
            {
                this.rng = rng;
                st = Stats.From(level);
                int carry = Stats.Bonus("carry", level);
                foreach (var item in Balance.Shop.Where(i => i.Consumable))
                {
                    items[item.Stat] = item;
                    charges[item.Stat] = Mathf.Min(level(item), item.BaseMax + carry);
                }
            }

            bool Use(string stat)
            {
                if (charges.GetValueOrDefault(stat) <= 0) return false;
                charges[stat]--;
                return true;
            }

            bool Roll(float chance) => rng.NextDouble() < chance;

            public RunResult Play()
            {
                var result = new RunResult { FightTurns = new int[Balance.Bosses.Count] };
                hp = st.MaxHp;
                revives = st.Revives;
                for (int d = 0; d < Balance.Bosses.Count; d++)
                {
                    b = Balance.Bosses[d];
                    bossHp = b.Hp;
                    misses = poison = 0;
                    clover = 0f;
                    warded = false;
                    phylactery = b.Quirk == "phylactery";
                    burnt = new int[wheel.Length];
                    int turn = 0;
                    bool won = false;
                    while (hp > 0)
                    {
                        turn++;
                        result.Turns++;
                        if (Turn(turn)) { won = true; break; }
                    }
                    result.FightTurns[d] = turn;
                    if (!won)
                    {
                        result.Gold = gold;
                        return result;
                    }
                    result.Cleared++;
                    hp = Mathf.Min(st.MaxHp, hp + Mathf.CeilToInt(st.MaxHp * st.RegenFraction));
                }
                result.Gold = gold;
                return result;
            }

            /// <summary>Worst case of the boss's next attack plus poison, after armor.</summary>
            int Threat(int sting) => b.Hits * Mathf.Max(1, b.DmgMax - st.Armor) + sting + poison + (b.Quirk == "venom" ? b.QuirkValue : 0);

            /// <summary>One player turn and the boss's reply; true when the boss died.</summary>
            bool Turn(int turn)
            {
                int sting = 0, mult = 1;
                bool justice = false, twice = false;
                float star = 0f;

                if (b.Quirk == "tarot" && (turn - 1) % b.QuirkValue == 0)
                {
                    var card = PickCard();
                    switch (card.Id)
                    {
                        case "tower": mult = card.Gain; sting = card.Cost; break;
                        case "justice": justice = true; bossHp = Mathf.Min(b.Hp, bossHp + card.Cost); break;
                        case "star": star = card.Gain; hp = Mathf.Max(1, hp - card.Cost); break;
                        case "temperance": hp = Mathf.Min(st.MaxHp, hp + card.Gain); bossHp = Mathf.Min(b.Hp, bossHp + card.Cost); break;
                        case "wheel": twice = true; sting = card.Cost; break;
                    }
                }

                // Items, before the spin.
                if (hp <= Threat(sting))
                {
                    if (!warded && Use("ward")) warded = true;
                    else if (hp < st.MaxHp && Use("potion"))
                    {
                        hp = Mathf.Min(st.MaxHp, hp + items["potion"].Value + st.PotionBonus);
                        poison = 0;
                    }
                }
                while (charges.GetValueOrDefault("bomb") > 0 && (bossHp == items["bomb"].Value || bossHp > maxWheel && bossHp > items["bomb"].Value))
                {
                    Use("bomb");
                    if (Land(FightMath.Apply(bossHp, b.Hp, items["bomb"].Value))) return true;
                    if (hp <= 0 && !Revive()) return false;
                }
                var values = mult == 1 ? wheel : wheel.Select(v => v * mult).ToArray();
                bool killable = values.Any(v => FightMath.Charm(v, bossHp, st.CharmBelow, st.CharmAbove) == bossHp);
                if (killable && Use("clover")) clover += items["clover"].Value + st.CloverBonus;

                // Spin.
                float weight = st.PityPerMiss * misses + clover + star;
                clover = 0f;
                int slot = FightMath.PickSlice(rng, values, bossHp, weight);
                if (twice)
                {
                    int other = FightMath.PickSlice(rng, values, bossHp, weight);
                    if (FightMath.Better(Resolve(values[other], justice), Resolve(values[slot], justice))) slot = other;
                }
                if (burnt[slot] > 0) hp = Mathf.Max(1, hp - b.QuirkValue);

                int value = values[slot];
                if (b.Quirk == "block" && Roll(b.QuirkChance)) value = Mathf.Max(0, value - b.QuirkValue);
                var hit = Resolve(value, justice);
                bool dead = false;
                if (b.Quirk == "swallow" && hit.Damage > 0 && Roll(b.QuirkChance)) gold += Mathf.RoundToInt(hit.Damage * st.GoldPerDamage * b.QuirkValue);
                else if (!Roll(b.Dodge)) dead = Land(hit);
                if (dead) return true;
                if (killable) misses++;
                if (hp <= 0 && !Revive()) return false;

                // Boss turn.
                Attack(sting);
                if (b.Quirk == "venom") hp -= poison;
                if (b.Quirk == "scorch") Scorch();
                if (hp <= 0) Revive();
                return false;
            }

            HitResult Resolve(int value, bool justice) => FightMath.Apply(bossHp, b.Hp, FightMath.Charm(value, bossHp, st.CharmBelow, st.CharmAbove), !justice);

            /// <summary>Damage lands; true when the boss is dead for good.</summary>
            bool Land(HitResult r)
            {
                gold += Mathf.RoundToInt(r.Damage * st.GoldPerDamage);
                switch (r.Outcome)
                {
                    case HitOutcome.Hit:
                        bossHp = r.HpAfter;
                        return false;
                    case HitOutcome.Bust:
                        bossHp = r.HpAfter;
                        if (b.Quirk == "counter") Attack(0);
                        return false;
                    case HitOutcome.Jackpot when phylactery:
                        phylactery = false;
                        bossHp = Mathf.CeilToInt(b.Hp * b.QuirkValue / 100f);
                        return false;
                    case HitOutcome.Jackpot:
                        gold += Mathf.RoundToInt(b.GoldBonus * st.BossGoldMultiplier);
                        return true;
                }
                return false;
            }

            void Attack(int sting)
            {
                if (warded)
                {
                    warded = false;
                    return;
                }
                bool landed = false;
                for (int i = 0; i < b.Hits; i++)
                {
                    if (Roll(st.Dodge)) continue;
                    int raw = rng.Next(b.DmgMin, b.DmgMax + 1) + (landed ? 0 : sting);
                    hp -= Mathf.Max(1, raw - st.Armor);
                    landed = true;
                }
                if (landed && b.Quirk == "venom") poison += b.QuirkValue;
            }

            void Scorch()
            {
                for (int i = 0; i < burnt.Length; i++) burnt[i] = Mathf.Max(0, burnt[i] - 1);
                var fresh = Enumerable.Range(0, burnt.Length).Where(i => burnt[i] == 0).ToList();
                burnt[fresh[rng.Next(fresh.Count)]] = BossDef.ScorchTurns;
            }

            bool Revive()
            {
                if (revives == 0) return false;
                revives--;
                hp = Mathf.Max(1, st.MaxHp / 2);
                return true;
            }

            /// <summary>Two random cards; the scripted player takes the one that fits its HP and the boss's.</summary>
            TarotCard PickCard()
            {
                var deck = Balance.Tarot;
                int a = rng.Next(deck.Count), c = (a + 1 + rng.Next(deck.Count - 1)) % deck.Count;
                return Score(deck[a]) >= Score(deck[c]) ? deck[a] : deck[c];
            }

            float Score(TarotCard card)
            {
                int threat = Threat(0);
                bool killable = bossHp <= maxWheel;
                return card.Id switch
                {
                    "tower" => hp > threat + card.Cost ? (killable ? 0.5f : 3f) : -10f,
                    "justice" => killable ? 2f : 0.5f,
                    "star" => killable && hp > threat + card.Cost ? 3f : -5f,
                    "temperance" => st.MaxHp - hp >= card.Gain ? (hp <= threat * 2 ? 4f : 1.5f) : 0.2f,
                    "wheel" => hp > threat + card.Cost ? 2.5f : -5f,
                    _ => 0f,
                };
            }
        }
    }
}
