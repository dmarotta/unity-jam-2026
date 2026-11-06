import random, statistics

def fight(maxhp, wheel, rng, pity, heroturns, cap=500):
    hp = maxhp; spins = 0; busts = 0; miss = 0
    top = max(wheel)
    while spins < cap:
        w = [1.0]*len(wheel)
        if hp <= top and pity > 0:
            for i,v in enumerate(wheel):
                if v == hp: w[i] += pity*miss
        d = rng.choices(wheel, weights=w)[0]; spins += 1
        if d == hp: return spins, busts, spins <= heroturns
        if hp <= top: miss += 1
        if d < hp: hp -= d
        else: hp = maxhp - (d - hp); busts += 1
    return spins, busts, False

def report(name, wheel, bosses, pity, n=20000, seed=2):
    rng = random.Random(seed)
    print(f"\n== {name} pity={pity}")
    runwin = 1.0; tot = 0
    for hp, heroturns in bosses:
        s = [fight(hp, wheel, rng, pity, heroturns) for _ in range(n)]
        sp = sorted(x[0] for x in s)
        surv = sum(x[2] for x in s)/n; runwin *= surv; tot += statistics.mean(sp)
        print(f" HP {hp:3d} heroTurns {heroturns:3d}: mean {statistics.mean(sp):5.1f} med {sp[n//2]:3d} p90 {sp[int(n*.9)]:3d} busts {statistics.mean(x[1] for x in s):.2f} survive {surv:.2f}")
    print(f" run win ~{runwin:.2f}, total spins ~{tot:.0f}")

wheel12 = [1,2,3,4,5,6,7,8,9,10,11,12]
wheel8x = [1,2,3,4,5,6,7,8,1,2,3,5]
bosses = [(12,30),(16,32),(20,34),(25,38),(30,42)]
for p in (0, 0.5, 1.0):
    report("1-12", wheel12, bosses, p)
for p in (0, 1.0):
    report("1-8 heavy", wheel8x, bosses, p)
