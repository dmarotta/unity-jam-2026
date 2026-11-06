#!/usr/bin/env python3
"""Detailed numeric inspection of audio files (envelope + band balance).

    python inspect_audio.py file1 [file2 ...] [--step 20]
Paths may be relative to .playbot/audio-layers or the project root.
"""
import argparse
import math
from pathlib import Path

import numpy as np
from scipy import signal

import mix_sfx as M

BANDS = [(20, 100), (100, 400), (400, 2000), (2000, 6000), (6000, 12000), (12000, 22000)]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--step", type=float, default=20.0, help="envelope step in ms")
    ap.add_argument("--max", type=int, default=60, help="max envelope points")
    a = ap.parse_args()
    root = M.PROJECT_ROOT / ".playbot" / "audio-layers"
    for f in a.files:
        p = Path(f)
        if not p.is_absolute():
            p = root / f if (root / f).exists() else M.PROJECT_ROOT / f
        x = M.load_audio(p)
        m = M.mono(x)
        st = M.ms2s(a.step)
        env = [M.db(math.sqrt(float(np.mean(m[i:i + st] ** 2)) + 1e-12)) for i in range(0, len(m), st)]
        fr, ps = signal.welch(m, M.SR, nperseg=min(4096, len(m)))
        tot = ps.sum() + 1e-20
        bands = " ".join(f"{lo}-{hi}:{100 * ps[(fr >= lo) & (fr < hi)].sum() / tot:.0f}%" for lo, hi in BANDS)
        side = ""
        if x.shape[1] == 2:
            s = (x[:, 0] - x[:, 1]) / 2
            side = f" side/mid={M.db(np.sqrt(np.mean(s**2))+1e-12) - M.db(np.sqrt(np.mean(m**2))+1e-12):.1f}dB"
        print(f"== {f}  dur={1000*len(m)/M.SR:.0f}ms peak={M.db(np.abs(x).max()):.1f} dc={m.mean():.4f}{side}")
        print(f"   bands {bands}")
        print("   env(dB/{:.0f}ms): ".format(a.step) + " ".join(f"{e:.0f}" for e in env[:a.max]))


if __name__ == "__main__":
    main()
