#!/usr/bin/env python3
"""Music analysis + seamless-loop mastering for Lucky Number.

    python music_tools.py analyze <file> [...]
    python music_tools.py master            # build all tracks in MUSIC_JOBS

Loop method: estimate tempo, pick a loop of whole bars inside the steady part
of the track, fine-align the loop end so the material just before the loop
end matches the material just before the loop start (onset-envelope and
spectral correlation), then equal-power crossfade the pre-head into the tail.
The file therefore ends exactly where its head continues, so it loops without
a click or a rhythmic hiccup. Loudness is normalised by RMS (target dBFS) with
a soft-knee peak safety limiter.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

import mix_sfx as M

SR = M.SR
LAYERS = M.PROJECT_ROOT / ".playbot" / "audio-layers"
OUT = M.PROJECT_ROOT / "Assets" / "Audio" / "Music"

KRUMHANSL_MAJ = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
KRUMHANSL_MIN = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])
NOTES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]

HOP = 512


def onset_env(m: np.ndarray) -> np.ndarray:
    f, t, Z = signal.stft(m, SR, nperseg=2048, noverlap=2048 - HOP)
    S = np.log1p(100 * np.abs(Z))
    flux = np.maximum(np.diff(S, axis=1), 0).sum(axis=0)
    flux = flux - signal.medfilt(flux, 31)
    return np.maximum(flux, 0)


def tempo(m: np.ndarray, lo: float = 70, hi: float = 180) -> tuple[float, list]:
    oe = onset_env(m)
    oe = (oe - oe.mean()) / (oe.std() + 1e-9)
    ac = signal.correlate(oe, oe, mode="full")[len(oe) - 1:]
    fps = SR / HOP
    cands = []
    for bpm in np.arange(lo, hi, 0.25):
        lag = 60 * fps / bpm
        score = 0.0
        for k, w in ((1, 1.0), (2, 0.5), (4, 0.25)):
            L = lag * k
            i = int(L)
            if i + 1 < len(ac):
                frac = L - i
                score += w * (ac[i] * (1 - frac) + ac[i + 1] * frac)
        cands.append((score, bpm))
    cands.sort(reverse=True)
    return cands[0][1], [round(b, 2) for _, b in cands[:5]]


def chroma(m: np.ndarray) -> np.ndarray:
    f, t, Z = signal.stft(m, SR, nperseg=8192, noverlap=8192 - 2048)
    P = np.abs(Z) ** 2
    ok = (f > 55) & (f < 2000)
    pcs = (np.round(12 * np.log2(f[ok] / 261.6256)) % 12).astype(int)
    c = np.zeros(12)
    e = P[ok].sum(axis=1)
    for pc in range(12):
        c[pc] = e[pcs == pc].sum()
    return c / c.sum()


def key(m: np.ndarray) -> tuple[str, list]:
    c = chroma(m)
    res = []
    for tonic in range(12):
        for name, prof in (("major", KRUMHANSL_MAJ), ("minor", KRUMHANSL_MIN)):
            r = np.corrcoef(c, np.roll(prof, tonic))[0, 1]
            res.append((r, f"{NOTES[tonic]} {name}"))
    res.sort(reverse=True)
    return res[0][1], [(k, round(r, 3)) for r, k in res[:4]]


def analyze(path: Path) -> dict:
    x = M.load_audio(path)
    m = M.mono(x)
    n = len(m)
    seg = SR * 5
    rms5 = [round(M.db(math.sqrt(float(np.mean(m[i:i + seg] ** 2)))), 1) for i in range(0, n, seg)]
    bpm, cands = tempo(m[: min(n, SR * 60)])
    k, kc = key(m)
    return {
        "file": str(path.name), "dur_s": round(n / SR, 2), "ch": x.shape[1],
        "peak": round(M.db(np.abs(x).max()), 2), "rms": round(M.db(math.sqrt(float(np.mean(x ** 2)))), 2),
        "rms_per_5s": rms5, "bpm": bpm, "bpm_cands": cands, "key": k, "key_cands": kc,
    }


def frame_feats(m: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    f, t, Z = signal.stft(m, SR, nperseg=2048, noverlap=2048 - HOP)
    S = np.log1p(100 * np.abs(Z))
    oe = np.maximum(np.diff(S, axis=1), 0).sum(axis=0)
    return S[:, 1:], oe


def best_loop(x: np.ndarray, bpm: float, bars: int, start_min_s: float, end_max_s: float,
              xfade_s: float, beats_per_bar: int = 4) -> tuple[int, int, float]:
    """Return (a, b, score) sample indices for the loop [a, b)."""
    m = M.mono(x)
    S, oe = frame_feats(m)
    fps = SR / HOP
    L_nom = bars * beats_per_bar * 60.0 / bpm  # seconds
    W = int(max(xfade_s, 2.0) * fps)  # comparison window (frames) before a / before b
    best = (-1e9, 0, 0)
    a_lo = int(max(start_min_s, xfade_s + 0.5) * fps)
    a_hi = int((end_max_s - L_nom) * fps)
    if a_hi <= a_lo:
        raise ValueError("track too short for requested loop")
    Sn = S / (np.linalg.norm(S, axis=0, keepdims=True) + 1e-9)
    oen = (oe - oe.mean()) / (oe.std() + 1e-9)
    step = max(1, int(0.25 * fps))
    for a in range(a_lo, a_hi, step):
        b0 = a + int(L_nom * fps)
        for b in range(b0 - int(0.12 * fps), b0 + int(0.12 * fps) + 1):
            if b + 1 >= S.shape[1] or b - W < 0:
                continue
            spec = float(np.mean(np.sum(Sn[:, a - W:a] * Sn[:, b - W:b], axis=0)))
            rhy = float(np.mean(oen[a - W:a] * oen[b - W:b]))
            sc = spec + 0.15 * rhy
            if sc > best[0]:
                best = (sc, a, b)
    sc, a, b = best
    # refine to sample level around the frame match using waveform xcorr of
    # the 100 ms just before a vs before b (low-passed, mono)
    a_s, b_s = a * HOP, b * HOP
    lp = signal.butter(2, 2000, "lowpass", fs=SR, output="sos")
    ml = signal.sosfilt(lp, m)
    w = M.ms2s(150)
    ref = ml[a_s - w:a_s]
    search = M.ms2s(12)
    seg = ml[b_s - w - search:b_s + search]
    if len(ref) == w and len(seg) == w + 2 * search:
        cc = signal.correlate(seg, ref, mode="valid")
        b_s = b_s - search + int(np.argmax(cc))
    return a_s, b_s, sc


def make_seamless(x: np.ndarray, a: int, b: int, xfade_s: float) -> np.ndarray:
    X = int(xfade_s * SR)
    body = x[a:b].copy()
    head_pre = x[a - X:a]
    t = np.linspace(0, math.pi / 2, X)[:, None]
    body[-X:] = body[-X:] * np.cos(t) + head_pre * np.sin(t)
    return body


def loudness_match(x: np.ndarray, target_rms_db: float, ceiling_db: float = -1.0) -> np.ndarray:
    rms = math.sqrt(float(np.mean(x ** 2)))
    y = x * M.undb(target_rms_db) / rms
    pk = np.abs(y).max()
    ceil = M.undb(ceiling_db)
    if pk > ceil * 0.7:
        # soft-knee safety limiting, knee at 70% of ceiling
        k = 0.7
        y = y / ceil
        up = signal.resample_poly(y, 2, 1, axis=0)
        up = M.soft_clip(up, k)
        y = signal.resample_poly(up, 1, 2, axis=0) * ceil
        # re-match RMS once after limiting (limiter lowers it slightly)
        r2 = math.sqrt(float(np.mean(y ** 2)))
        y = y * min(M.undb(target_rms_db) / r2, ceil / np.abs(y).max())
    return y


def write_ogg(path: Path, y: np.ndarray, quality: float = 0.6) -> None:
    """Write OGG Vorbis in small blocks (one-shot writes overflow the stack
    inside libsndfile's Vorbis encoder on Windows for long files)."""
    y = np.clip(y, -1.0, 1.0).astype(np.float32)
    with sf.SoundFile(str(path), "w", SR, y.shape[1], format="OGG", subtype="VORBIS") as f:
        try:
            f._set_compression_level(1.0 - quality) if hasattr(f, "_set_compression_level") else None
        except Exception:  # noqa: BLE001
            pass
        for i in range(0, len(y), 4096):
            f.write(y[i:i + 4096])


def loop_report(y: np.ndarray) -> dict:
    jump = float(np.abs(y[0] - y[-1]).max())
    d = np.abs(np.diff(y, axis=0)).max(axis=1)
    return {"loop_jump": round(jump, 5), "p99_step": round(float(np.percentile(d, 99)), 5),
            "median_step": round(float(np.median(d)), 5)}


MUSIC_JOBS = {
    "music_title": {"src": "music/title_fantasy_raw.mp3", "loop": True, "bars": None, "xfade_s": 2.0,
                    "bpm": 90.0, "start_min_s": 3.0, "end_max_s": 103.0},
    "music_battle": {"src": "music/battle_fantasy_raw.mp3", "loop": True, "bars": None, "xfade_s": 1.5,
                     "bpm": 130.0, "start_min_s": 8.0, "end_max_s": 118.0},
    "music_final": {"src": "music/final_fantasy_raw.mp3", "loop": True, "bars": None, "xfade_s": 1.5,
                    "bpm": 140.0, "start_min_s": 4.0, "end_max_s": 118.0},
    # generator ignored "E-flat" requests; candidate _d is solidly D major, so
    # varispeed +1 semitone (about 6% faster) lands it in E-flat major
    "music_victory": {"src": "music/victory_fantasy_raw_d.mp3", "loop": False, "pitch_semitones": 1.0,
                      "fade_out_ms": 1200.0},
}


def master(name: str, job: dict, target_rms_db: float = -18.0) -> dict:
    x = M.load_audio(LAYERS / job["src"])
    if x.shape[1] == 1:
        x = np.repeat(x, 2, axis=1)
    x = M.apply_filters(x, {"hp_hz": 25})
    info = analyze(LAYERS / job["src"])
    if job.get("pitch_semitones"):
        x = M.varispeed(x, job["pitch_semitones"])
    bpm = job.get("bpm") or info["bpm"]
    rep: dict = {"name": name, "src": job["src"], "bpm_used": bpm, "key_est_src": info["key"],
                 "pitch_semitones": job.get("pitch_semitones", 0)}
    if job["loop"]:
        dur = len(x) / SR
        end_max = job.get("end_max_s", dur - 4.0)
        start_min = job.get("start_min_s", 4.0)
        bar_s = 4 * 60.0 / bpm
        bars = job.get("bars") or int((end_max - start_min - 1.0) / bar_s)
        # prefer multiples of 4 bars (phrase length)
        bars = max(4, bars - bars % 4)
        a, b, sc = best_loop(x, bpm, bars, start_min, end_max, job["xfade_s"])
        y = make_seamless(x, a, b, job["xfade_s"])
        rep.update({"loop_start_s": round(a / SR, 3), "loop_end_s": round(b / SR, 3), "bars": bars,
                    "match_score": round(sc, 3)})
    else:
        y = M.trim_silence(x, -60.0, 5.0)
        y = M.fade(y, 2.0, job.get("fade_out_ms", 1500.0))
    y = loudness_match(y, target_rms_db)
    OUT.mkdir(parents=True, exist_ok=True)
    out = OUT / f"{name}.ogg"
    write_ogg(out, y)
    # verify by reading the encoded file back
    z, _ = sf.read(str(out), always_2d=True)
    rep.update({"dur_s": round(len(z) / SR, 3), "peak_dbfs": round(M.db(np.abs(z).max()), 2),
                "rms_dbfs": round(M.db(math.sqrt(float(np.mean(z ** 2)))), 2), "loop": job["loop"]})
    if job["loop"]:
        rep.update(loop_report(z))
    return rep


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 1
    if argv[0] == "analyze":
        for f in argv[1:]:
            p = Path(f)
            if not p.is_absolute():
                p = LAYERS / f if (LAYERS / f).exists() else M.PROJECT_ROOT / f
            print(json.dumps(analyze(p)))
        return 0
    if argv[0] == "master":
        names = argv[1:] or list(MUSIC_JOBS)
        for n in names:
            print(json.dumps(master(n, MUSIC_JOBS[n])))
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
