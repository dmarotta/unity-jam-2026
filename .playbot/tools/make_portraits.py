# Circular HUD portraits cropped from idle sprites. Crops are (center x, center y, size) as fractions of the trimmed sprite.
from PIL import Image, ImageDraw
CH = "F:/unity-jam-2026/Assets/Art/Characters/"
OUT = "F:/unity-jam-2026/Assets/Art/UI/"
CROPS = {
    "hero":   ("hero_idle",         0.645, 0.135, 0.23),
    "goblin": ("boss1_goblin_idle", 0.62, 0.20, 0.27),
    "golem":  ("boss2_golem_idle",  0.68, 0.12, 0.25),
    "witch":  ("boss3_witch_idle",  0.53, 0.13, 0.20),
    "dragon": ("boss4_dragon_idle", 0.17, 0.17, 0.30),
    "lich":   ("boss5_lich_idle",   0.45, 0.12, 0.22),
}
N = 256
for name, (src, cx, cy, size) in CROPS.items():
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
