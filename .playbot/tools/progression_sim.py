"""Monte-Carlo the meta progression straight from the balance sheets (Assets/Resources/Data/*.csv).

Simulates players who run, bank gold, then shop between runs with a fixed priority, and reports how many
runs it takes to break the curse, where each run ends, and rough play time.

usage: python progression_sim.py [players=2000]
"""
import csv, os, random, statistics, sys

# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"
D = ROOT + "Assets/Resources/Data/"

bosses = list(csv.DictReader(open(D + "bosses.csv")))
wheel = [int(r["value"]) for r in sorted(csv.DictReader(open(D + "wheel.csv")), key=lambda r: int(r["slot"]))]
rules = {r["key"]: float(r["value"]) for r in csv.DictReader(open(D + "rules.csv"))}
shop = {r["id"]: r for r in csv.DictReader(open(D + "shop.csv"))}
for item in shop.values():
    item["costs"] = [int(c) for c in item["cost"].split(";")]
    item["max"], item["value"] = int(item["max"]), int(item["value"])

HERO_HP, PITY, GOLD_PER_DMG = int(rules["hero_hp"]), rules["pity_per_miss"], rules["gold_per_damage"]
TOP = max(wheel)
# Seconds of play per spin turn, per boss (intro, kill, run between depths) and per shop visit.
SPIN_S, BOSS_S, SHOP_S = 6.0, 9.0, 15.0
# Shop priority: (item id, level to reach). Bombs are topped up with leftover gold.
PRIORITY = [("vitality", 1), ("loaded_die", 1), ("vitality", 2), ("vitality", 3), ("horseshoe", 1),
            ("vitality", 4), ("vitality", 5), ("vitality", 6), ("vitality", 7), ("vitality", 8)]
BOMB_RESERVE = 2


def kind_level(levels, kind):
    return max((levels[i] for i, it in shop.items() if it["kind"] == kind), default=0)


def resolve(v, hp, reach_lo, reach_hi):
    """The value the charms turn a landed number into."""
    return hp if hp - reach_lo <= v <= hp + reach_hi else v


def fight(boss, hero_max, levels, rng, stats):
    hp = mx = int(boss["hp"])
    hero, misses = hero_max, 0
    lo = 1 if kind_level(levels, "plus_one") or kind_level(levels, "plus_minus_one") else 0
    hi = 1 if kind_level(levels, "plus_minus_one") else 0
    bomb = shop["firebomb"]["value"]
    while True:
        # Bomb: take an exact kill, or pull an out-of-range HP into wheel range.
        if levels["firebomb"] and (hp == bomb or hp > TOP + lo >= hp - bomb):
            levels["firebomb"] -= 1
            stats["gold"] += bomb * GOLD_PER_DMG
            if hp == bomb:
                return True
            hp -= bomb
        killable = any(resolve(v, hp, lo, hi) == hp for v in wheel)
        weights = [1 + (PITY * misses if v == hp else 0) for v in wheel]
        v = resolve(rng.choices(wheel, weights=weights)[0], hp, lo, hi)
        stats["spins"] += 1
        stats["gold"] += v * GOLD_PER_DMG
        if v == hp:
            stats["gold"] += int(boss["gold_bonus"])
            return True
        if killable:
            misses += 1
        hp = (hp - v) % mx or mx
        hero -= rng.randint(int(boss["dmg_min"]), int(boss["dmg_max"]))
        if hero <= 0:
            return False


def play_run(levels, rng):
    hero_max = HERO_HP + levels["vitality"] * shop["vitality"]["value"]
    stats = {"spins": 0, "gold": 0}
    for depth, boss in enumerate(bosses):
        if not fight(boss, hero_max, levels, rng, stats):
            return depth, stats
    return len(bosses), stats


def go_shopping(levels, gold):
    for item_id, target in PRIORITY:
        item = shop[item_id]
        if levels[item_id] >= target or levels[item_id] >= item["max"]:
            continue
        if item["requires"] and not levels[item["requires"]]:
            continue
        cost = item["costs"][min(levels[item_id], len(item["costs"]) - 1)]
        if cost > gold:
            return gold  # Saving up for the next priority.
        gold -= cost
        levels[item_id] += 1
    bomb = shop["firebomb"]
    while levels["firebomb"] < min(BOMB_RESERVE, bomb["max"]) and gold >= bomb["costs"][0]:
        gold -= bomb["costs"][0]
        levels["firebomb"] += 1
    return gold


def career(rng, max_runs=40):
    levels = {i: 0 for i in shop}
    gold, seconds, depths = 0, 0.0, []
    for run in range(1, max_runs + 1):
        depth, stats = play_run(levels, rng)
        depths.append(depth)
        gold += stats["gold"]
        seconds += stats["spins"] * SPIN_S + (depth + 1) * BOSS_S
        if depth == len(bosses):
            return run, seconds, depths, dict(levels)
        gold = go_shopping(levels, gold)
        seconds += SHOP_S
    return None, seconds, depths, dict(levels)


def main():
    players = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    rng = random.Random(11)
    results = [career(rng) for _ in range(players)]
    won = [r for r in results if r[0]]
    runs = sorted(r[0] for r in won)
    print(f"won within 40 runs: {len(won) / players:.1%}")
    if runs:
        print(f"runs to first win: median {runs[len(runs) // 2]}, p10 {runs[len(runs) // 10]}, p90 {runs[int(len(runs) * .9)]}")
        mins = sorted(r[1] / 60 for r in won)
        print(f"minutes to first win: median {mins[len(mins) // 2]:.0f}, p90 {mins[int(len(mins) * .9)]:.0f}")
    for k in range(6):
        reached = [r[2][k] for r in results if len(r[2]) > k]
        hist = [sum(1 for d in reached if d == i) / len(reached) for i in range(len(bosses) + 1)]
        print(f"run {k + 1}: died at depth " + " ".join(f"{i + 1}:{p:4.0%}" for i, p in enumerate(hist[:-1])) + f" | won {hist[-1]:.0%}")

    # Fully upgraded run win rate and per-boss survival.
    full = {i: it["max"] for i, it in shop.items()}
    full["firebomb"] = 0
    for label, lv in (("no upgrades", {i: 0 for i in shop}), ("fully upgraded, no bombs", full)):
        n, deaths = 4000, [0] * (len(bosses) + 1)
        for _ in range(n):
            deaths[play_run(dict(lv), rng)[0]] += 1
        print(f"{label}: win {deaths[-1] / n:.0%}  deaths " + " ".join(f"{b['id']}:{deaths[i] / n:.0%}" for i, b in enumerate(bosses)))


if __name__ == "__main__":
    main()
