"""Extract a chroma-keyed flipbook from a generated animation video.

usage: python extract_frames.py <video.mp4> <out_name> <mode: loop|once> [frames] [fps] [facing: 1|-1]
Writes Assets/Resources/Anim/<out_name>.png (grid sheet) and <out_name>.json (playback metadata).
"""
import os
import json, sys
import numpy as np
import cv2
from scipy import ndimage
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"

OUT = ROOT + "Assets/Resources/Anim/"
MAX_H = 400


def read_frames(path):
    cap = cv2.VideoCapture(path)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24
    frames = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        frames.append(cv2.cvtColor(f, cv2.COLOR_BGR2RGB).astype(np.float32))
    return frames, fps


def key_color(frame):
    h, w, _ = frame.shape
    border = np.concatenate([frame[:8].reshape(-1, 3), frame[-8:].reshape(-1, 3), frame[:, :8].reshape(-1, 3), frame[:, -8:].reshape(-1, 3)])
    return np.median(border, axis=0)


def chroma_alpha(frame, key):
    r, g, b = frame[..., 0], frame[..., 1], frame[..., 2]
    if key[1] > key[0]:  # green screen
        dom = g - np.maximum(r, b)
    else:  # magenta screen
        dom = np.minimum(r, b) - g
    alpha = 1.0 - np.clip((dom - 28.0) / (95.0 - 28.0), 0.0, 1.0)
    # Despill toward neutral.
    out = frame.copy()
    if key[1] > key[0]:
        out[..., 1] = np.minimum(g, np.maximum(r, b) + 6)
    else:
        m = np.minimum(r, b)
        spill = np.clip(m - g - 6, 0, None)
        out[..., 0] -= spill
        out[..., 2] -= spill
    # Remove specks: keep components larger than 0.15% of the frame.
    solid = alpha > 0.5
    lab, n = ndimage.label(solid)
    if n > 1:
        sizes = ndimage.sum(solid, lab, range(1, n + 1))
        keep = np.isin(lab, np.nonzero(sizes > solid.size * 0.0015)[0] + 1)
        keep = ndimage.binary_dilation(keep, iterations=3)
        alpha = alpha * keep
    alpha = ndimage.gaussian_filter(alpha, 0.6)
    # Feather the video borders so effects clipped by the frame fade out instead of cutting hard.
    h, w = alpha.shape
    m = max(8, int(min(h, w) * 0.05))
    ramp_x = np.clip(np.minimum(np.arange(w), w - 1 - np.arange(w)) / m, 0, 1)
    ramp_y = np.clip(np.minimum(np.arange(h), h - 1 - np.arange(h)) / m, 0, 1)
    alpha = alpha * np.minimum.outer(ramp_y, ramp_x)
    return np.clip(out, 0, 255), np.clip(alpha, 0, 1)


def diff(a, b):
    sa, sb = a[::4, ::4], b[::4, ::4]
    return float(np.mean(np.abs(sa - sb)))


def choose_loop(frames, fps, want, max_span=2.4):
    n = len(frames)
    min_len = int(fps * 1.4)
    max_len = int(fps * max_span)
    best = (1e9, 0, n - 1)
    for i in range(0, max(1, n - min_len), 2):
        for j in range(i + min_len, min(n, i + max_len + 1)):
            d = diff(frames[i], frames[j])
            if d < best[0]:
                best = (d, i, j)
    _, i, j = best
    idx = np.linspace(i, j, want, endpoint=False).round().astype(int)
    return list(idx), (j - i) / fps


def choose_once(frames, fps, want):
    n = len(frames)
    # Trim the static tail: last frame that still differs from its successor-run.
    motion = [diff(frames[k], frames[k + 1]) for k in range(n - 1)]
    thr = max(0.6, np.percentile(motion, 30))
    end = n - 1
    while end > want and motion[end - 1] < thr:
        end -= 1
    start = 0
    while start < end - want and motion[start] < thr * 0.8:
        start += 1
    idx = np.linspace(start, end, want).round().astype(int)
    return list(idx), (end - start) / fps


def main():
    path, name, mode = sys.argv[1], sys.argv[2], sys.argv[3]
    want = int(sys.argv[4]) if len(sys.argv) > 4 else 16
    play_fps = float(sys.argv[5]) if len(sys.argv) > 5 else 12
    facing = int(sys.argv[6]) if len(sys.argv) > 6 else 1
    frames, fps = read_frames(path)
    key = key_color(frames[0])
    idx, span = choose_loop(frames, fps, want) if mode == "loop" else choose_once(frames, fps, want)
    if play_fps <= 0:  # 0 = play back at the source's real-time speed
        play_fps = round(len(idx) / max(span, 0.1), 2)

    keyed = [chroma_alpha(frames[k], key) for k in idx]
    alphas = np.stack([a for _, a in keyed])
    union = alphas.max(0) > 0.08
    ys, xs = np.nonzero(union)
    pad = 6
    x0, x1 = max(0, xs.min() - pad), min(union.shape[1], xs.max() + pad)
    y0, y1 = max(0, ys.min() - pad), min(union.shape[0], ys.max() + pad)

    # Feet anchor from the first frame: horizontal center of the bottom band, bottom of the body.
    a0 = alphas[0] > 0.5
    rows = np.nonzero(a0.any(1))[0]
    foot_y = rows.max()
    body_h = foot_y - rows.min()
    band = a0[max(0, foot_y - int(body_h * 0.12)):foot_y + 1]
    cols = np.nonzero(band.any(0))[0]
    anchor_x = (cols.min() + cols.max()) / 2

    w, h = x1 - x0, y1 - y0
    scale = min(1.0, MAX_H / h)
    # Frames are 8-aligned so the sheet compresses (DXT) and its half-size normal sheet maps exactly.
    fw, fh = (int(round(w * scale)) + 7) // 8 * 8, (int(round(h * scale)) + 7) // 8 * 8
    cols_n = min(len(idx), max(1, int(np.ceil(np.sqrt(len(idx) * fh / fw)))))
    rows_n = int(np.ceil(len(idx) / cols_n))
    sheet = np.zeros((rows_n * fh, cols_n * fw, 4), np.uint8)
    hit_frame, best_reach = 0, -1e9
    for k, (rgb, a) in enumerate(keyed):
        rgba = np.dstack([rgb, a * 255])[y0:y1, x0:x1].astype(np.uint8)
        rgba = cv2.resize(rgba, (fw, fh), interpolation=cv2.INTER_AREA)
        # Premultiplied-edge cleanup: zero color where fully transparent.
        rgba[rgba[..., 3] < 4] = 0
        r, c = divmod(k, cols_n)
        sheet[r * fh:(r + 1) * fh, c * fw:(c + 1) * fw] = rgba
        solid_cols = np.nonzero((a[y0:y1, x0:x1] > 0.5).any(0))[0]
        if len(solid_cols):
            reach = solid_cols.max() if facing > 0 else -solid_cols.min()
            if reach > best_reach:
                best_reach, hit_frame = reach, k

    cv2.imwrite(OUT + name + ".png", cv2.cvtColor(sheet, cv2.COLOR_RGBA2BGRA))
    meta = {
        "frames": len(idx), "columns": cols_n, "frameWidth": fw, "frameHeight": fh,
        "fps": play_fps, "loop": mode == "loop",
        "pivotX": round((anchor_x - x0) / w, 4), "pivotY": round(1 - (foot_y - y0) / h, 4),
        "bodyHeight": round(body_h * scale, 1), "hitFrame": hit_frame if mode == "once" else -1,
        "sourceSpan": round(span, 2), "sourceIndices": [int(i) for i in idx],
    }
    with open(OUT + name + ".json", "w") as f:
        json.dump(meta, f, indent=1)
    print(name, json.dumps({k: v for k, v in meta.items() if k != "sourceIndices"}))


if __name__ == "__main__":
    main()
