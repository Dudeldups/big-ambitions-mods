#!/usr/bin/env python3
"""Generate deterministic SF90 Spider V19 engine audio.

No recorded engine samples are used.  Unlike V18's sustained harmonic stacks,
V19 is built from repeated damped combustion-pressure pulses.  The pulse shape
is shared across low/mid/high reference bands and coast/load variants so the
runtime crossfades behave like one engine instead of several pitched horns.
"""
from __future__ import annotations

import json
import math
from pathlib import Path
import struct
import wave

RATE = 44_100
SECONDS = 2
COUNT = RATE * SECONDS
OUTPUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"
LAYERS = (("EngineLow", 70.0), ("EngineMid", 175.0), ("EngineHigh", 350.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def combustion_layer(reference_hz: float, loaded: bool) -> list[float]:
    """Flat-plane V8 pulse train with broad, damped pressure resonances.

    The sound is intentionally transient-rich rather than a stack of stationary
    sine tones.  Four slightly different firing pulses repeat in a flat-plane
    cadence.  Load increases exhaust-body and edge, but keeps identical pulse
    timing/phase so coast/load blending cannot turn into a two-note chord.
    """
    output = [0.0] * COUNT
    pulse_count = round(reference_hz * SECONDS)
    spacing = RATE / reference_hz
    tail_cycles = 2.85 if loaded else 2.35
    tail_samples = max(8, round(RATE * tail_cycles / reference_hz))

    # Even flat-plane cadence with tiny bank/runner differences, not a lopey
    # cross-plane pattern.  Values repeat exactly over the loop.
    gains = (1.00, 0.94, 0.98, 0.92)
    timing = (0.000, 0.010, -0.007, 0.005)
    phase_a = (0.00, 0.12, -0.08, 0.06)
    phase_b = (0.18, -0.10, 0.13, -0.04)

    for pulse in range(pulse_count):
        bank = pulse & 3
        start = round(pulse * spacing + timing[bank] * spacing)
        gain = gains[bank]
        for offset in range(tail_samples):
            cyc = offset * reference_hz / RATE
            attack = 1.0 - math.exp(-cyc * (31.0 if loaded else 25.0))
            decay = math.exp(-cyc * (1.65 if loaded else 1.95))
            env = attack * decay

            # Broad pressure body: non-integer resonances keep it mechanical
            # instead of organ-like after pitch shifting.
            body = (
                0.69 * math.sin(math.tau * 0.73 * cyc + phase_a[bank])
                + 0.25 * math.sin(math.tau * 1.61 * cyc + phase_b[bank])
                + 0.10 * math.sin(math.tau * 2.87 * cyc + 0.29)
            )

            # Short metallic/exhaust edge.  The chirped phase spreads energy
            # over a band and avoids a single 'fanfare' partial.
            edge_phase = math.tau * (3.75 * cyc + 0.82 * cyc * cyc)
            edge = math.sin(edge_phase + 0.17 * bank)
            edge *= math.exp(-cyc * (2.8 if loaded else 3.6))

            # Low pressure component gives the 4.0L V8 body without creating a
            # separate bass note or ship-horn fundamental.
            pressure = math.sin(math.tau * 0.36 * cyc + 0.11)

            sample = env * (
                body
                + (0.25 if loaded else 0.105) * edge
                + (0.15 if loaded else 0.07) * pressure
            )
            output[(start + offset) % COUNT] += gain * sample

    # Very small continuous mechanical texture made from non-integer partials.
    # It fills the gaps between individual fires but is far below the pulses.
    texture_gain = 0.035 if loaded else 0.018
    for i in range(COUNT):
        t = i / RATE
        output[i] += texture_gain * (
            0.55 * math.sin(math.tau * reference_hz * 2.23 * t + 0.21)
            + 0.30 * math.sin(math.tau * reference_hz * 3.47 * t - 0.33)
            + 0.15 * math.sin(math.tau * reference_hz * 5.11 * t + 0.08)
        )

    return normalize(output, 0.092 if loaded else 0.084)


def idle_layer() -> list[float]:
    # Idle uses the same combustion idea at a low acoustic reference rather
    # than a separate sustained sine bed.
    return combustion_layer(56.0, False)


def turbo_airflow() -> list[float]:
    # Restrained broad airflow, deliberately not a whistle tone.
    freqs = (510.0, 685.0, 910.0, 1215.0, 1580.0, 2055.0)
    amps = (0.30, 0.27, 0.22, 0.16, 0.10, 0.065)
    phases = (0.13, -0.21, 0.38, -0.34, 0.19, 0.07)
    out = []
    for i in range(COUNT):
        t = i / RATE
        value = sum(a * math.sin(math.tau * f * t + p) for f, a, p in zip(freqs, amps, phases))
        value *= 0.88 + 0.12 * math.sin(math.tau * 1.5 * t + 0.27)
        out.append(value)
    return normalize(out, 0.045)


def write_wav(path: Path, samples: list[float], method: str) -> dict[str, float | int | str]:
    peak = max(abs(sample) for sample in samples)
    scale = min(0.90 / max(peak, 1e-12), 1.0)
    payload = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, sample * scale)) * 32767.0))
        for sample in samples
    )
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(payload)
    rms = math.sqrt(sum((sample * scale) ** 2 for sample in samples) / len(samples))
    return {
        "name": path.name,
        "seconds": len(samples) / RATE,
        "peak": peak * scale,
        "rms": rms,
        "method": method,
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "Ferrari SF90 Spider 4.0L twin-turbo flat-plane V8 inspired synthetic profile",
        "recorded_samples": False,
        "design": "damped combustion-pulse synthesis; no donor engine recording",
        "layers": [],
    }
    report["layers"].append(write_wav(
        OUTPUT / "EngineIdle.wav", idle_layer(), "flat-plane V8 damped combustion pulses"
    ))
    for name, hz in LAYERS:
        report["layers"].append(write_wav(
            OUTPUT / f"{name}.wav", combustion_layer(hz, False),
            "phase-coherent flat-plane V8 coast/light-load combustion pulses"
        ))
        report["layers"].append(write_wav(
            OUTPUT / f"{name}Load.wav", combustion_layer(hz, True),
            "phase-coherent flat-plane V8 loaded combustion pulses"
        ))
    report["layers"].append(write_wav(
        OUTPUT / "TurboSpool.wav", turbo_airflow(), "restrained multi-partial turbo airflow"
    ))
    (OUTPUT / "generation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
