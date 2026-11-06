# Circular HUD portraits cropped from idle sprites. Crops are (center x, center y, size) as fractions of the trimmed sprite.
import os, sys
from PIL import Image, ImageDraw
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"
CH = ROOT + "Assets/Art/Characters/"
OUT = ROOT + "Assets/Art/UI/"
CROPS = {
    "hero":   ("hero_idle",         0.645, 0.135, 0.23),
    "goblin": ("boss1_goblin_idle", 0.62, 0.20, 0.27),
    "golem":  ("boss2_golem_idle",  0.32, 0.12, 0.25),
    "witch":  ("boss3_witch_idle",  0.53, 0.13, 0.20),
    "dragon": ("boss4_dragon_idle", 0.17, 0.17, 0.30),
    "lich":   ("boss5_lich_idle",   0.45, 0.12, 0.22),
    "mimic":  ("boss_mimic_idle",   0.45, 0.38, 0.55),
    "knight": ("boss_knight_idle",  0.47, 0.13, 0.22),
    "spider": ("boss_spider_idle",  0.26, 0.57, 0.32),
}
N = 256
# Optional names on the command line limit the run to those portraits.
for name, (src, cx, cy, size) in CROPS.items():
    if sys.argv[1:] and name not in sys.argv[1:]:
        continue
    im = Image.open(CH + src + ".png").convert("RGBA")
    im = im.crop(im.getbbox())
    s = size * im.height
    x0, y0 = cx * im.width - s / 2, cy * im.height - s / 2
    bg = Image.new("RGBA", im.size, (0, 0, 0, 0))
    crop = im.crop((round(x0), round(y0), round(x0 + s), round(y0 + s))).resize((N, N), Image.LANCZOS)
    back = Image.new("RGBA", (N, N), (34, 20, 28, 255))
    back.alpha_composite(crop)
    mask = Image.new("L", (N * 4, N * 4), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, N * 4 - 1, N * 4 - 1), fill=255)
    back.putalpha(mask.resize((N, N), Image.LANCZOS))
    back.save(OUT + f"portrait_{name}.png")
print("ok")
