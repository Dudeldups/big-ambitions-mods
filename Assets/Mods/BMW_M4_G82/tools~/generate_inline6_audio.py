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
    phases = [rng.uniform(-0.18, 0.18) for _ in range(14)]
    amplitudes = (
        (1.0, 0.70, 0.52, 0.39, 0.29, 0.22, 0.17, 0.13, 0.10, 0.078, 0.06, 0.046)
        if loaded
        else (1.0, 0.44, 0.30, 0.21, 0.15, 0.11, 0.083, 0.062, 0.047, 0.035, 0.026, 0.020)
    )
    texture_frequencies = [
        reference_hz * harmonic
        for harmonic in (5.5, 6.5, 7.5, 8.5, 9.5, 10.5)
    ]
    texture_phases = [rng.random() * math.tau for _ in texture_frequencies]
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            rolloff = math.exp(-((reference_hz * harmonic) / 7200.0) ** 2)
            value += amplitude * rolloff * math.sin(
                math.tau * reference_hz * harmonic * time + phases[harmonic - 1]
            )
        # Half-order intake/exhaust resonances keep the inline-six body audible
        # without turning the loop into a smooth organ-like tone.
        value += (0.17 if loaded else 0.09) * math.sin(
            math.tau * reference_hz * 0.5 * time + phases[12]
        )
        value += (0.14 if loaded else 0.06) * math.sin(
            math.tau * reference_hz * 1.5 * time + phases[13]
        )
        texture = sum(
            math.sin(math.tau * frequency * time + phase)
            for frequency, phase in zip(texture_frequencies, texture_phases)
        ) / len(texture_frequencies)
        if loaded:
            # Asymmetric soft clipping and a restrained high-frequency exhaust
            # texture make boost sound pressurized and mechanical, not bubbly.
            value = math.tanh((value + 0.055 * texture) * 1.38)
            value += 0.018 * texture
        else:
            value += 0.012 * texture
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
    return normalize(output, (0.082, 0.075, 0.088)[variant])


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
