"""Generate the deterministic V24 SF90 engine core and dual-reed horn clips.

The shipped clips are mathematical synthesis only. No Ferrari recordings or
third-party audio samples are used. Runtime RPM/load shaping is handled by
FerrariSF90SpiderAudioController, so V24 intentionally uses one tonal engine
loop instead of the obsolete low/mid/high coast/load layer set.
"""
from __future__ import annotations

import json
import math
import struct
import wave
from pathlib import Path

RATE = 44_100
ENGINE_SECONDS = 2
HORN_SECONDS = 1
ENGINE_REFERENCE_HZ = 96.0
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def engine_core() -> list[float]:
    """Create one seamless flat-plane-V8-inspired tonal engine loop."""
    count = RATE * ENGINE_SECONDS
    phases = (
        0.17, 1.03, 2.11, 0.62, 1.74, 2.72, 0.31,
        1.29, 2.36, 0.83, 1.91, 2.98, 0.44, 1.52,
    )
    amplitudes = (
        1.00, .48, .38, .29, .235, .195, .164,
        .136, .111, .089, .070, .055, .042, .031,
    )

    output: list[float] = []
    for index in range(count):
        time = index / RATE
        phase = math.tau * ENGINE_REFERENCE_HZ * time
        value = 0.0
        for harmonic, amplitude in enumerate(amplitudes, 1):
            frequency = ENGINE_REFERENCE_HZ * harmonic
            rolloff = math.exp(-((frequency / 8200.0) ** 2))
            value += amplitude * rolloff * math.sin(
                harmonic * phase + phases[harmonic - 1]
            )

        # Low-order body plus restrained intake/turbo edge. Every multiplier
        # completes an integer number of cycles over the two-second loop.
        value += .055 * math.sin(.5 * phase + .33)
        value += .030 * math.sin(6.5 * phase + 1.19)
        value += .018 * math.sin(9.0 * phase + 2.03)
        output.append(math.tanh(value * 1.08))

    return normalize(output, .112)


def horn_layer(frequency: float, high_voice: bool) -> list[float]:
    count = RATE * HORN_SECONDS
    output: list[float] = []
    for index in range(count):
        time = index / RATE
        phase = math.tau * frequency * time
        wobble = 1.0 + .045 * math.sin(math.tau * 5.0 * time)
        if high_voice:
            value = (
                1.00 * math.sin(phase)
                + .31 * math.sin(2.0 * phase + .10)
                + .16 * math.sin(3.0 * phase + .24)
                + .055 * math.sin(4.0 * phase + .41)
            )
        else:
            value = (
                1.00 * math.sin(phase)
                + .42 * math.sin(2.0 * phase + .08)
                + .23 * math.sin(3.0 * phase + .21)
                + .09 * math.sin(4.0 * phase + .38)
            )
        output.append(math.tanh(value * wobble * 1.05))

    return normalize(output, .145 if not high_voice else .095)


def write_wav(path: Path, samples: list[float], method: str) -> dict[str, object]:
    peak = max(abs(sample) for sample in samples)
    scale = min(.90 / max(peak, 1e-12), 1.0)
    payload = b"".join(
        struct.pack(
            "<h",
            round(max(-1.0, min(1.0, sample * scale)) * 32767.0),
        )
        for sample in samples
    )
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(payload)

    return {
        "name": path.name,
        "seconds": len(samples) / RATE,
        "peak": peak * scale,
        "method": method,
    }


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    engine = write_wav(
        OUT / "EngineCore.wav",
        engine_core(),
        "single-source additive flat-plane-V8-inspired synthesis",
    )
    horn_low = write_wav(
        OUT / "HornLow.wav",
        horn_layer(285.0, False),
        "deterministic deep dual-reed horn synthesis",
    )
    horn_high = write_wav(
        OUT / "HornHigh.wav",
        horn_layer(355.0, True),
        "deterministic supporting dual-reed horn synthesis",
    )

    report = {
        "engine": "Ferrari SF90 Spider 4.0L twin-turbo flat-plane V8 inspired synthetic voice",
        "recorded_samples": False,
        "revision": "V24 single-engine-source runtime architecture",
        "runtime": {
            "engine_sources": 1,
            "turbo_loop": False,
            "crackle_loop": False,
            "native_engine_exhaust_suppressed": True,
            "rpm_load_shaping": "runtime pitch/filter/distortion",
        },
        "engine_core": engine,
        "horn": [horn_low, horn_high],
        "notes": [
            "Mathematical synthesis only; no Ferrari or third-party recordings.",
            "Runtime volume, pitch and filtering are calibrated in FerrariSF90SpiderAudioModel.",
            "Obsolete low/mid/high coast/load layers are not part of V24.",
        ],
    }
    (OUT / "generation.json").write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
