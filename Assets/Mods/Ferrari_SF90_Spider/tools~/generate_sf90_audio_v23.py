"""Generate the SF90 V23 single-source engine loop.

This uses the same proven *simple additive synthesis family* as the working
Porsche 911 GT3 RS and Lamborghini Revuelto mods, but with a distinct harmonic
balance and lower acoustic range for the SF90's twin-turbo flat-plane V8.
There is exactly one tonal engine loop; runtime pitch/filtering supplies RPM and
load character. No recordings or third-party samples are embedded.
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
REF_HZ = 96.0
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def engine_core() -> list[float]:
    rng = random.Random(2390)
    phases = [rng.random() * math.tau for _ in range(12)]
    # More low/mid harmonic density than the Porsche generator, but less upper
    # harmonic energy than the Revuelto so the result stays deep and turbo-V8
    # rather than flat-six sharp or V12 bright.
    amplitudes = (
        1.00, 0.50, 0.36, 0.27, 0.205, 0.155,
        0.118, 0.089, 0.067, 0.050, 0.037, 0.028,
    )
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            rolloff = math.exp(-((REF_HZ * harmonic) / 6800.0) ** 2)
            value += amplitude * rolloff * math.sin(
                math.tau * REF_HZ * harmonic * time + phases[harmonic - 1]
            )
        # Restrained half-order exhaust body. It is part of this same loop, not
        # a second AudioSource, so it cannot form a separate audible engine.
        value += 0.105 * math.sin(math.tau * (REF_HZ * 0.5) * time + 0.31)
        value = math.tanh(value * 1.10)
        output.append(value)
    return normalize(output, 0.118)


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
        "seconds": len(samples) / RATE,
        "peak": peak * scale,
        "method": "single-source additive twin-turbo flat-plane V8 synthesis",
    }


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    result = write_wav(OUT / "EngineCore.wav", engine_core())
    report = {
        "engine": "Ferrari SF90 Spider 4.0L twin-turbo flat-plane V8 inspired synthetic voice",
        "recorded_samples": False,
        "architecture": "one tonal engine source only; RPM/load via runtime pitch/filter/distortion",
        "reference_hz": REF_HZ,
        "layer": result,
        "notes": "No RPM bands, no turbo loop, no crackle loop, no native donor engine/exhaust bed.",
    }
    (OUT / "generation_v23.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
