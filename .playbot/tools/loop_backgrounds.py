"""Turn generated ambient background videos into seamless web-friendly loops.

Crossfades the last `fade` seconds into the first, trims to a clean loop, encodes H.264 (yuv420p, faststart)
to Assets/StreamingAssets/Video/<name>.mp4, and reports camera drift vs. the source still.
"""
import glob, os, sys
import cv2
import numpy as np
import imageio_ffmpeg
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"

SRC = ROOT + ".playbot/anim-src/"
OUT = ROOT + "Assets/StreamingAssets/Video/"
STILLS = ROOT + "Assets/Art/Backgrounds/"
W, H = 1600, 896


def read(path):
    cap = cv2.VideoCapture(path)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24
    frames = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        frames.append(cv2.resize(f, (W, H), interpolation=cv2.INTER_AREA).astype(np.float32))
    return frames, fps


def main(names):
    os.makedirs(OUT, exist_ok=True)
    for path in sorted(glob.glob(SRC + "bg_*.mp4")):
        name = os.path.splitext(os.path.basename(path))[0]
        if names and name not in names:
            continue
        frames, fps = read(path)
        still = cv2.resize(cv2.imread(STILLS + name + ".png"), (W, H)).astype(np.float32)
        drift = [float(np.mean(np.abs(f[::8, ::8] - still[::8, ::8]))) for f in (frames[0], frames[len(frames) // 2], frames[-1])]
        fade = int(fps * 1.0)
        body = frames[:len(frames) - fade]
        tail = frames[len(frames) - fade:]
        for i in range(fade):
            w = (i + 1) / (fade + 1)
            body[i] = body[i] * w + tail[i] * (1 - w)
        writer = imageio_ffmpeg.write_frames(OUT + name + ".mp4", (W, H), fps=fps, codec="libx264", pix_fmt_out="yuv420p",
                                             output_params=["-crf", "27", "-preset", "slow", "-movflags", "+faststart", "-an"])
        writer.send(None)
        for f in body:
            writer.send(cv2.cvtColor(np.clip(f, 0, 255).astype(np.uint8), cv2.COLOR_BGR2RGB).tobytes())
        writer.close()
        size = os.path.getsize(OUT + name + ".mp4") / 1e6
        seam = float(np.mean(np.abs(body[-1][::8, ::8] - body[0][::8, ::8])))
        print(f"{name}: {len(body)} frames @ {fps:.0f}fps, {size:.2f} MB, drift vs still {[round(d, 1) for d in drift]}, loop seam {seam:.2f}")


if __name__ == "__main__":
    main(sys.argv[1:])
