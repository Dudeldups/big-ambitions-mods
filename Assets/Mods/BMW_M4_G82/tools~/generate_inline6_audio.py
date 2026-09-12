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
        for harmonic in (6.5, 7.5, 8.5, 9.5, 10.5, 11.5, 13.5, 15.5)
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
            value = math.tanh((value + 0.11 * texture) * 1.42)
            value += 0.055 * texture
        else:
            value += 0.025 * texture
        output.append(value)
    return normalize(output, 0.115)


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
        "runtime_layers": [
            "very-low-gain procedural continuous exhaust crackle"
        ],
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
    (OUTPUT / "generation.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
