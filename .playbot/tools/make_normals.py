"""Normal maps for painted 2D art, plus light-source detection for backgrounds.

Characters/VFX: rounded silhouette bevel (distance transform) + high-pass luminance relief.
Backgrounds: softer luminance relief only. Output: <name>_n.png next to each source.
Lights: Assets/Resources/Data/lights.json — bright warm/green/violet blobs per background (normalized uv).
"""
import glob, json, os
import numpy as np
from PIL import Image
from scipy import ndimage

ART = "F:/unity-jam-2026/Assets/Art/"
ANIM = "F:/unity-jam-2026/Assets/Resources/Anim/"


def luminance(rgb):
    return (0.3 * rgb[..., 0] + 0.59 * rgb[..., 1] + 0.11 * rgb[..., 2]) / 255.0


def to_normal(height, strength):
    gy, gx = np.gradient(height)
    n = np.dstack([-gx * strength, gy * strength, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save_half(rgb, out):
    """Normals are low-frequency: store them at exactly half resolution (sources are 8-aligned)."""
    img = Image.fromarray(np.dstack([rgb, np.full(rgb.shape[:2], 255, np.uint8)]), "RGBA")
    img.resize((rgb.shape[1] // 2, rgb.shape[0] // 2), Image.LANCZOS).save(out)


def character_normal(path, out):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    a = im[..., 3] / 255.0
    scale = max(im.shape[:2]) / 1000.0
    inside = ndimage.distance_transform_edt(a > 0.5)
    bevel = 1.0 - np.exp(-inside / (14.0 * scale + 1e-3))
    bevel = ndimage.gaussian_filter(bevel, 2.0 * scale + 0.5)
    lum = luminance(im)
    detail = ndimage.gaussian_filter(lum, 1.0) - ndimage.gaussian_filter(lum, 7.0 * scale + 1)
    height = bevel * 0.9 + detail * 1.4
    save_half(to_normal(height, 18.0), out)


def background_normal(path, out):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    lum = luminance(im)
    height = ndimage.gaussian_filter(lum, 1.2) * 0.6 + ndimage.gaussian_filter(lum, 5.0) * 0.4
    save_half(to_normal(height, 10.0), out)


def detect_lights(path, max_lights=7):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0
    mx, mn = im.max(2), im.min(2)
    sat = (mx - mn) / (mx + 1e-4)
    hot = (mx > 0.82) & (sat > 0.35)
    hot = ndimage.binary_opening(hot, iterations=1)
    hot = ndimage.binary_dilation(hot, iterations=6)  # merge flame fragments
    lab, n = ndimage.label(hot)
    lights = []
    h, w = hot.shape
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if len(xs) < 120:
            continue
        col = im[ys, xs].mean(0)
        col = col / max(col.max(), 1e-3)
        lights.append({"u": float(xs.mean() / w), "v": float(ys.mean() / h), "size": float(np.sqrt(len(xs)) / w),
                       "color": [round(float(c), 3) for c in col]})
    lights.sort(key=lambda l: -l["size"])
    return lights[:max_lights]


def main():
    os.makedirs(ART + "Normals", exist_ok=True)
    for path in glob.glob(ART + "Characters/*.png"):
        name = os.path.splitext(os.path.basename(path))[0]
        character_normal(path, ART + f"Normals/{name}_n.png")
    for path in glob.glob(ANIM + "*.png"):
        if path.endswith("_n.png"):
            continue
        name = os.path.splitext(os.path.basename(path))[0]
        character_normal(path, ANIM + f"{name}_n.png")
    lights = {}
    for path in glob.glob(ART + "Backgrounds/bg_*.png"):
        name = os.path.splitext(os.path.basename(path))[0]
        background_normal(path, ART + f"Normals/{name}_n.png")
        lights[name] = detect_lights(path)
    with open("F:/unity-jam-2026/Assets/Resources/Data/lights.json", "w") as f:
        json.dump({"arenas": [{"background": k, "lights": v} for k, v in sorted(lights.items())]}, f, indent=1)
    for k, v in sorted(lights.items()):
        print(k, len(v), [(round(l["u"], 2), round(l["v"], 2)) for l in v])


if __name__ == "__main__":
    main()
