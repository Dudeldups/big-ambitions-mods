"""Generate deterministic, seamless synthetic Bugatti W16 audio layers.

The output uses only mathematical waveforms and deterministic noise. No recorded
engine or horn audio and no third-party samples are embedded in the generated clips.
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
LAYERS = (("EngineLow", 110.0), ("EngineMid", 360.0), ("EngineHigh", 720.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def engine_layer(reference_hz: float, loaded: bool, seed: int) -> list[float]:
    """Build a smooth, low-biased W16 texture with paired-bank modulation."""
    rng = random.Random(seed)
    phases = [rng.random() * math.tau for _ in range(12)]
    amplitudes = (
        (1.0, 0.78, 0.55, 0.42, 0.30, 0.22, 0.16, 0.12, 0.09, 0.07)
        if loaded
        else (1.0, 0.44, 0.27, 0.18, 0.125, 0.09, 0.065, 0.047, 0.034, 0.025)
    )
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            rolloff = math.exp(-((reference_hz * harmonic) / 6000.0) ** 2)
            phase = math.tau * reference_hz * harmonic * time
            # Two phase-offset banks soften the single-frequency synthetic edge.
            bank = math.sin(phase + phases[harmonic - 1])
            paired_bank = math.sin(phase + phases[harmonic - 1] + 0.43)
            value += amplitude * rolloff * (0.61 * bank + 0.39 * paired_bank)
        # Low intake body and an eight-event pulse distinguish the turbo W16
        # from the brighter naturally aspirated V12 synthesis.
        value += (0.24 if loaded else 0.13) * math.sin(
            math.tau * (reference_hz / 2.0) * time + phases[10]
        )
        value += (0.10 if loaded else 0.045) * math.sin(
            math.tau * (reference_hz * 1.5) * time + phases[11]
        )
        if loaded:
            exhaust_pulse = math.tanh(
                2.4 * math.sin(math.tau * (reference_hz / 2.0) * time + phases[10])
            )
            value += 0.24 * exhaust_pulse
            value = math.tanh(value * 1.65)
        output.append(value)
    return normalize(output, 0.135 if loaded else 0.12)


def horn() -> list[float]:
    """Create a deep dual-tone grand-tourer horn as an original loop."""
    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        value = (
            0.78 * math.sin(math.tau * 370.0 * time)
            + 0.66 * math.sin(math.tau * 465.0 * time + 0.18)
            + 0.18 * math.sin(math.tau * 740.0 * time + 0.42)
            + 0.13 * math.sin(math.tau * 930.0 * time + 0.31)
            + 0.07 * math.sin(math.tau * 185.0 * time)
        )
        output.append(math.tanh(value * 0.92))
    return normalize(output, 0.28)


def write_wav(
    path: Path,
    samples: list[float],
    method: str,
) -> dict[str, float | int | str]:
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
        "method": method,
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "8.0 litre quad-turbocharged W16",
        "firing_frequency": "eight combustion events per crankshaft revolution",
        "recorded_samples": False,
        "layers": [],
        "horn": None,
        "runtime_layers": ["low-gain procedural quad-turbo airflow"],
    }
    for layer_index, (name, frequency) in enumerate(LAYERS):
        report["layers"].append(
            write_wav(
                OUTPUT / f"{name}.wav",
                engine_layer(frequency, False, 1600 + layer_index),
                "deterministic additive W16 synthesis",
            )
        )
        report["layers"].append(
            write_wav(
                OUTPUT / f"{name}Load.wav",
                engine_layer(frequency, True, 1700 + layer_index),
                "deterministic additive loaded-W16 synthesis",
            )
        )
    report["horn"] = write_wav(
        OUTPUT / "Horn.wav",
        horn(),
        "deterministic dual-tone horn synthesis",
    )
    (OUTPUT / "generation.json").write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
