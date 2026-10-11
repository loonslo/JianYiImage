"""Synthesize the promo soundtrack - original music plus UI sound effects - in time with the cue sheet.

    py -3.14 tools/promo-video/music.py --cues <cues.json> --out <music.wav> [--stems]

128 BPM in C major, 16 bars = 30 s: a quiet intro under the hook, an impact for the logo, a house groove under the four
highlights, a build through the montage, a plagal cadence (F -> C) under the end card. Clicks, key taps, a chime on
every "copied" and a whoosh on every caption change come from the cue sheet written by render.py --cues, so they land on
the frames. Everything is generated here with numpy/scipy: no samples, no third-party music.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy import signal

SR = 48000
RNG = np.random.default_rng(20261011)
NOTE = {n: 440.0 * 2 ** ((i - 9) / 12) for i, n in enumerate(["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"])}


def hz(name: str) -> float:
    return NOTE[name[:-1]] * 2 ** (int(name[-1]) - 4)


CHORDS = {
    "Fmaj7": ["F3", "A3", "C4", "E4"], "G6": ["G3", "B3", "D4", "E4"], "Em7": ["E3", "G3", "B3", "D4"], "Am7": ["A3", "C4", "E4", "G4"],
    "Cmaj9": ["C3", "G3", "B3", "D4", "E4"], "Fmaj9": ["F3", "C4", "E4", "G4", "A4"], "Gsus": ["G3", "C4", "D4", "F4"],
}
ROOT = {"Fmaj7": "F2", "G6": "G2", "Em7": "E2", "Am7": "A2", "Cmaj9": "C2", "Fmaj9": "F2", "Gsus": "G2"}
# one chord per bar
BARS = ["Fmaj7", "G6", "Cmaj9", "Fmaj7", "G6", "Em7", "Am7", "Fmaj7", "G6", "Em7", "Am7", "Fmaj7", "G6", "Gsus", "Fmaj9", "Cmaj9"]


# ------------------------------------------------------------------ building blocks

def t_axis(seconds: float) -> np.ndarray:
    return np.arange(int(SR * seconds)) / SR


def sos(kind: str, freq, order: int = 2):
    return signal.butter(order, freq, btype=kind, fs=SR, output="sos")


def filt(x: np.ndarray, kind: str, freq, order: int = 2) -> np.ndarray:
    return signal.sosfilt(sos(kind, freq, order), x, axis=0)


def biquad(kind: str, f0: float, gain_db: float, q: float = 0.707):
    """RBJ cookbook shelf/peak filter as (b, a)."""
    a_ = 10 ** (gain_db / 40)
    w = 2 * np.pi * f0 / SR
    cw, alpha = np.cos(w), np.sin(w) / (2 * q)
    if kind == "peak":
        b = [1 + alpha * a_, -2 * cw, 1 - alpha * a_]
        a = [1 + alpha / a_, -2 * cw, 1 - alpha / a_]
    else:
        sq = 2 * np.sqrt(a_) * alpha
        if kind == "lowshelf":
            b = [a_ * ((a_ + 1) - (a_ - 1) * cw + sq), 2 * a_ * ((a_ - 1) - (a_ + 1) * cw), a_ * ((a_ + 1) - (a_ - 1) * cw - sq)]
            a = [(a_ + 1) + (a_ - 1) * cw + sq, -2 * ((a_ - 1) + (a_ + 1) * cw), (a_ + 1) + (a_ - 1) * cw - sq]
        else:
            b = [a_ * ((a_ + 1) + (a_ - 1) * cw + sq), -2 * a_ * ((a_ - 1) + (a_ + 1) * cw), a_ * ((a_ + 1) + (a_ - 1) * cw - sq)]
            a = [(a_ + 1) - (a_ - 1) * cw + sq, 2 * ((a_ - 1) - (a_ + 1) * cw), (a_ + 1) - (a_ - 1) * cw - sq]
    return np.array(b) / a[0], np.array(a) / a[0]


def eq(x: np.ndarray, *bands) -> np.ndarray:
    for kind, f0, gain_db, *q in bands:
        b, a = biquad(kind, f0, gain_db, *(q or [0.707]))
        x = signal.lfilter(b, a, x, axis=0)
    return x


def noise(n: int) -> np.ndarray:
    return RNG.standard_normal(n)


def saw(freq: float, n: int, phase: float | None = None) -> np.ndarray:
    """Band-limited sawtooth (polyBLEP)."""
    dt = freq / SR
    p = ((RNG.random() if phase is None else phase) + np.arange(n) * dt) % 1.0
    y = 2 * p - 1
    m = p < dt
    x = p[m] / dt
    y[m] -= x + x - x * x - 1
    m = p > 1 - dt
    x = (p[m] - 1) / dt
    y[m] -= x * x + x + x + 1
    return y


def env_ar(n: int, attack: float, release: float, hold: float | None = None) -> np.ndarray:
    t = np.arange(n) / SR
    e = np.minimum(1.0, t / max(attack, 1e-4))
    if hold is not None:
        e *= np.where(t < hold, 1.0, np.exp(-(t - hold) / max(release, 1e-4)))
    else:
        e *= np.exp(-t / max(release, 1e-4))
    return e


def stft_sweep(seconds: float, centers, widths, gains) -> np.ndarray:
    """Noise whose spectrum is a moving band: centers/widths/gains are callables of u in 0..1 (u = time / seconds)."""
    n = int(SR * seconds)
    f, tt, z = signal.stft(noise(n + 2048), fs=SR, nperseg=1024)
    u = np.clip(tt / seconds, 0, 1)
    c, w, g = centers(u), widths(u), gains(u)
    mask = np.exp(-0.5 * ((np.log2(np.maximum(f[:, None], 20)) - np.log2(c[None, :])) / w[None, :]) ** 2) * g[None, :]
    _, y = signal.istft(z * mask, fs=SR, nperseg=1024)
    y = y[:n]
    return y / (np.max(np.abs(y)) + 1e-9)


class Bus:
    """A stereo buffer that sounds are dropped into at a time with a gain and a pan (-1 left .. 1 right)."""

    def __init__(self, seconds: float):
        self.x = np.zeros((int(SR * seconds), 2))

    def add(self, sound: np.ndarray, at: float, gain: float = 1.0, pan: float = 0.0) -> None:
        start = int(round(at * SR))
        if start >= len(self.x):
            return
        if sound.ndim == 1:
            a = (pan + 1) * np.pi / 4
            sound = np.stack([sound * np.cos(a), sound * np.sin(a)], axis=1) * np.sqrt(2)
        end = min(len(self.x), start + len(sound))
        if start < 0:
            sound, start = sound[-start:], 0
        self.x[start:end] += gain * sound[: end - start]


def reverb_ir(seconds: float = 2.4, rt60: float = 1.7, damp: float = 4500) -> np.ndarray:
    n = int(SR * seconds)
    t = np.arange(n) / SR
    ir = noise(2 * n).reshape(n, 2) * (10 ** (-3 * t / rt60))[:, None]
    ir = filt(ir, "low", damp)
    ir = np.vstack([np.zeros((int(0.018 * SR), 2)), ir])
    return ir / np.sqrt((ir ** 2).sum(axis=0, keepdims=True))


def convolve(x: np.ndarray, ir: np.ndarray) -> np.ndarray:
    return np.stack([signal.fftconvolve(x[:, c], ir[:, c])[: len(x)] for c in range(2)], axis=1)


# ------------------------------------------------------------------ instruments

def kick() -> np.ndarray:
    t = t_axis(0.42)
    f = 52 + 115 * np.exp(-t / 0.03)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.17) * np.minimum(1, t / 0.0015)
    click = filt(noise(len(t)), "band", [1800, 6000]) * np.exp(-t / 0.005) * 0.45
    return np.tanh(1.6 * (body + click)) / np.tanh(1.6)


def clap() -> np.ndarray:
    t = t_axis(0.38)
    e = sum(np.exp(-np.maximum(t - d, 0) / 0.0065) * (t >= d) for d in (0, 0.010, 0.021))
    e = e + 0.55 * np.exp(-np.maximum(t - 0.03, 0) / 0.12) * (t >= 0.03)
    y = filt(noise(len(t)), "band", [900, 2600]) * e
    return y / np.max(np.abs(y))


def hat(open_: bool = False) -> np.ndarray:
    t = t_axis(0.3 if open_ else 0.08)
    y = filt(noise(len(t)), "high", 7500) * np.exp(-t / (0.14 if open_ else 0.026))
    return y / np.max(np.abs(y))


def shaker() -> np.ndarray:
    t = t_axis(0.09)
    y = filt(noise(len(t)), "band", [4500, 10000]) * np.minimum(1, t / 0.006) * np.exp(-t / 0.035)
    return y / np.max(np.abs(y))


def bass(freq: float, seconds: float) -> np.ndarray:
    t = t_axis(seconds + 0.06)
    y = 0.45 * np.sin(2 * np.pi * freq * t) + 0.3 * np.sin(4 * np.pi * freq * t) + 0.5 * filt(saw(freq, len(t)), "low", 1100)
    e = np.minimum(1, t / 0.004) * np.where(t < seconds, 0.75 + 0.25 * np.exp(-t / 0.07), 0.75 * np.exp(-(t - seconds) / 0.03))
    return np.tanh(1.3 * y * e) / np.tanh(1.3)


def pad(notes: list[str], seconds: float, bright: float = 1700) -> np.ndarray:
    n = int(SR * (seconds + 0.6))
    out = np.zeros((n, 2))
    for name in notes:
        f = hz(name)
        for c, cents in enumerate(((-9, 4), (-4, 9))):
            v = sum(saw(f * 2 ** (ct / 1200), n) for ct in cents)
            out[:, c] += v
    out = filt(out, "low", bright)
    t = np.arange(n) / SR
    e = np.minimum(1, t / 0.12) * np.where(t < seconds, 1.0, np.exp(-(t - seconds) / 0.25))
    return out * e[:, None] / (len(notes) * 2.2)


def stab(notes: list[str], seconds: float = 0.24) -> np.ndarray:
    t = t_axis(seconds)
    y = np.zeros(len(t))
    for name in notes:
        f = hz(name) * 2
        y += saw(f, len(t)) + 0.5 * saw(f * 1.004, len(t))
    y = filt(y, "low", 2600) * np.minimum(1, t / 0.003) * np.exp(-t / 0.11)
    return y / (len(notes) * 1.5)


def pluck(freq: float, seconds: float = 0.32) -> np.ndarray:
    t = t_axis(seconds)
    y = saw(freq, len(t)) * np.exp(-t / 0.05) + 0.6 * np.sin(2 * np.pi * freq * t)
    y = filt(y, "low", 3200) * np.minimum(1, t / 0.002) * np.exp(-t / 0.12)
    return y


def bell(freq: float, seconds: float = 1.4) -> np.ndarray:
    t = t_axis(seconds)
    index = 2.2 * np.exp(-t / 0.25) + 0.3
    y = np.sin(2 * np.pi * freq * t + index * np.sin(2 * np.pi * freq * 3.5 * t))
    return y * np.minimum(1, t / 0.002) * np.exp(-t / 0.45)


def riser(seconds: float) -> np.ndarray:
    y = stft_sweep(seconds, lambda u: 300 * (24 ** u), lambda u: 0.55 + 0 * u, lambda u: u ** 2.2)
    t = t_axis(seconds)
    tone = np.sin(2 * np.pi * np.cumsum(220 * (4 ** (t / seconds))) / SR) * (t / seconds) ** 2 * 0.25
    return y + tone


def whoosh(seconds: float = 0.42) -> np.ndarray:
    return stft_sweep(seconds, lambda u: 700 * (5 ** np.sin(np.pi * u * 0.9)), lambda u: 0.7 + 0 * u,
                      lambda u: np.sin(np.pi * np.clip(u, 0, 1)) ** 1.5)


def impact() -> np.ndarray:
    t = t_axis(1.6)
    f = 34 + 30 * np.exp(-t / 0.12)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.55) * np.minimum(1, t / 0.002)
    hit = filt(noise(len(t)), "low", 3500) * np.exp(-t / 0.09) * 0.6
    return np.tanh(1.4 * (boom + hit))


def crash() -> np.ndarray:
    t = t_axis(1.8)
    y = filt(noise(len(t)), "high", 3800) * np.exp(-t / 0.7) * np.minimum(1, t / 0.003)
    return y / np.max(np.abs(y))


# UI sounds
def ui_click(low: bool = False) -> np.ndarray:
    t = t_axis(0.06)
    tick = filt(noise(len(t)), "band", [1800, 6500]) * np.exp(-t / 0.0022)
    tone = np.sin(2 * np.pi * (1300 if low else 1900) * t) * np.exp(-t / 0.005) * 0.6
    thump = np.sin(2 * np.pi * 260 * t) * np.exp(-t / 0.012) * 0.35
    y = tick / np.max(np.abs(tick)) + tone + thump
    return y / np.max(np.abs(y))


def key_tap() -> np.ndarray:
    t = t_axis(0.07)
    y = filt(noise(len(t)), "band", [900, 4200]) * np.exp(-t / 0.009)
    y = y / np.max(np.abs(y)) + 0.35 * np.sin(2 * np.pi * 820 * t) * np.exp(-t / 0.012)
    return y / np.max(np.abs(y))


def chime() -> np.ndarray:
    out = np.zeros(int(SR * 0.9))
    for i, f in enumerate((hz("G5"), hz("C6"))):
        t = t_axis(0.9 - 0.075 * i)
        y = (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t))
        y *= np.minimum(1, t / 0.003) * np.exp(-t / 0.28)
        start = int(0.075 * i * SR)
        out[start:start + len(y)] += y
    return out / np.max(np.abs(out))


def tick() -> np.ndarray:
    t = t_axis(0.03)
    y = filt(noise(len(t)), "band", [2500, 7000]) * np.exp(-t / 0.002)
    return y / np.max(np.abs(y))


# ------------------------------------------------------------------ arrangement

def build(cues: dict, with_sfx: bool) -> tuple[np.ndarray, np.ndarray]:
    beat = cues["beat"]
    duration = cues["duration"]
    bar = 4 * beat
    total = duration + 0.05
    drums, bassb, chords, lead, fx, sfx = (Bus(total) for _ in range(6))
    K, CL, CH, OH, SH = kick(), clap(), hat(), hat(True), shaker()

    def at(b):                                    # beat number -> seconds
        return b * beat

    kicks = []
    for b in range(64):
        bar_i, pos = divmod(b, 4)
        section = "intro" if bar_i < 2 else "brand" if bar_i == 2 else "end" if bar_i >= 14 else "main"
        # kick: four on the floor from the drop, a single hit on the logo, nothing after the final chord
        if section == "main" or (section == "end" and b < 61) or b == 8:
            if b != 55:                           # breath before the end card hit
                drums.add(K, at(b), 0.82)
                kicks.append(at(b))
        if section == "main" and pos in (1, 3):
            drums.add(CL, at(b), 0.44, pan=0.05)
        if section in ("main", "end") and b < 60:
            drums.add(CH, at(b + 0.5), 0.22, pan=0.25)
            if bar_i >= 11 or section == "end":   # montage and end: 16th hats, open hats on the off-beats
                drums.add(CH, at(b + 0.25), 0.08, pan=0.3)
                drums.add(CH, at(b + 0.75), 0.08, pan=0.3)
                drums.add(OH, at(b + 0.5), 0.11, pan=-0.2)
        if section == "intro" and bar_i == 1:
            for s in range(4):
                drums.add(SH, at(b + s * 0.25), 0.10 if s % 2 else 0.16, pan=-0.35)
    # snare roll building into the end card (bar 13, last two beats in 16ths, then 32nds)
    for i in range(16):
        b = 52 + i * 0.25 if i < 8 else 54 + (i - 8) * 0.25
        drums.add(CL, at(b), 0.10 + 0.30 * (i / 15) ** 1.5, pan=0.05)
    for i in range(8):
        drums.add(CL, at(55 + i * 0.125), 0.32 + 0.12 * i / 7, pan=0.05)

    # harmony
    for bar_i, name in enumerate(BARS):
        start, notes = at(bar_i * 4), CHORDS[name]
        if bar_i < 2:
            chords.add(pad(notes, bar * 1.0, bright=1300 + 600 * bar_i), start, 0.7)
        elif bar_i == 2:
            chords.add(pad(notes, bar * 0.95, bright=2400), start, 0.75)
        elif bar_i < 15:
            chords.add(pad(notes, bar * 1.0, bright=2600), start, 0.68)
            for s in (0.5, 1.5, 2.5, 3.5):        # off-beat stabs
                chords.add(stab(notes), start + s * beat, 0.5, pan=-0.15 if s in (0.5, 2.5) else 0.15)
        else:
            chords.add(pad(notes, duration - start, bright=2600), start, 0.8)
            chords.add(stab(notes, 0.5), start, 0.5)
        # bass: root on the off-beats (house bounce), an octave jump on the last one
        if 3 <= bar_i < 15:
            root = hz(ROOT[name])
            for s, mult in ((0.5, 1), (1.5, 1), (2.5, 1), (3.5, 2)):
                bassb.add(bass(root * mult, beat * 0.42), start + s * beat, 0.38)
        if bar_i == 2 or bar_i == 15:
            bassb.add(bass(hz(ROOT[name]), bar * 0.9), start, 0.6)

    # plucked arpeggio: up through the chord in 16ths, every other bar in the groove, all through the intro
    for bar_i, name in enumerate(BARS):
        if bar_i == 2 or bar_i >= 15 or (3 <= bar_i < 11 and bar_i % 2 == 0):
            continue
        tones = [hz(n) * 2 for n in CHORDS[name]]
        seq = tones + tones[::-1][1:-1]
        for s in range(16):
            lead.add(pluck(seq[s % len(seq)]), at(bar_i * 4) + s * beat / 4, 0.22 if bar_i < 2 else 0.18,
                     pan=0.3 * np.sin(s * 0.9))
    # logo sparkle and the end card's bell
    for i, n in enumerate(["C6", "E6", "G6", "C7"]):
        lead.add(bell(hz(n)), at(8) + 0.08 + i * 0.07, 0.10, pan=-0.3 + 0.2 * i)
    for i, n in enumerate(["F5", "A5", "C6", "E6", "G6"]):
        lead.add(bell(hz(n)), at(56) + 0.05 + i * 0.06, 0.09, pan=-0.3 + 0.15 * i)

    # transitions
    fx.add(riser(1.4), at(8) - 1.4, 0.22)
    fx.add(riser(1.8), at(56) - 1.8, 0.24)
    fx.add(impact(), at(8), 0.75)
    fx.add(impact(), at(56), 0.8)
    for b in (12, 44, 56):
        fx.add(crash(), at(b), 0.16, pan=0.1)

    if with_sfx:
        for cue in cues["cues"]:
            k, t = cue["kind"], cue["t"]
            if k == "click":
                sfx.add(ui_click(), t, 0.42, pan=0.1)
            elif k == "rclick":
                sfx.add(ui_click(low=True), t, 0.42, pan=0.1)
            elif k == "key":
                taps = 2 if "+" in cue.get("label", "") else 1
                for i in range(taps):
                    sfx.add(key_tap(), t + 0.06 * i, 0.32, pan=-0.1)
            elif k == "copy":
                sfx.add(chime(), t, 0.26)
            elif k == "wheel":
                sfx.add(tick(), t, 0.16)
            elif k == "caption" and 0.5 < t < duration - 1 and abs(t - at(8)) > 0.2 and abs(t - at(56)) > 0.2:
                sfx.add(whoosh(), t - 0.12, 0.16, pan=-0.2)

    # sidechain: pads, stabs and bass duck under every kick
    duck = np.ones(len(chords.x))
    for k in kicks:
        s = int(k * SR)
        n = min(len(duck) - s, int(0.4 * SR))
        if n > 0:
            tt = np.arange(n) / SR
            duck[s:s + n] = np.minimum(duck[s:s + n], 1 - 0.55 * np.exp(-tt / 0.11))
    chords.x *= duck[:, None]
    bassb.x *= (0.35 + 0.65 * duck)[:, None]

    # reverb sends
    ir = reverb_ir()
    wet = convolve(chords.x * 0.35 + lead.x * 0.6 + fx.x * 0.35 + drums.x * 0.06 + sfx.x * 0.18, ir)
    # dotted-eighth echo on the plucks
    echo = np.zeros_like(lead.x)
    d = int(0.75 * beat * SR)
    for i, g in enumerate((0.35, 0.18, 0.09), start=1):
        echo[d * i:, (i + 1) % 2] += g * lead.x[: len(lead.x) - d * i].sum(axis=1) * 0.5
    wet_sfx = convolve(sfx.x * 0.18, ir)
    music = drums.x + bassb.x + chords.x + lead.x + echo + fx.x + (wet - wet_sfx) * 0.55
    effects = sfx.x + wet_sfx * 0.55
    music, effects = filt(music, "high", 38), filt(effects, "high", 38)     # no sub-sonic rumble
    music = eq(music, ("lowshelf", 90, -2.5), ("peak", 2800, 2.0, 0.8), ("highshelf", 9000, 2.0))
    mix = music + effects

    # gentle bus compression (RMS, 2:1 above -16 dBFS), then a soft limiter
    rms = np.sqrt(signal.sosfilt(sos("low", 8), (mix ** 2).mean(axis=1)) + 1e-12)
    level = 20 * np.log10(rms)
    gain_db = np.where(level > -16, -(level + 16) * 0.5, 0.0)
    gain = (10 ** (gain_db / 20))[:, None]
    # fade the very end
    fade = np.ones((len(mix), 1))
    tail = int(0.6 * SR)
    fade[-tail:, 0] = np.linspace(1, 0, tail) ** 1.5
    return music * gain * fade, effects * gain * fade


# ------------------------------------------------------------------ loudness (ITU-R BS.1770, integrated) and limiting

def k_weight(x: np.ndarray) -> np.ndarray:
    # BS.1770 pre-filter (high shelf) and RLB high-pass, 48 kHz coefficients from the standard
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    return signal.lfilter(b2, a2, signal.lfilter(b1, a1, x, axis=0), axis=0)


def lufs(x: np.ndarray) -> float:
    y = k_weight(x)
    block, step = int(0.4 * SR), int(0.1 * SR)
    powers = np.array([(y[i:i + block] ** 2).mean(axis=0).sum() for i in range(0, len(y) - block, step)])
    loud = -0.691 + 10 * np.log10(powers + 1e-12)
    gated = powers[loud > -70]
    rel = -0.691 + 10 * np.log10(gated.mean()) - 10
    gated = gated[(-0.691 + 10 * np.log10(gated + 1e-12)) > rel]
    return -0.691 + 10 * np.log10(gated.mean())


def true_peak(x: np.ndarray) -> float:
    return float(np.max(np.abs(signal.resample_poly(x, 4, 1, axis=0))))


def master(x: np.ndarray, target_lufs: float = -14.0, ceiling_db: float = -1.0, gain: float | None = None) -> tuple[np.ndarray, float, float]:
    x = x * (10 ** ((target_lufs - lufs(x)) / 20) if gain is None else gain)
    ceiling = 10 ** (ceiling_db / 20)
    for _ in range(6):                            # soft-knee limiting until the true peak sits under the ceiling
        peak = true_peak(x)
        if peak <= ceiling:
            break
        knee = ceiling * 0.8
        over = np.abs(x) > knee
        x[over] = np.sign(x[over]) * (knee + (ceiling - knee) * np.tanh((np.abs(x[over]) - knee) / (ceiling - knee)))
        x *= 0.985
    return x, lufs(x), 20 * np.log10(true_peak(x))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--cues", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--stems", action="store_true",
                        help="also write <out>-music.wav (music alone, mastered on its own) and <out>-sfx.wav (sound effects at their level in the mix)")
    args = parser.parse_args()
    from scipy.io import wavfile
    cues = json.loads(args.cues.read_text(encoding="utf-8"))
    music, effects = build(cues, with_sfx=True)

    def write(path: Path, x: np.ndarray, note: str) -> None:
        wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
        print(f"{path}: {len(x) / SR:.2f}s, {note}")

    full = music + effects
    gain = 10 ** ((-14.0 - lufs(full)) / 20)
    mix, loudness, peak = master(full)
    write(args.out, mix, f"{loudness:.1f} LUFS integrated, true peak {peak:.1f} dBTP")
    if args.stems:
        alone, loudness, peak = master(music)
        write(args.out.with_name(args.out.stem + "-music.wav"), alone, f"{loudness:.1f} LUFS, true peak {peak:.1f} dBTP")
        fx_only = effects * gain
        write(args.out.with_name(args.out.stem + "-sfx.wav"), fx_only, f"same gain as in the mix, peak {20 * np.log10(np.max(np.abs(fx_only)) + 1e-12):.1f} dBFS")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
