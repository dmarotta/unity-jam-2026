import random, statistics
from balance_sim2 import fight
wheel = [1,2,3,4,5,6,7,8,9,10,11,12]
rng = random.Random(5)
for pity in (0.35, 0.5):
    print("pity", pity)
    for hp in (10, 14, 18, 22, 27):
        L = sorted(fight(hp, wheel, rng, pity, 999)[0] for _ in range(20000))
        n=len(L)
        def surv(T): return sum(1 for x in L if x<=T)/n
        row = " ".join(f"T{T}:{surv(T):.2f}" for T in (6,8,10,12,14,16,18,20,24))
        print(f" HP{hp:3d} mean {statistics.mean(L):5.1f} p90 {L[int(n*.9)]:3d} | {row}")
