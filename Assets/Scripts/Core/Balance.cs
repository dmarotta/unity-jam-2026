using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Lucky
{
    public class BossDef
    {
        public string Id, Name, Title, Arena, Attack, IdleSprite, AttackSprite, HurtSprite, Background;
        public int Hp, DmgMin, DmgMax, GoldBonus;
        public float Height, OffsetX;
        public Color Accent;
    }

    public enum ItemKind { MaxHp, PlusOne, PlusMinusOne, Bomb }

    /// <summary>A shop entry. Levels live in SaveData; consumables use their level as the owned count.</summary>
    public class ShopItem
    {
        public string Id, Name, Requires, Icon, Desc;
        public ItemKind Kind;
        public int Max, Value;
        public int[] Costs;

        public bool Consumable => Kind == ItemKind.Bomb;
        public int Level => SaveData.Level(Id);
        public bool Maxed => Level >= Max;
        public bool Locked => !string.IsNullOrEmpty(Requires) && SaveData.Level(Requires) == 0;
        public int Price => Costs[Mathf.Min(Level, Costs.Length - 1)];

        public bool TryBuy()
        {
            if (Maxed || Locked || Price > SaveData.Gold) return false;
            SaveData.Gold -= Price;
            SaveData.SetLevel(Id, Level + 1);
            return true;
        }
    }

    /// <summary>Spreadsheet-driven balance: Resources/Data/bosses.csv, wheel.csv and rules.csv.</summary>
    public static class Balance
    {
        public static List<BossDef> Bosses { get; private set; }
        public static int[] Wheel { get; private set; }
        public static List<ShopItem> Shop { get; private set; }
        static Dictionary<string, float> rules;

        static readonly Dictionary<string, ItemKind> Kinds = new()
        {
            { "max_hp", ItemKind.MaxHp }, { "plus_one", ItemKind.PlusOne }, { "plus_minus_one", ItemKind.PlusMinusOne }, { "bomb", ItemKind.Bomb },
        };

        public static int HeroHp => (int)Rule("hero_hp", 100);
        public static float PityPerMiss => Rule("pity_per_miss", 0.4f);
        public static float SpinSeconds => Rule("spin_seconds", 3.1f);
        public static float AutoSpinSeconds => Rule("auto_spin_seconds", 2.4f);
        public static float GoldPerDamage => Rule("gold_per_damage", 1f);

        public static void Load()
        {
            Bosses = new List<BossDef>();
            foreach (var row in Read("Data/bosses"))
            {
                ColorUtility.TryParseHtmlString(row["accent"], out var accent);
                Bosses.Add(new BossDef
                {
                    Id = row["id"], Name = row["name"], Title = row["title"], Arena = row["arena"],
                    Hp = Int(row["hp"]), DmgMin = Int(row["dmg_min"]), DmgMax = Int(row["dmg_max"]),
                    Attack = row["attack"], Height = Float(row["height"]), GoldBonus = Int(row["gold_bonus"]),
                    Accent = accent, IdleSprite = row["idle_sprite"], AttackSprite = row["attack_sprite"],
                    HurtSprite = row["hurt_sprite"], Background = row["background"], OffsetX = Float(row["offset_x"]),
                });
            }

            var slots = Read("Data/wheel");
            Wheel = new int[slots.Count];
            foreach (var row in slots) Wheel[Int(row["slot"])] = Int(row["value"]);

            Shop = new List<ShopItem>();
            foreach (var row in Read("Data/shop"))
            {
                Shop.Add(new ShopItem
                {
                    Id = row["id"], Name = row["name"], Kind = Kinds[row["kind"].Trim()], Max = Int(row["max"]),
                    Costs = System.Array.ConvertAll(row["cost"].Split(';'), Int), Value = Int(row["value"]),
                    Requires = row["requires"].Trim(), Icon = row["icon"].Trim(), Desc = row["desc"],
                });
            }

            rules = new Dictionary<string, float>();
            foreach (var row in Read("Data/rules")) rules[row["key"]] = Float(row["value"]);
        }

        static float Rule(string key, float fallback) => rules != null && rules.TryGetValue(key, out var v) ? v : fallback;
        static int Int(string s) => int.Parse(s.Trim(), CultureInfo.InvariantCulture);
        static float Float(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

        static List<Dictionary<string, string>> Read(string resource)
        {
            var rows = new List<Dictionary<string, string>>();
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null)
            {
                Debug.LogError($"Missing balance sheet Resources/{resource}.csv");
                return rows;
            }
            var lines = asset.text.Replace("\r", "").Split('\n');
            var header = SplitCsv(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cells = SplitCsv(lines[i]);
                var row = new Dictionary<string, string>();
                for (int c = 0; c < header.Count; c++) row[header[c].Trim()] = c < cells.Count ? cells[c] : "";
                rows.Add(row);
            }
            return rows;
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char ch in line)
            {
                if (ch == '"') quoted = !quoted;
                else if (ch == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(ch);
            }
            cells.Add(cell.ToString());
            return cells;
        }
    }
}
