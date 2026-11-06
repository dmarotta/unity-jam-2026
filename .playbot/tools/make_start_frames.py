# Composite character sprites onto flat chroma backdrops as video start frames.
import os, sys
from PIL import Image
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"
CH = ROOT + "Assets/Art/Characters/"
OUT = ROOT + ".playbot/anim-src/"
GREEN, MAGENTA = (0, 255, 0), (255, 0, 255)
SIZES = {"3:4": (960, 1280), "4:3": (1280, 960), "1:1": (1152, 1152)}
# name: (source sprite, aspect, chroma, height fraction, x center fraction)
JOBS = {
    "hero_idle":     ("hero_idle",           "3:4", GREEN,   0.74, 0.50),
    "hero_run":      ("hero_run",            "3:4", GREEN,   0.72, 0.50),
    "hero_attack":   ("hero_idle",           "4:3", GREEN,   0.70, 0.30),
    "goblin_idle":   ("boss1_goblin_idle",   "3:4", MAGENTA, 0.74, 0.50),
    "goblin_attack": ("boss1_goblin_idle",   "4:3", MAGENTA, 0.70, 0.66),
    "golem_idle":    ("boss2_golem_idle",    "1:1", GREEN,   0.74, 0.50),
    "golem_attack":  ("boss2_golem_idle",    "4:3", GREEN,   0.70, 0.64),
    "witch_idle":    ("boss3_witch_idle",    "3:4", GREEN,   0.76, 0.50),
    "witch_attack":  ("boss3_witch_idle",    "4:3", GREEN,   0.72, 0.64),
    "dragon_idle":   ("boss4_dragon_idle",   "1:1", GREEN,   0.72, 0.50),
    "dragon_attack": ("boss4_dragon_idle",   "4:3", GREEN,   0.68, 0.64),
    "lich_idle":     ("boss5_lich_idle",     "3:4", MAGENTA, 0.76, 0.50),
    "lich_attack":   ("boss5_lich_idle",     "4:3", MAGENTA, 0.72, 0.64),
    "mimic_idle":    ("boss_mimic_idle",     "1:1", GREEN,   0.70, 0.50),
    "knight_idle":   ("boss_knight_idle",    "3:4", MAGENTA, 0.76, 0.50),
    "spider_idle":   ("boss_spider_idle",    "4:3", MAGENTA, 0.70, 0.50),
}
# Optional job names on the command line limit the run to those frames.
for name, (src, aspect, key, hfrac, xfrac) in JOBS.items():
    if sys.argv[1:] and name not in sys.argv[1:]:
        continue
    W, H = SIZES[aspect]
    sprite = Image.open(CH + src + ".png").convert("RGBA")
    sprite = sprite.crop(sprite.getbbox())
    scale = H * hfrac / sprite.height
    if sprite.width * scale > W * 0.9: scale = W * 0.9 / sprite.width
    sprite = sprite.resize((round(sprite.width * scale), round(sprite.height * scale)), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), key + (255,))
    x = round(W * xfrac - sprite.width / 2); x = max(0, min(W - sprite.width, x))
    y = round(H * 0.92 - sprite.height)
    canvas.alpha_composite(sprite, (x, y))
    canvas.convert("RGB").save(OUT + name + "_start.png")
    print(name, aspect, sprite.size)
