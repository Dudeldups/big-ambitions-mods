#!/usr/bin/env python3
"""Generate deterministic SF90 Spider V18 vehicle audio.

The generated engine is original mathematical synthesis: no recorded Ferrari,
other vehicle, or third-party engine/horn samples are embedded. The timbre is
built around a high-revving twin-turbo flat-plane V8: a broad combustion body,
strong even-order edge under load, and a restrained turbo airflow layer.
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
LAYERS = (("EngineLow", 82.0), ("EngineMid", 235.0), ("EngineHigh", 470.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def softclip(value: float, drive: float) -> float:
    return math.tanh(value * drive)


def v8_layer(reference_hz: float, loaded: bool, seed: int) -> list[float]:
    """Broad flat-plane V8 loop with load-specific exhaust/intake weight."""
    rng = random.Random(seed)
    # Deterministic but not perfectly phase-aligned: this avoids a buzzy organ tone.
    phases = [rng.uniform(-0.45, 0.45) for _ in range(14)]
    if loaded:
        amplitudes = (1.00, 0.62, 0.36, 0.34, 0.21, 0.18, 0.12, 0.10, 0.075, 0.060, 0.045, 0.034)
    else:
        amplitudes = (1.00, 0.43, 0.28, 0.22, 0.145, 0.105, 0.076, 0.057, 0.043, 0.032, 0.024, 0.018)

    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            # Preserve useful rasp but roll off the brittle top end.
            rolloff = math.exp(-((reference_hz * harmonic) / 6500.0) ** 2)
            value += amplitude * rolloff * math.sin(
                math.tau * reference_hz * harmonic * time + phases[harmonic - 1]
            )

        # Flat-plane V8 character: half-order exhaust body and 1.5-order intake texture.
        value += (0.25 if loaded else 0.105) * math.sin(
            math.tau * reference_hz * 0.5 * time + phases[12]
        )
        value += (0.16 if loaded else 0.055) * math.sin(
            math.tau * reference_hz * 1.5 * time + phases[13]
        )
        # A light quarter-order pressure pulse keeps the low/mid bands muscular.
        value += (0.08 if loaded else 0.025) * math.sin(
            math.tau * reference_hz * 0.25 * time + 0.21
        )
        if loaded:
            value = softclip(value, 1.18)
        output.append(value)
    return normalize(output, 0.132 if loaded else 0.118)


def idle_layer() -> list[float]:
    reference_hz = 64.0
    output: list[float] = []
    # A lumpy but stable hot-idle body. All frequencies make an integer number
    # of cycles across the 2-second loop, so the seam is clean.
    for index in range(COUNT):
        time = index / RATE
        value = (
            1.00 * math.sin(math.tau * reference_hz * time)
            + 0.46 * math.sin(math.tau * reference_hz * 2.0 * time + 0.10)
            + 0.30 * math.sin(math.tau * reference_hz * 3.0 * time - 0.17)
            + 0.18 * math.sin(math.tau * reference_hz * 4.0 * time + 0.08)
            + 0.11 * math.sin(math.tau * reference_hz * 0.5 * time + 0.31)
            + 0.06 * math.sin(math.tau * reference_hz * 1.5 * time - 0.22)
        )
        output.append(softclip(value, 1.08))
    return normalize(output, 0.12)


def turbo_spool() -> list[float]:
    """Restrained broadband-ish twin-turbo airflow/whistle loop.

    It deliberately avoids a single dominant sine tone, which would read as an
    insect/electric buzz once pitch-shifted.
    """
    frequencies = (430.0, 570.0, 745.0, 930.0, 1180.0, 1475.0, 1810.0, 2240.0)
    amplitudes = (0.34, 0.30, 0.27, 0.22, 0.18, 0.145, 0.10, 0.065)
    phases = (0.11, -0.27, 0.38, -0.44, 0.19, 0.31, -0.16, 0.07)
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for frequency, amplitude, phase in zip(frequencies, amplitudes, phases):
            value += amplitude * math.sin(math.tau * frequency * time + phase)
        # Slow amplitude breathing, again seamless at two seconds.
        value *= 0.84 + 0.16 * math.sin(math.tau * 2.0 * time + 0.37)
        output.append(value)
    return normalize(output, 0.07)


def write_wav(path: Path, samples: list[float], method: str) -> dict[str, float | int | str]:
    peak = max(abs(sample) for sample in samples)
    scale = min(0.92 / max(peak, 1e-12), 1.0)
    payload = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, sample * scale)) * 32767.0))
        for sample in samples
    )
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(payload)
    rms = math.sqrt(sum((sample * scale) ** 2 for sample in samples) / len(samples))
    return {
        "name": path.name,
        "samples": len(samples),
        "seconds": len(samples) / RATE,
        "peak": peak * scale,
        "rms": rms,
        "method": method,
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "4.0 litre twin-turbo flat-plane V8 with hybrid assistance",
        "recorded_samples": False,
        "design": "original deterministic synthesis; no donor engine recording used",
        "layers": [],
        "runtime_layers": [
            "custom synthesized idle bed",
            "three RPM bands with coast/load variants",
            "restrained high-load twin-turbo airflow",
            "very-low-gain procedural exhaust crackle",
        ],
    }
    report["layers"].append(write_wav(
        OUTPUT / "EngineIdle.wav", idle_layer(), "deterministic flat-plane V8 idle synthesis"
    ))
    for layer_index, (name, frequency) in enumerate(LAYERS):
        report["layers"].append(write_wav(
            OUTPUT / f"{name}.wav",
            v8_layer(frequency, False, 1800 + layer_index),
            "deterministic twin-turbo flat-plane V8 coast/light-load synthesis",
        ))
        report["layers"].append(write_wav(
            OUTPUT / f"{name}Load.wav",
            v8_layer(frequency, True, 1900 + layer_index),
            "deterministic twin-turbo flat-plane V8 loaded exhaust/intake synthesis",
        ))
    report["layers"].append(write_wav(
        OUTPUT / "TurboSpool.wav", turbo_spool(), "multi-partial restrained twin-turbo airflow synthesis"
    ))
    (OUTPUT / "generation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
