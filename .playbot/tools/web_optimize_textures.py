"""Right-size and 4-align textures for WebGL (DXT/crunch need dimensions that are multiples of 4).

Originals are backed up once to .playbot/art-src/originals/. Sizes are chosen by on-screen role.
Normal maps are regenerated afterwards by make_normals.py (half resolution via --half).
"""
import glob, os, shutil
from PIL import Image
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"

BACKUP = ROOT + ".playbot/art-src/originals/"

# (glob, max dimension or None to keep size)
RULES = [
    ("Assets/Art/Characters/*.png", 768),
    ("Assets/Art/Backgrounds/bg_*.png", None),
    ("Assets/Art/Backgrounds/fg_pillar.png", 768),
    ("Assets/Art/Backgrounds/edge_fade.png", None),
    ("Assets/Art/UI/logo.png", None),
    ("Assets/Art/UI/bar_frame.png", None),
    ("Assets/Art/UI/panel_frame.png", None),
    ("Assets/Art/UI/spin_button.png", 512),
    ("Assets/Art/UI/tarot_*.png", 512),
    ("Assets/Art/UI/icon_*.png", 256),
    ("Assets/Art/UI/portrait_*.png", 256),
    ("Assets/Art/VFX/vfx_coin.png", 128),
    ("Assets/Art/VFX/vfx_sparkle.png", 128),
    ("Assets/Art/VFX/vfx_card.png", 192),
    ("Assets/Art/VFX/vfx_darkorb.png", 256),
    ("Assets/Art/VFX/vfx_impact.png", 384),
    ("Assets/Art/VFX/*.png", None),
    ("Assets/Art/Wheel/wheel_hub.png", 256),
    ("Assets/Art/Wheel/wheel_pointer.png", 256),
    ("Assets/Art/Wheel/*.png", None),
]


def align(n, a):
    return (n + a - 1) // a * a


def process(path, max_dim):
    rel = os.path.relpath(path, ROOT)
    backup = os.path.join(BACKUP, rel)
    if not os.path.exists(backup):
        os.makedirs(os.path.dirname(backup), exist_ok=True)
        shutil.copy2(path, backup)
    im = Image.open(path)
    mode = im.mode
    im = im.convert("RGBA")
    w, h = im.size
    if max_dim and max(w, h) > max_dim:
        s = max_dim / max(w, h)
        im = im.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
        w, h = im.size
    # Normal-mapped art aligns to 8 so its half-resolution normal map is an exact 2x downscale (still 4-aligned).
    a = 8 if ("/Characters/" in path or "/bg_" in path) else 4
    W, H = align(w, a), align(h, a)
    if (W, H) != (w, h):
        # Pad on the right and top: bottom-centre pivots move a few px horizontally, never vertically.
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.paste(im, (0, H - h))
        im = canvas
    if mode == "RGB":
        im = im.convert("RGB")
    im.save(path)
    return (w, h), im.size


def main():
    done = set()
    for pattern, max_dim in RULES:
        for path in sorted(glob.glob(ROOT + pattern)):
            path = path.replace("\\", "/")
            if path in done or path.endswith("_n.png"):
                continue
            done.add(path)
            before, after = process(path, max_dim)
            if before != after or max_dim:
                print(f"{os.path.relpath(path, ROOT)}: -> {after}")


if __name__ == "__main__":
    main()
