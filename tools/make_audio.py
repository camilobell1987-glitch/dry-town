#!/usr/bin/env python3
"""Synthesise every sound in Dry Town from scratch.

No samples, no third-party audio: each file below is built from sine partials and
filtered noise with numpy, then written as 16-bit mono PCM WAV at 22050 Hz into
godot/audio/. The output is deterministic (fixed seed), so rerunning gives the same bytes.

    python3 tools/make_audio.py
"""

import os
import wave

import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "godot", "audio")
rng = np.random.default_rng(1927)


# ---------------------------------------------------------------- helpers

def t_axis(seconds):
    return np.arange(int(round(seconds * SR))) / SR


def midi_hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def spectral(x, gain_fn, circular=False):
    """Filter by shaping the spectrum. Circular filtering keeps a loop seamless;
    otherwise the signal is zero-padded so nothing wraps around."""
    n = len(x)
    size = n if circular else n * 2
    spec = np.fft.rfft(x, size)
    f = np.fft.rfftfreq(size, 1.0 / SR)
    return np.fft.irfft(spec * gain_fn(f), size)[:n]


def lowpass(fc, order=2):
    return lambda f: 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order))


def highpass(fc, order=2):
    return lambda f: 1.0 / np.sqrt(1.0 + (fc / np.maximum(f, 1e-3)) ** (2 * order))


def bandpass(lo, hi, order=2):
    lp, hp = lowpass(hi, order), highpass(lo, order)
    return lambda f: lp(f) * hp(f)


def noise(seconds):
    return rng.standard_normal(int(round(seconds * SR)))


def fades(x, fade_in=0.002, fade_out=0.02):
    x = x.copy()
    a, b = int(fade_in * SR), int(fade_out * SR)
    if a:
        x[:a] *= np.linspace(0, 1, a)
    if b:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def normalise(x, peak=0.8):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def reverb(x, seconds=0.6, mix=0.25, tone=3000):
    """Cheap room: convolve with lowpassed, exponentially decaying noise."""
    ir_t = t_axis(seconds)
    ir = rng.standard_normal(len(ir_t)) * np.exp(-ir_t / (seconds / 5.0))
    ir = spectral(ir, lowpass(tone))
    ir /= np.sqrt(np.sum(ir ** 2))
    wet = np.convolve(np.concatenate([x, np.zeros(len(ir))]), ir)[: len(x) + len(ir)]
    dry = np.concatenate([x, np.zeros(len(ir))])
    return dry * (1 - mix) + wet * mix * 0.6


def place(buf, sig, start, wrap=False):
    """Mix sig into buf at sample start; wrap around the end for loops."""
    i = int(start)
    if wrap:
        idx = (np.arange(len(sig)) + i) % len(buf)
        np.add.at(buf, idx, sig)
    else:
        end = min(len(buf), i + len(sig))
        buf[i:end] += sig[: end - i]


def write(name, x):
    x = np.clip(x, -1.0, 1.0)
    data = (x * 32767.0).astype("<i2").tobytes()
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)
    rms = np.sqrt(np.mean(x ** 2))
    print(f"{name + '.wav':22s} {len(x) / SR:6.2f}s  peak {np.max(np.abs(x)):.3f}  "
          f"rms {20 * np.log10(rms + 1e-12):6.1f} dBFS  {os.path.getsize(path) / 1024:7.1f} KiB")


# ---------------------------------------------------------------- instruments

def piano(m, dur, vel=1.0):
    """Additive piano: decaying harmonics on two slightly detuned strings, damper release."""
    f0 = midi_hz(m)
    t = t_axis(dur + 0.35)
    base_tau = np.clip(1.6 * (261.0 / f0) ** 0.5, 0.35, 2.5)
    out = np.zeros(len(t))
    for detune in (-1.6, 1.4):
        f = f0 * 2 ** (detune / 1200.0)
        for n in range(1, 11):
            fn = f * n * (1 + 0.0004 * n * n)  # slight stretch, like real strings
            if fn > 5500:
                break
            amp = (1.0 / n ** 1.3) * (0.55 if n == 1 and m > 60 else 1.0)
            tau = base_tau / (1 + 0.55 * (n - 1))
            out += amp * np.exp(-t / tau) * np.sin(2 * np.pi * fn * t + n)
    att = np.minimum(1.0, t / 0.003)
    rel = np.where(t > dur, np.exp(-(t - dur) / 0.07), 1.0)
    return out * att * rel * vel * 0.25


def trumpet(m, dur, vel=1.0):
    """Muted trumpet: band-limited saw with a nasal formant hump, delayed vibrato."""
    f0 = midi_hz(m)
    t = t_axis(dur + 0.08)
    vib_depth = 0.006 * np.clip((t - 0.15) / 0.25, 0, 1)
    freq = f0 * (1 + vib_depth * np.sin(2 * np.pi * 5.4 * t)) * (1 - 0.02 * np.exp(-t / 0.03))
    phase = 2 * np.pi * np.cumsum(freq) / SR
    out = np.zeros(len(t))
    for n in range(1, 16):
        fn = f0 * n
        if fn > 5000:
            break
        formant = 1.0 + 2.5 * np.exp(-((fn - 1500.0) / 600.0) ** 2)  # cup-mute "wah"
        rolloff = 1.0 / (1 + (fn / 2200.0) ** 4)
        out += (1.0 / n) * formant * rolloff * np.sin(n * phase)
    att = np.minimum(1.0, t / 0.035)
    rel = np.clip((dur + 0.08 - t) / 0.08, 0, 1)
    breath = spectral(noise(len(t) / SR), bandpass(1200, 3000)) * 0.03 * np.exp(-t / 0.05)
    return (out * att * rel + breath) * vel * 0.12


def brush(accent=1.0, length=0.18):
    """Brushed snare: bandpassed noise swish with a soft attack."""
    t = t_axis(length)
    env = np.minimum(1.0, t / 0.012) * np.exp(-t / (length / 3.5))
    return spectral(noise(length), bandpass(1800, 6000, 1)) * env * 0.05 * accent


# ---------------------------------------------------------------- music

def make_music():
    bpm = 160
    beat = 60.0 / bpm
    bar = beat * 4
    bars = 24  # two 12-bar blues choruses in F: 36 s
    n_total = int(round(bars * bar * SR))
    tail = int(3 * SR)
    buf = np.zeros(n_total + tail)

    def at(b, eighth):
        """Swung eighth position (0..8) within bar b, in samples."""
        pos = (eighth // 2) + (2.0 / 3.0 if eighth % 2 else 0.0)
        return (b * bar + pos * beat) * SR

    def eighth_len(eighth, length):
        def p(e):
            return (e // 2) + (2.0 / 3.0 if e % 2 else 0.0)
        return (p(eighth + length) - p(eighth)) * beat

    F7 = dict(bass=(41, 36), stab=(57, 63, 65, 69))
    Bb7 = dict(bass=(46, 41), stab=(56, 62, 65, 68))
    C7 = dict(bass=(36, 43), stab=(58, 64, 67, 70))
    form = [F7, Bb7, F7, F7, Bb7, Bb7, F7, F7, C7, Bb7, F7, C7]

    # Head: (eighth index in bar, length in eighths, midi). Blue notes on Ab and Eb.
    head = [
        [(0, 2, 72), (3, 1, 74), (4, 2, 72), (6, 2, 69)],
        [(1, 2, 68), (3, 3, 70), (7, 1, 74)],
        [(0, 2, 72), (3, 1, 75), (4, 2, 74), (6, 1, 72), (7, 1, 69)],
        [(0, 3, 75), (3, 1, 74), (4, 4, 72)],
        [(0, 2, 74), (3, 1, 77), (4, 2, 74), (6, 2, 70)],
        [(1, 2, 68), (3, 3, 70), (7, 1, 72)],
        [(0, 2, 72), (2, 1, 69), (3, 1, 72), (4, 4, 77)],
        [(0, 2, 75), (3, 1, 74), (4, 2, 72), (6, 2, 69)],
        [(0, 2, 79), (3, 1, 76), (4, 2, 72), (6, 2, 70)],
        [(0, 2, 77), (3, 1, 74), (4, 2, 70), (6, 2, 68)],
        [(0, 3, 69), (3, 1, 72), (4, 4, 65)],
        [(0, 1, 67), (1, 1, 69), (2, 1, 70), (3, 1, 72), (4, 2, 74), (6, 2, 76)],
    ]

    for b in range(bars):
        ch = form[b % 12]
        chorus = b // 12
        # Stride left hand: bass on 1 and 3, chord stab on 2 and 4.
        for beat_i, note in ((0, ch["bass"][0]), (2, ch["bass"][1])):
            s = piano(note, beat * 0.9, 0.9) + 0.5 * piano(note + 12, beat * 0.9, 0.5)
            place(buf, s, (b * bar + beat_i * beat) * SR)
        for beat_i in (1, 3):
            stab = sum(piano(n, beat * 0.45, 0.42) for n in ch["stab"])
            place(buf, stab, (b * bar + beat_i * beat) * SR)
        for e, ln, m in head[b % 12]:
            d = eighth_len(e, ln)
            if chorus == 0:
                # Piano right hand, doubled an octave down softly.
                s = piano(m, d * 0.95, 0.85) + piano(m - 12, d * 0.95, 0.3)
            else:
                s = trumpet(m, d * 0.92, 0.9 + 0.1 * (e % 2 == 0))
            place(buf, s, at(b, e))

        # Under the trumpet chorus the piano answers with a high chord-tone fill.
        if chorus == 1 and b % 2 == 1:
            for k, n in enumerate(sorted(ch["stab"])[1:]):
                place(buf, piano(n + 12, beat * 0.3, 0.35), at(b, 5 + k % 2) + k * 0.02 * SR)

        # Brushes: swish on 2 and 4, light swung taps elsewhere.
        for beat_i in range(4):
            accent = 1.0 if beat_i in (1, 3) else 0.45
            place(buf, brush(accent, 0.2 if accent == 1.0 else 0.1), (b * bar + beat_i * beat) * SR)
            place(buf, brush(0.3, 0.08), at(b, beat_i * 2 + 1))

    # Fold the tails past the loop point back into the start so the seam is continuous.
    music = buf[:n_total].copy()
    music[:tail] += buf[n_total:]

    # Lo-fi warmth (circular, so the loop stays seamless): gentle 5 kHz low-pass, lift lows a touch.
    music = spectral(music, lambda f: lowpass(5000, 1)(f) * highpass(40, 1)(f), circular=True)

    # Vinyl: faint hiss plus sparse crackle pops.
    hiss = spectral(rng.standard_normal(n_total), bandpass(2000, 7000, 1), circular=True) * 0.0025
    pops = np.zeros(n_total)
    idx = rng.choice(n_total, size=int(bars * bar * 6), replace=False)
    pops[idx] = rng.standard_normal(len(idx)) * rng.uniform(0.01, 0.05, len(idx))
    pops = spectral(pops, bandpass(800, 6000, 1), circular=True)

    music = music / np.max(np.abs(music))
    music = music + hiss + pops
    # Gentle soft-clip to raise loudness, then peak to 0.8.
    music = np.tanh(music * 1.1) / np.tanh(1.1)
    return normalise(music, 0.8)


# ---------------------------------------------------------------- ambience

def car_horn(f=330.0, dur=0.5):
    t = t_axis(dur)
    sig = sum((1.0 / n) * np.sin(2 * np.pi * f * n * t) for n in range(1, 8))
    sig += sum((0.6 / n) * np.sin(2 * np.pi * f * 1.26 * n * t) for n in range(1, 6))
    env = np.minimum(1, t / 0.03) * np.clip((dur - t) / 0.06, 0, 1)
    return spectral(sig * env, lowpass(1200)) * 0.25


def streetcar_bell(f=1150.0):
    t = t_axis(1.4)
    ratios = (1.0, 2.32, 4.25, 5.4)
    sig = sum(np.exp(-t / (0.6 / (1 + i))) * np.sin(2 * np.pi * f * r * t) / (1 + i)
              for i, r in enumerate(ratios))
    return sig * np.minimum(1, t / 0.002)


def make_ambience():
    seconds = 30.0
    n = int(seconds * SR)

    # Traffic rumble: brown-ish noise shaped in the frequency domain (circular = seamless).
    rumble = spectral(rng.standard_normal(n),
                      lambda f: 1.0 / np.maximum(f, 15.0) * lowpass(220, 2)(f) * highpass(25, 2)(f),
                      circular=True)
    rumble /= np.max(np.abs(rumble))
    swell = spectral(rng.standard_normal(n), lowpass(0.15, 2), circular=True)
    swell = 0.7 + 0.3 * swell / np.max(np.abs(swell))
    rumble *= swell * 0.5

    # Crowd murmur: speech-band noise with a syllabic (3-6 Hz) random envelope, far away.
    murmur = np.zeros(n)
    for lo, hi in ((250, 700), (600, 1400), (1200, 2400)):
        band = spectral(rng.standard_normal(n), bandpass(lo, hi, 2), circular=True)
        env = spectral(rng.standard_normal(n), bandpass(2.5, 6.0, 1), circular=True)
        env = np.maximum(0, env / np.max(np.abs(env)) + 0.2)
        murmur += band / np.max(np.abs(band)) * env
    murmur = spectral(murmur, lowpass(1500, 1), circular=True)
    murmur *= 0.12 / np.max(np.abs(murmur))

    amb = rumble + murmur

    # Distant events, wrapped around the end so none is cut at the loop point.
    for start, f, d in ((4.2, 330, 0.35), (4.7, 330, 0.55), (17.8, 392, 0.6), (27.6, 300, 0.4)):
        place(amb, reverb(car_horn(f, d), 1.0, 0.6, 1500) * 0.35, start * SR, wrap=True)
    for start in (11.0, 11.45):
        place(amb, spectral(reverb(streetcar_bell(), 1.2, 0.5, 2500), lowpass(2500)) * 0.05,
              start * SR, wrap=True)
    for start in (23.0,):
        place(amb, spectral(reverb(streetcar_bell(1050), 1.2, 0.5, 2500), lowpass(2500)) * 0.04,
              start * SR, wrap=True)

    # Belt and braces: crossfade the last half second into the first.
    x = int(0.5 * SR)
    ramp = np.linspace(0, 1, x)
    amb[:x] = amb[:x] * ramp + amb[-x:] * (1 - ramp)
    amb = amb[:-x]
    # Quiet: peak 0.45, it sits under everything.
    return normalise(amb, 0.45)


# ---------------------------------------------------------------- one-shots

def shot(intensity=1.0, tone=1.0):
    t = t_axis(0.6)
    crack = spectral(noise(0.6), highpass(1500, 2)) * np.exp(-t / 0.004) * 1.4
    body = spectral(noise(0.6), lowpass(900 * tone, 2)) * np.exp(-t / 0.045) * 2.5
    thump_f = 110 * tone * np.exp(-t / 0.05) + 45
    thump = np.sin(2 * np.pi * np.cumsum(thump_f) / SR) * np.exp(-t / 0.07) * 0.8
    tail = spectral(noise(0.6), bandpass(200, 2500, 1)) * np.exp(-t / 0.16) * 0.25
    s = (crack + body + thump + tail) * intensity
    s = np.tanh(s * 1.5)
    return reverb(s, 0.5, 0.3, 2500)[: len(t)]


def make_gunshot():
    return normalise(fades(shot(), 0.0005, 0.08))


def make_gunshots():
    out = np.zeros(int(1.2 * SR))
    for start, inten, tone in ((0.0, 1.0, 1.0), (0.21, 0.85, 1.08), (0.46, 0.95, 0.93), (0.74, 0.8, 1.04)):
        place(out, shot(inten, tone), start * SR)
    return normalise(fades(out, 0.0005, 0.1))


def make_whistle():
    t = t_axis(0.8)
    trill = 0.5 + 0.5 * np.sin(2 * np.pi * 26 * t)  # the pea rattling
    freq = 2750 * (1 + 0.025 * np.sin(2 * np.pi * 26 * t + 0.6)) * (1 - 0.03 * np.exp(-t / 0.05))
    phase = 2 * np.pi * np.cumsum(freq) / SR
    tone = np.sin(phase) + 0.18 * np.sin(2 * phase) + 0.05 * np.sin(3 * phase)
    breath = spectral(noise(0.8), bandpass(2200, 3600, 2)) * 0.25
    env = np.minimum(1, t / 0.03) * np.clip((0.8 - t) / 0.08, 0, 1)
    s = (tone * (0.45 + 0.55 * trill) + breath * (0.6 + 0.4 * trill)) * env
    return normalise(fades(s, 0.002, 0.03), 0.7)


def bell_strike(f, decay, partials=(1.0, 2.0, 2.76, 5.4, 8.93)):
    t = t_axis(decay * 4)
    s = sum(np.exp(-t / (decay / (1 + 0.6 * i))) * np.sin(2 * np.pi * f * r * t) / (1 + 0.8 * i)
            for i, r in enumerate(partials) if f * r < 10000)
    return s * np.minimum(1, t / 0.001)


def thud(f=90.0, decay=0.08, length=0.4, noise_lp=600):
    t = t_axis(length)
    fr = f * (1 + 0.6 * np.exp(-t / 0.015))
    s = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / decay)
    s += spectral(noise(length), lowpass(noise_lp)) * np.exp(-t / (decay * 0.5)) * 0.8
    return s


def make_cash():
    out = np.zeros(int(0.7 * SR))
    # "Ka": key lever click.
    t = t_axis(0.03)
    place(out, spectral(noise(0.03), bandpass(1500, 5000)) * np.exp(-t / 0.004) * 0.6, 0)
    # "Ching": the bell.
    place(out, bell_strike(2093, 0.12) * 0.55, 0.06 * SR)
    # Drawer: a short rattle then a wooden clunk.
    t = t_axis(0.12)
    rattle = spectral(noise(0.12), bandpass(400, 2500)) * (0.5 + 0.5 * np.sin(2 * np.pi * 60 * t)) * 0.18
    place(out, rattle * np.minimum(1, t / 0.01), 0.16 * SR)
    place(out, thud(120, 0.05, 0.3, 900) * 0.7, 0.28 * SR)
    return normalise(fades(out, 0.001, 0.05))


def knock_one(vel=1.0):
    t = t_axis(0.16)
    modes = ((190, 0.035, 1.0), (420, 0.022, 0.6), (760, 0.014, 0.35), (1150, 0.008, 0.25))
    s = sum(a * np.exp(-t / d) * np.sin(2 * np.pi * f * t) for f, d, a in modes)
    s += spectral(noise(0.16), bandpass(800, 4000)) * np.exp(-t / 0.003) * 0.6
    return s * vel


def make_knock():
    out = np.zeros(int(0.7 * SR))
    for start, vel in ((0.02, 1.0), (0.21, 0.9), (0.40, 1.05)):
        place(out, knock_one(vel), start * SR)
    out = reverb(out, 0.3, 0.15, 2000)[: len(out)]
    return normalise(fades(out, 0.001, 0.05))


def make_horn():
    dur = 0.9
    t = t_axis(dur)
    # Pitch: "ah" low, sweeps up for "OO", drops for "ga".
    f = np.interp(t, [0, 0.08, 0.45, 0.62, 0.7, 0.9], [150, 165, 330, 340, 250, 230])
    phase = 2 * np.pi * np.cumsum(f) / SR
    # Vowel: formant moves from open "ah" to closed "oo" then back.
    formant = np.interp(t, [0, 0.15, 0.45, 0.65, 0.75, 0.9], [900, 900, 600, 550, 1000, 1000])
    s = np.zeros(len(t))
    for n in range(1, 25):
        fn = f * n
        amp = (1.0 / n) * (1 + 3 * np.exp(-((fn - formant) / 350.0) ** 2)) * (fn < 6000)
        s += amp * np.sin(n * phase)
    rough = 1 + 0.25 * np.sin(2 * np.pi * 38 * t)  # the mechanical rasp
    env = np.minimum(1, t / 0.04) * np.clip((dur - t) / 0.1, 0, 1)
    s = np.tanh(s * rough * env * 0.4)
    return normalise(fades(reverb(s, 0.3, 0.12)[: len(t)], 0.002, 0.04))


def make_click():
    t = t_axis(0.04)
    s = np.sin(2 * np.pi * 1700 * t) * np.exp(-t / 0.004)
    s += np.sin(2 * np.pi * 900 * t) * np.exp(-t / 0.007) * 0.5
    s += spectral(noise(0.04), bandpass(2000, 6000)) * np.exp(-t / 0.0015) * 0.3
    return normalise(fades(s, 0.0005, 0.01), 0.6)


def make_paper():
    dur = 0.5
    t = t_axis(dur)
    # Crinkle: dense clusters of tiny impulses, thinning out.
    rate = np.interp(t, [0, 0.05, 0.25, 0.5], [0.0, 0.4, 0.15, 0.0])
    imp = (rng.random(len(t)) < rate * 0.12) * rng.standard_normal(len(t))
    crinkle = spectral(imp, bandpass(1500, 7000, 1))
    swish = spectral(noise(dur), bandpass(500, 5000, 1))
    env = np.interp(t, [0, 0.06, 0.18, 0.32, 0.5], [0, 1, 0.55, 0.3, 0])
    s = crinkle * 1.2 + swish * env * 0.35
    # The page flop at the end of the flip.
    place(s, thud(140, 0.03, 0.12, 1500) * 0.08, 0.27 * SR)
    return normalise(fades(s, 0.002, 0.04), 0.75)


def make_door():
    out = np.zeros(int(0.5 * SR))
    place(out, thud(70, 0.11, 0.5, 400) * 1.0, 0)
    t = t_axis(0.18)
    rattle = spectral(noise(0.18), bandpass(600, 3000)) * np.exp(-t / 0.04) * 0.25
    place(out, rattle, 0.004 * SR)
    out = reverb(out, 0.45, 0.25, 1500)[: len(out)]
    out = np.tanh(out * 1.3)
    return normalise(fades(out, 0.0005, 0.06))


def make_bell():
    out = np.zeros(int(0.6 * SR))
    hits = ((0.0, 2350, 1.0), (0.07, 2760, 0.7), (0.13, 2350, 0.6), (0.21, 3120, 0.45),
            (0.30, 2760, 0.3), (0.40, 2350, 0.2))
    for start, f, a in hits:
        f *= 1 + rng.uniform(-0.01, 0.01)
        place(out, bell_strike(f, 0.09, (1.0, 2.41, 3.9, 5.6)) * a, start * SR)
    return normalise(fades(out, 0.001, 0.08), 0.75)


def main():
    os.makedirs(OUT, exist_ok=True)
    write("music_jazz", make_music())
    write("ambience_street", make_ambience())
    write("gunshot", make_gunshot())
    write("gunshots", make_gunshots())
    write("whistle", make_whistle())
    write("cash", make_cash())
    write("knock", make_knock())
    write("horn", make_horn())
    write("click", make_click())
    write("paper", make_paper())
    write("door", make_door())
    write("bell", make_bell())


if __name__ == "__main__":
    main()
