"""Generate deterministic, seamless synthetic Cadillac V8 engine layers.

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
LAYERS = (("EngineLow", 80.0), ("EngineMid", 220.0), ("EngineHigh", 480.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def layer(reference_hz: float, loaded: bool, seed: int) -> list[float]:
    rng = random.Random(seed)
    phases = [rng.random() * math.tau for _ in range(12)]
    amplitudes = (
        (1.0, 0.58, 0.38, 0.27, 0.20, 0.15, 0.11, 0.085, 0.065, 0.05, 0.04, 0.03)
        if loaded
        else (1.0, 0.43, 0.27, 0.18, 0.13, 0.095, 0.07, 0.052, 0.04, 0.03, 0.024, 0.018)
    )
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            rolloff = math.exp(-((reference_hz * harmonic) / 5200.0) ** 2)
            value += amplitude * rolloff * math.sin(
                math.tau * reference_hz * harmonic * time + phases[harmonic - 1]
            )
        # A periodic intake/exhaust pressure pulse gives the large OHV V8 its
        # low-frequency body without embedding a recorded sample.
        value += (0.24 if loaded else 0.12) * math.sin(math.tau * 40.0 * time + phases[0])
        if loaded:
            value = math.tanh(value * 1.28)
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
        "method": "deterministic additive OHV V8 synthesis",
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "6.2 litre naturally aspirated OHV V8",
        "firing_frequency": "four combustion events per crankshaft revolution",
        "recorded_samples": False,
        "layers": [],
        "transients": [],
        "runtime_layers": [
            "very-low-gain procedural exhaust texture",
            "independent 320 Hz and 400 Hz road-horn reeds",
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
