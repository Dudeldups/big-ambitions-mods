"""Generate deterministic, seamless synthetic BMW S58 inline-six engine layers.

The output uses only mathematical waveforms and deterministic noise. No recorded
engine audio or third-party samples are embedded in the generated clips.
"""

from __future__ import annotations

import json
import math
from pathlib import Path
import random
import struct
import wave


RATE = 44_100
SECONDS = 2
COUNT = RATE * SECONDS
OUTPUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"
LAYERS = (("EngineLow", 80.0), ("EngineMid", 190.0), ("EngineHigh", 380.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def layer(reference_hz: float, loaded: bool, seed: int) -> list[float]:
    rng = random.Random(seed)
    phases = [rng.random() * math.tau for _ in range(12)]
    amplitudes = (
        (1.0, 0.56, 0.38, 0.27, 0.20, 0.15, 0.11, 0.08, 0.058, 0.042)
        if loaded
        else (1.0, 0.38, 0.25, 0.17, 0.12, 0.085, 0.06, 0.043, 0.031, 0.023)
    )
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            rolloff = math.exp(-((reference_hz * harmonic) / 7200.0) ** 2)
            value += amplitude * rolloff * math.sin(
                math.tau * reference_hz * harmonic * time + phases[harmonic - 1]
            )
        # Retain BMW-specific inline-six half-order resonances, but keep them
        # below the main harmonic stack so they add body without rasp.
        value += (0.12 if loaded else 0.05) * math.sin(
            math.tau * reference_hz * 0.5 * time + phases[10]
        )
        value += (0.09 if loaded else 0.035) * math.sin(
            math.tau * reference_hz * 1.5 * time + phases[11]
        )
        # A smooth 62 Hz intake pulse borrows the Lamborghini's clean low-body
        # architecture, retuned for the S58 rather than copying its V12 voice.
        value += (0.13 if loaded else 0.05) * math.sin(
            math.tau * 62.0 * time + phases[0]
        )
        if loaded:
            # Gentle saturation preserves boost/load density without the raw,
            # rusty upper texture produced by the previous dense stack.
            value = math.tanh(value * 1.20)
        output.append(value)
    return normalize(output, 0.115)


def exhaust_pop(variant: int) -> list[float]:
    """Create a short S58-style exhaust report with low-mid body.

    These are deliberately finite one-shots, not a looping noise bed. The
    variants differ in body pitch and decay so consecutive gear events do not
    repeat an identical synthetic click.
    """
    rng = random.Random(8600 + variant)
    duration = 0.220 + 0.025 * variant
    sample_count = round(duration * RATE)
    body_hz = 82.0 + 11.0 * variant
    filtered_noise = 0.0
    output: list[float] = []
    for index in range(sample_count):
        time = index / RATE
        noise = rng.uniform(-1.0, 1.0)
        filtered_noise = 0.78 * filtered_noise + 0.22 * noise
        body_phase = math.tau * (body_hz * time + 42.0 * time * time)
        body = math.sin(body_phase)
        low_mid = math.sin(math.tau * (205.0 + 23.0 * variant) * time + 0.45)
        crack = (0.38 * noise + 0.62 * filtered_noise) * math.exp(-time / 0.0105)
        rumble = (
            0.78 * body + 0.20 * low_mid + 0.24 * filtered_noise
        ) * math.exp(-time / (0.052 + 0.004 * variant))
        attack = math.sin(min(time / 0.001, 1.0) * math.pi / 2.0) ** 2
        release = math.sin(
            min((duration - time) / 0.018, 1.0) * math.pi / 2.0
        ) ** 2
        output.append((0.72 * crack + rumble) * attack * release)
    return normalize(output, (0.095, 0.088, 0.102)[variant])


def write_wav(path: Path, samples: list[float]) -> dict[str, float | int | str]:
    peak = max(abs(sample) for sample in samples)
    scale = min(0.92 / max(peak, 1e-12), 1.0)
    payload = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, sample * scale)) * 32767.0))
        for sample in samples
    )
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(payload)
    return {
        "name": path.name,
        "samples": len(samples),
        "seconds": len(samples) / RATE,
        "peak": peak * scale,
        "method": "deterministic additive twin-turbo inline-six synthesis",
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "2,993 cc BMW S58 twin-turbo inline-six",
        "firing_frequency": "three combustion events per crankshaft revolution",
        "recorded_samples": False,
        "layers": [],
        "transients": [],
        "runtime_layers": [],
    }
    for layer_index, (name, frequency) in enumerate(LAYERS):
        report["layers"].append(
            write_wav(OUTPUT / f"{name}.wav", layer(frequency, False, 400 + layer_index))
        )
        report["layers"].append(
            write_wav(
                OUTPUT / f"{name}Load.wav",
                layer(frequency, True, 500 + layer_index),
            )
        )
    for variant in range(3):
        stats = write_wav(
            OUTPUT / f"ExhaustPop{variant + 1}.wav",
            exhaust_pop(variant),
        )
        stats["method"] = (
            "deterministic filtered-noise attack with decaying S58 low-mid body"
        )
        report["transients"].append(stats)
    (OUTPUT / "generation.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
