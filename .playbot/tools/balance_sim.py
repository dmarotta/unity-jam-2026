"""Monte-Carlo the full run straight from the game's balance sheets (Assets/Resources/Data/*.csv)."""
import csv, random, statistics
D = "F:/unity-jam-2026/Assets/Resources/Data/"
bosses = list(csv.DictReader(open(D + "bosses.csv")))
wheel = [int(r["value"]) for r in sorted(csv.DictReader(open(D + "wheel.csv")), key=lambda r: int(r["slot"]))]
rules = {r["key"]: float(r["value"]) for r in csv.DictReader(open(D + "rules.csv"))}
hero_max, pity = int(rules["hero_hp"]), rules["pity_per_miss"]

def run(rng):
    spins_total, jackpots = 0, 0
    for depth, b in enumerate(bosses):
        hp = mx = int(b["hp"]); hero = hero_max; misses = 0; spins = 0
        while True:
            w = [1 + (pity * misses if v == hp else 0) for v in wheel]
            d = rng.choices(wheel, weights=w)[0]; spins += 1
            killable = hp <= max(wheel)
            if d == hp: jackpots += 1; break
            if killable: misses += 1
            hp = hp - d if d < hp else mx - (d - hp) % mx or mx
            hero -= rng.randint(int(b["dmg_min"]), int(b["dmg_max"]))
            if hero <= 0: return depth, spins_total + spins
        spins_total += spins
    return len(bosses), spins_total

rng = random.Random(7); N = 20000
res = [run(rng) for _ in range(N)]
wins = sum(1 for d, _ in res if d == len(bosses))
print(f"win rate {wins/N:.1%}  avg spins/run {statistics.mean(s for _, s in res):.1f}")
for i, b in enumerate(bosses):
    reached = sum(1 for d, _ in res if d >= i); died = sum(1 for d, _ in res if d == i)
    print(f"  {b['name']:<22} reached {reached/N:5.1%}  died here {died/max(reached,1):5.1%}")
