#!/usr/bin/env python3
"""Layered SFX mixer for Lucky Number.

Builds each final sound effect from 2-4 generated layer files, the way a sound
designer would in a DAW: per-layer slicing, EQ, envelope and gain, transient
alignment, summing, gentle soft-clip limiting, silence trimming and peak
normalisation. Everything is driven by a JSON recipe file so sounds can be
remixed later without touching code.

Usage
-----
    python mix_sfx.py                       # mix every recipe
    python mix_sfx.py ui_click wheel_tick_1 # mix only these
    python mix_sfx.py --analyze-layers ui_click   # inspect raw layers
    python mix_sfx.py --list                # list recipes

Paths in the recipe are relative to the project root (two levels above this
file) unless absolute. Layer "file" paths are relative to "layers_root".

Recipe format (sfx_recipes.json)
--------------------------------
{
  "layers_root": ".playbot/audio-layers",
  "output_dir": "Assets/Audio/SFX",
  "defaults": { ...any per-sfx key below... },
  "sfx": {
    "<name>": {
      "channels": 1 | 2,            # output channel count (default 1)
      "peak_dbfs": -1.0,            # final peak level
      "length_ms": 300,             # optional hard max length (faded out)
      "fade_in_ms": 0.5, "fade_out_ms": 15,
      "trim_db": -50,               # silence threshold relative to peak
      "pre_roll_ms": 0,             # silence kept before first transient
      "limit_db": 3,                # gain pushed into the soft clipper
      "knee": 0.5,                  # soft clipper knee (linear, 0..1)
      "master": { "hp_hz":, "lp_hz":, "eq": [...] },
      "loop": {"mode": "wrap", "length_ms": 1600}             # or
      "loop": {"mode": "crossfade", "start_ms": 100, "length_ms": 1200,
               "crossfade_ms": 60},
      "layers": [
        {
          "file": "ui_click/click.mp3",
          "gain_db": 0,             # applied after the layer is peak-normalised
          "offset_ms": 0,           # where the anchor lands on the timeline
          "align": "onset",         # onset | start | peak | end
          "onset_index": 0,         # which detected onset is the anchor
          "onset_floor_db": -35,    # ignore onsets quieter than this (rel. peak)
          "trim_before_anchor_ms": 3,  # drop audio before anchor-N ms (default 3
                                       # for onset alignment; -1 disables)
          "slice": {"onset_index": 2, "pre_ms": 1, "length_ms": 80},
          "start_ms": 0, "end_ms": 500,   # explicit slice (alternative)
          "tail_ms": 900, "tail_fade_ms": 60,  # keep only the last N ms
          "reverse": false,
          "pitch_semitones": 0,     # varispeed (changes duration too)
          "saturate_db": 0,         # tanh drive before EQ (sub -> audible harmonics)
          "hp_hz": 200, "hp_order": 2, "lp_hz": 9000, "lp_order": 2,
          "eq": [{"type": "peak|lowshelf|highshelf", "hz": 3000,
                  "db": -4, "q": 1.0}],
          "fade_in_ms": 0.5, "fade_out_ms": 10, "length_ms": 120,
          "decay": {"hold_ms": 15, "tau_ms": 20},  # exponential tail tamer
          "normalize": true,
          "pan": 0.0                # -1 left .. +1 right (stereo output)
        }
      ]
    }
  }
}
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from fractions import Fraction
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

SR = 44100
TOOLS_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = TOOLS_DIR.parent.parent
DEFAULT_RECIPES = TOOLS_DIR / "sfx_recipes.json"
DEBUG = False


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------
def db(x: float) -> float:
    return 20.0 * math.log10(max(float(x), 1e-12))


def undb(d: float) -> float:
    return 10.0 ** (d / 20.0)


def ms2s(ms: float) -> int:
    return int(round(ms * SR / 1000.0))


def resolve(p: str | Path, base: Path = PROJECT_ROOT) -> Path:
    p = Path(p)
    return p if p.is_absolute() else (base / p)


def load_audio(path: Path) -> np.ndarray:
    """Load any soundfile-readable file as float64 (n, ch) at 44.1 kHz."""
    data, sr = sf.read(str(path), dtype="float64", always_2d=True)
    if sr != SR:
        frac = Fraction(SR, sr).limit_denominator(1000)
        data = signal.resample_poly(data, frac.numerator, frac.denominator, axis=0)
    return data


def to_channels(x: np.ndarray, ch: int) -> np.ndarray:
    if x.shape[1] == ch:
        return x
    if ch == 1:
        return x.mean(axis=1, keepdims=True)
    return np.repeat(x[:, :1], ch, axis=1) if x.shape[1] == 1 else x[:, :ch]


def mono(x: np.ndarray) -> np.ndarray:
    return x.mean(axis=1) if x.ndim == 2 else x


def varispeed(x: np.ndarray, semitones: float) -> np.ndarray:
    if not semitones:
        return x
    ratio = 2.0 ** (-semitones / 12.0)  # output length factor
    frac = Fraction(ratio).limit_denominator(400)
    return signal.resample_poly(x, frac.numerator, frac.denominator, axis=0)


def biquad_sos(kind: str, hz: float, gain_db: float, q: float) -> np.ndarray:
    """RBJ cookbook biquad as a single SOS row."""
    a_lin = 10 ** (gain_db / 40.0)
    w0 = 2 * math.pi * hz / SR
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2 * q)
    if kind == "peak":
        b = [1 + alpha * a_lin, -2 * cw, 1 - alpha * a_lin]
        a = [1 + alpha / a_lin, -2 * cw, 1 - alpha / a_lin]
    elif kind in ("lowshelf", "highshelf"):
        sq = 2 * math.sqrt(a_lin) * alpha
        if kind == "lowshelf":
            b = [a_lin * ((a_lin + 1) - (a_lin - 1) * cw + sq),
                 2 * a_lin * ((a_lin - 1) - (a_lin + 1) * cw),
                 a_lin * ((a_lin + 1) - (a_lin - 1) * cw - sq)]
            a = [(a_lin + 1) + (a_lin - 1) * cw + sq,
                 -2 * ((a_lin - 1) + (a_lin + 1) * cw),
                 (a_lin + 1) + (a_lin - 1) * cw - sq]
        else:
            b = [a_lin * ((a_lin + 1) + (a_lin - 1) * cw + sq),
                 -2 * a_lin * ((a_lin - 1) + (a_lin + 1) * cw),
                 a_lin * ((a_lin + 1) + (a_lin - 1) * cw - sq)]
            a = [(a_lin + 1) - (a_lin - 1) * cw + sq,
                 2 * ((a_lin - 1) - (a_lin + 1) * cw),
                 (a_lin + 1) - (a_lin - 1) * cw - sq]
    else:
        raise ValueError(f"unknown eq type {kind}")
    b = np.array(b) / a[0]
    a = np.array(a) / a[0]
    return np.array([[b[0], b[1], b[2], 1.0, a[1], a[2]]])


def apply_filters(x: np.ndarray, spec: dict) -> np.ndarray:
    sos_list = []
    if spec.get("hp_hz"):
        sos_list.append(signal.butter(int(spec.get("hp_order", 2)), spec["hp_hz"],
                                      "highpass", fs=SR, output="sos"))
    if spec.get("lp_hz"):
        sos_list.append(signal.butter(int(spec.get("lp_order", 2)), min(spec["lp_hz"], SR / 2 - 100),
                                      "lowpass", fs=SR, output="sos"))
    for band in spec.get("eq", []) or []:
        sos_list.append(biquad_sos(band["type"], band["hz"], band.get("db", 0.0), band.get("q", 0.707)))
    if not sos_list:
        return x
    sos = np.vstack(sos_list)
    return signal.sosfilt(sos, x, axis=0)


def fade(x: np.ndarray, fade_in_ms: float = 0.0, fade_out_ms: float = 0.0) -> np.ndarray:
    x = x.copy()
    n = len(x)
    fi = min(ms2s(fade_in_ms), n)
    fo = min(ms2s(fade_out_ms), n)
    if fi > 1:
        x[:fi] *= (np.sin(np.linspace(0, math.pi / 2, fi)) ** 2)[:, None]
    if fo > 1:
        x[n - fo:] *= (np.cos(np.linspace(0, math.pi / 2, fo)) ** 2)[:, None]
    return x


def envelope_db(x: np.ndarray, win: int = 64) -> np.ndarray:
    """Smoothed per-sample amplitude envelope in dBFS (mono)."""
    m = np.abs(mono(x))
    if win > 1:
        m = signal.lfilter(np.ones(win) / win, [1.0], m)
        m = np.roll(m, -win // 2)
    return 20 * np.log10(np.maximum(m, 1e-9))


def detect_onsets(x: np.ndarray, rise_db: float = 10.0, floor_rel_db: float = -35.0,
                  min_gap_ms: float = 25.0) -> list[int]:
    """Energy-rise onset detector with sample-accurate refinement.

    Frames of 256 samples (hop 64). An onset is where the frame energy rises
    more than `rise_db` above the minimum of the previous ~15 ms and is above
    `floor_rel_db` relative to the loudest frame. The sample position is then
    refined to the first sample reaching 15% of the local peak.
    """
    m = mono(x)
    if len(m) < 512:
        m = np.pad(m, (0, 512 - len(m)))
    hop, frame = 64, 256
    # emphasise transients a little (first difference ~ +6 dB/oct)
    emph = np.concatenate([[0.0], np.diff(m)]) * 0.5 + m * 0.5
    nfr = 1 + (len(emph) - frame) // hop
    idx = np.arange(frame)[None, :] + hop * np.arange(nfr)[:, None]
    e = 10 * np.log10(np.mean(emph[idx] ** 2, axis=1) + 1e-12)
    peak = e.max()
    look = 10
    onsets: list[int] = []
    last = -10 ** 9
    gap = ms2s(min_gap_ms)
    for i in range(nfr):
        if e[i] < peak + floor_rel_db:
            continue
        prev_min = e[max(0, i - look):i].min() if i > 0 else -120.0
        if e[i] - prev_min >= rise_db:
            pos = i * hop
            if pos - last < gap:
                continue
            # refine: local peak in next 15 ms, first sample above 15% of it
            seg = np.abs(m[max(0, pos - frame):pos + frame + ms2s(15)])
            base = max(0, pos - frame)
            if len(seg) == 0:
                continue
            lp = seg.max()
            hit = np.nonzero(seg >= 0.15 * lp)[0]
            s = base + (int(hit[0]) if len(hit) else 0)
            if s - last < gap:
                continue
            onsets.append(s)
            last = s
    if not onsets:
        onsets = [first_above(x, -30.0)]
    return onsets


def first_above(x: np.ndarray, rel_db: float) -> int:
    a = np.abs(mono(x))
    pk = a.max() if len(a) else 0.0
    if pk <= 0:
        return 0
    hit = np.nonzero(a >= pk * undb(rel_db))[0]
    return int(hit[0]) if len(hit) else 0


def last_above(x: np.ndarray, rel_db: float) -> int:
    env = envelope_db(x, 256)
    pk = env.max()
    hit = np.nonzero(env >= pk + rel_db)[0]
    return int(hit[-1]) + 1 if len(hit) else len(x)


def soft_clip(x: np.ndarray, knee: float) -> np.ndarray:
    """Linear below `knee`, tanh-rounded above, asymptote at 1.0."""
    y = x.copy()
    a = np.abs(y)
    over = a > knee
    span = 1.0 - knee
    y[over] = np.sign(y[over]) * (knee + span * np.tanh((a[over] - knee) / span))
    return y


def limit(x: np.ndarray, push_db: float, knee: float) -> np.ndarray:
    pk = np.abs(x).max()
    if pk <= 0:
        return x
    x = x / pk * undb(push_db)
    # 2x oversampled soft clip to keep aliasing down
    up = signal.resample_poly(x, 2, 1, axis=0)
    up = soft_clip(up, knee)
    return signal.resample_poly(up, 1, 2, axis=0)


def pan_gains(p: float) -> tuple[float, float]:
    ang = (max(-1.0, min(1.0, p)) + 1) * math.pi / 4
    return math.cos(ang) * math.sqrt(2), math.sin(ang) * math.sqrt(2)


# --------------------------------------------------------------------------
# layer + mix
# --------------------------------------------------------------------------
def render_layer(spec: dict, layers_root: Path, ch: int) -> tuple[np.ndarray, int]:
    """Return (audio, anchor_sample)."""
    x = load_audio(resolve(spec["file"], layers_root))
    x = to_channels(x, ch)
    if spec.get("reverse"):
        x = x[::-1].copy()
    x = varispeed(x, spec.get("pitch_semitones", 0.0))

    sl = spec.get("slice")
    if sl:
        ons = detect_onsets(x, sl.get("rise_db", 10.0), sl.get("floor_rel_db", sl.get("onset_floor_db", -35.0)),
                            sl.get("min_gap_ms", 25.0))
        k = min(int(sl.get("onset_index", 0)), len(ons) - 1)
        s0 = max(0, ons[k] - ms2s(sl.get("pre_ms", 1.0)))
        s1 = s0 + ms2s(sl.get("length_ms", 100.0)) if sl.get("length_ms") else len(x)
        x = x[s0:s1]
        x = fade(x, sl.get("fade_in_ms", 0.3), sl.get("fade_out_ms", 8.0))
    if spec.get("tail_ms"):
        # keep only the last N ms (handy for reversed swells), faded in
        x = x[max(0, len(x) - ms2s(spec["tail_ms"])):]
        x = fade(x, spec.get("tail_fade_ms", 60.0), 0.0)
    if "start_ms" in spec or "end_ms" in spec:
        s0 = ms2s(spec.get("start_ms", 0.0))
        s1 = ms2s(spec["end_ms"]) if "end_ms" in spec else len(x)
        x = fade(x[s0:s1], 0.3, 0.0)

    if spec.get("saturate_db"):
        # tanh harmonic saturation: adds upper harmonics so sub content is
        # still perceived on small speakers (filters below tame the top end)
        pk = np.abs(x).max()
        if pk > 0:
            g = undb(spec["saturate_db"])
            x = np.tanh(g * x / pk) / math.tanh(g) * pk
    x = apply_filters(x, spec)

    if spec.get("length_ms"):
        x = x[:ms2s(spec["length_ms"])]
    dec = spec.get("decay")
    if dec:
        t = np.arange(len(x))
        hold = ms2s(dec.get("hold_ms", 0.0))
        start = first_above(x, -30.0) + hold
        tau = max(ms2s(dec.get("tau_ms", 20.0)), 1)
        g = np.ones(len(x))
        g[start:] = np.exp(-(t[start:] - start) / tau)
        x = x * g[:, None]
    x = fade(x, spec.get("fade_in_ms", 0.0), spec.get("fade_out_ms", 5.0))

    if spec.get("normalize", True):
        pk = np.abs(x).max()
        if pk > 0:
            x = x / pk
    x = x * undb(spec.get("gain_db", 0.0))
    if ch == 2 and spec.get("pan"):
        gl, gr = pan_gains(spec["pan"])
        x = x * np.array([gl, gr])[None, :]

    align = spec.get("align", "onset")
    if align == "start":
        anchor = 0
    elif align == "peak":
        anchor = int(np.argmax(np.abs(mono(x))))
    elif align == "end":
        anchor = last_above(x, -45.0)
    else:
        ons = detect_onsets(x, floor_rel_db=spec.get("onset_floor_db", -35.0))
        anchor = ons[min(int(spec.get("onset_index", 0)), len(ons) - 1)]
    # onset-aligned layers drop their pre-onset lead-in by default (3 ms margin)
    tb = spec.get("trim_before_anchor_ms", 3.0 if align == "onset" else None)
    if tb is not None and tb >= 0:
        s0 = max(0, anchor - ms2s(tb))
        x = fade(x[s0:], 0.5, 0.0)
        anchor -= s0
    return x, anchor


def trim_silence(x: np.ndarray, rel_db: float, pre_roll_ms: float) -> np.ndarray:
    a = np.abs(mono(x))
    pk = a.max()
    if pk <= 0:
        return x
    thr = pk * undb(rel_db)
    hit = np.nonzero(a >= thr)[0]
    if not len(hit):
        return x
    start = max(0, int(hit[0]) - ms2s(pre_roll_ms))
    end = last_above(x, rel_db)
    return x[start:max(end, start + 1)]


def make_loop(x: np.ndarray, spec: dict) -> np.ndarray:
    mode = spec.get("mode", "wrap")
    if mode == "wrap":
        L = ms2s(spec["length_ms"])
        out = np.zeros((L, x.shape[1]))
        for s in range(0, len(x), L):
            chunk = x[s:s + L]
            out[:len(chunk)] += chunk
        return out
    if mode == "crossfade":
        s0 = ms2s(spec.get("start_ms", 0.0))
        L = ms2s(spec["length_ms"])
        X = ms2s(spec.get("crossfade_ms", 50.0))
        need = s0 + L + X
        if len(x) < need:
            x = np.pad(x, ((0, need - len(x)), (0, 0)))
        body = x[s0 + X:s0 + X + L].copy()
        head = x[s0:s0 + X]
        tail = body[L - X:]
        t = np.linspace(0, math.pi / 2, X)[:, None]
        body[L - X:] = tail * np.cos(t) + head * np.sin(t)
        return body
    raise ValueError(f"unknown loop mode {mode}")


def mix_one(name: str, recipe: dict, defaults: dict, layers_root: Path, out_dir: Path) -> dict:
    r = {**defaults, **recipe}
    ch = int(r.get("channels", 1))
    rendered = [render_layer(l, layers_root, ch) for l in r["layers"]]
    starts = [ms2s(l.get("offset_ms", 0.0)) - anc for l, (_, anc) in zip(r["layers"], rendered)]
    base = min(starts)
    if DEBUG:
        for l, s, (a, anc) in zip(r["layers"], starts, rendered):
            print(f"   [{name}] {l['file']:42s} anchor={1000*anc/SR:7.1f}ms start={1000*(s-base)/SR:7.1f}ms "
                  f"len={1000*len(a)/SR:7.1f}ms")
    total = max(s - base + len(a) for s, (a, _) in zip(starts, rendered))
    mix = np.zeros((total, ch))
    for s, (a, _) in zip(starts, rendered):
        p = s - base
        mix[p:p + len(a)] += a

    mix = apply_filters(mix, r.get("master", {}))
    loop = r.get("loop")
    if loop:
        mix = make_loop(mix, loop)
    else:
        mix = trim_silence(mix, r.get("trim_db", -50.0), r.get("pre_roll_ms", 0.0))
        if r.get("length_ms"):
            mix = mix[:ms2s(r["length_ms"])]
    mix = limit(mix, r.get("limit_db", 3.0), r.get("knee", 0.5))
    if not loop:
        mix = fade(mix, r.get("fade_in_ms", 0.5), r.get("fade_out_ms", 15.0))
    pk = np.abs(mix).max()
    mix = mix / pk * undb(r.get("peak_dbfs", -1.0))
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{name}.wav"
    sf.write(str(out_path), mix.astype(np.float32), SR, subtype="PCM_16")
    return analyze(name, mix, loop is not None)


# --------------------------------------------------------------------------
# analysis
# --------------------------------------------------------------------------
def analyze(name: str, x: np.ndarray, is_loop: bool = False) -> dict:
    m = mono(x)
    n = len(m)
    peak = np.abs(x).max()
    rms = math.sqrt(float(np.mean(x ** 2))) if n else 0.0
    # loudest 50 ms window RMS (perceived punch)
    w = ms2s(50)
    if n > w:
        c = np.cumsum(np.concatenate([[0.0], m ** 2]))
        win_rms = math.sqrt(max(float(np.max(c[w:] - c[:-w]) / w), 0.0))
    else:
        win_rms = rms
    if n > 16:
        freqs, spec = signal.welch(m, SR, nperseg=min(2048, n), noverlap=None)
    else:
        freqs, spec = np.zeros(2), np.zeros(2)
    tot = spec.sum() + 1e-20
    centroid = float((spec * freqs).sum() / tot)
    hf = float(spec[freqs > 9000].sum() / tot)
    lf = float(spec[freqs < 150].sum() / tot)
    ons = detect_onsets(x) if n else [0]
    res = {
        "name": name,
        "dur_ms": round(1000 * n / SR, 1),
        "peak_dbfs": round(db(peak), 2),
        "rms_dbfs": round(db(rms), 2),
        "max50ms_rms_dbfs": round(db(win_rms), 2),
        "onset_ms": round(1000 * ons[0] / SR, 2),
        "n_onsets": len(ons),
        "centroid_hz": round(centroid),
        "hf_above_9k": round(hf, 3),
        "lf_below_150": round(lf, 3),
        "loop": is_loop,
    }
    if is_loop and n > 2:
        res["loop_jump"] = round(float(np.abs(x[0] - x[-1]).max()), 4)
        res["loop_typical_step"] = round(float(np.percentile(np.abs(np.diff(m)), 99)), 4)
    return res


def flatness(x: np.ndarray) -> float:
    m = mono(x)
    if len(m) < 2048:
        m = np.pad(m, (0, 2048 - len(m)))
    f, t, Z = signal.stft(m, SR, nperseg=1024)
    p = np.abs(Z) ** 2 + 1e-12
    e = p.sum(axis=0)
    loud = e > e.max() * 0.05
    fl = np.exp(np.mean(np.log(p), axis=0)) / np.mean(p, axis=0)
    return float(np.mean(fl[loud])) if loud.any() else 0.0


def analyze_layers(names: list[str], layers_root: Path) -> None:
    dirs = [layers_root / n for n in names] if names else sorted(p for p in layers_root.iterdir() if p.is_dir())
    for d in dirs:
        for f in sorted(d.glob("*.*")):
            if f.suffix.lower() not in (".mp3", ".wav", ".ogg", ".flac"):
                continue
            try:
                x = load_audio(f)
            except Exception as exc:  # noqa: BLE001
                print(f"{d.name}/{f.name}: LOAD FAIL {exc}")
                continue
            a = analyze(f.name, x)
            ons = detect_onsets(x)
            ons_ms = [round(1000 * o / SR) for o in ons[:12]]
            act = trim_silence(x, -40.0, 0)
            print(f"{d.name}/{f.name}: dur={a['dur_ms']:.0f}ms active={1000*len(act)/SR:.0f}ms "
                  f"peak={a['peak_dbfs']:.1f} rms={a['rms_dbfs']:.1f} cen={a['centroid_hz']} "
                  f"hf={a['hf_above_9k']:.2f} lf={a['lf_below_150']:.2f} flat={flatness(x):.2f} "
                  f"ch={x.shape[1]} onsets={ons_ms}{'...' if len(ons) > 12 else ''}")


# --------------------------------------------------------------------------
def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("names", nargs="*", help="recipe names to mix (default: all)")
    ap.add_argument("--recipes", default=str(DEFAULT_RECIPES))
    ap.add_argument("--analyze-layers", action="store_true", help="print stats for raw layers")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--report", default=None, help="write analysis JSON here")
    ap.add_argument("--debug", action="store_true", help="print per-layer anchors")
    args = ap.parse_args(argv)
    global DEBUG
    DEBUG = args.debug

    cfg = json.loads(Path(args.recipes).read_text(encoding="utf-8"))
    layers_root = resolve(cfg.get("layers_root", ".playbot/audio-layers"))
    out_dir = resolve(cfg.get("output_dir", "Assets/Audio/SFX"))
    if args.analyze_layers:
        analyze_layers(args.names, layers_root)
        return 0
    recipes = {k: v for k, v in cfg["sfx"].items() if not k.startswith("_")}
    if args.list:
        print("\n".join(recipes))
        return 0
    names = args.names or list(recipes)
    results = []
    for n in names:
        if n not in recipes:
            print(f"unknown recipe {n}", file=sys.stderr)
            return 2
        res = mix_one(n, recipes[n], cfg.get("defaults", {}), layers_root, out_dir)
        results.append(res)
        extra = f" loop_jump={res['loop_jump']} (p99 step {res['loop_typical_step']})" if res["loop"] else ""
        print(f"{n:20s} dur={res['dur_ms']:7.1f}ms peak={res['peak_dbfs']:6.2f} rms={res['rms_dbfs']:6.2f} "
              f"max50={res['max50ms_rms_dbfs']:6.2f} onset={res['onset_ms']:6.2f}ms cen={res['centroid_hz']:5d} "
              f"hf9k={res['hf_above_9k']:.3f} lf150={res['lf_below_150']:.3f}{extra}")
    if args.report:
        Path(args.report).write_text(json.dumps(results, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
