"""Generate deterministic synthetic SF90-style high-rev V8 + horn audio.

No recorded Ferrari or third-party samples are used. The loops are mathematical
synthesis only and are designed to stay seamless when pitch-shifted at runtime.
"""
from __future__ import annotations

import json
import math
import struct
import wave
from pathlib import Path

RATE = 44_100
SECONDS = 2
COUNT = RATE * SECONDS
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"
LAYERS = (("EngineLow", 80.0), ("EngineMid", 240.0), ("EngineHigh", 480.0))


def normalize(samples: list[float], target_rms: float) -> list[float]:
    mean = sum(samples) / len(samples)
    centered = [sample - mean for sample in samples]
    rms = math.sqrt(sum(sample * sample for sample in centered) / len(centered))
    return [sample * target_rms / max(rms, 1e-12) for sample in centered]


def engine_layer(reference_hz: float, loaded: bool) -> list[float]:
    # Strong four-stroke V8 firing fundamental plus a deliberately bright upper
    # harmonic stack. Under load the odd/upper harmonics and pulse edge become
    # stronger so the car sounds like a high-output sports V8 rather than a
    # generic low-rev petrol engine.
    phases = (0.17, 1.03, 2.11, 0.62, 1.74, 2.72, 0.31, 1.29,
              2.36, 0.83, 1.91, 2.98, 0.44, 1.52)
    unloaded = (1.00, .44, .34, .25, .205, .17, .145, .118,
                .096, .078, .063, .050, .039, .030)
    loaded_amps = (1.00, .52, .43, .34, .295, .25, .215, .185,
                   .158, .132, .108, .086, .067, .050)
    amps = loaded_amps if loaded else unloaded

    output: list[float] = []
    for index in range(COUNT):
        time = index / RATE
        phase = math.tau * reference_hz * time
        value = 0.0
        for harmonic, amplitude in enumerate(amps, 1):
            frequency = reference_hz * harmonic
            rolloff = math.exp(-((frequency / 8200.0) ** 2))
            value += amplitude * rolloff * math.sin(harmonic * phase + phases[harmonic - 1])

        # Cross-plane body in the lower register, fading naturally once pitch rises.
        value += (.070 if loaded else .045) * math.sin(.5 * phase + .33)

        # Intake/turbo edge. Harmonic-relative tones remain seamless and track pitch.
        value += (.055 if loaded else .020) * math.sin(6.5 * phase + 1.19)
        value += (.035 if loaded else .012) * math.sin(9.0 * phase + 2.03)

        if loaded:
            # A mild nonlinear edge gives throttle bark without simply raising volume.
            value = math.tanh(value * 1.28)
        output.append(value)

    return normalize(output, .120 if loaded else .108)


def horn_layer(frequency: float, high_voice: bool) -> list[float]:
    # Deeper dual-reed profile than the previous 335/420-Hz pair. A small
    # amplitude wobble and stronger 2nd/3rd harmonics make it fuller/deftiger,
    # while RMS and runtime gains remain restrained so it is not simply louder.
    output: list[float] = []
    for index in range(RATE):
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
    OUT.mkdir(parents=True, exist_ok=True)
    report: dict[str, object] = {
        "engine": "3.990 L 90-degree twin-turbo V8 + hybrid system (acoustic synthesis only)",
        "recorded_samples": False,
        "revision": "V16 high-rev sports calibration",
        "layers": [],
        "horn": [],
        "notes": [
            "Mathematical synthesis only; no Ferrari or third-party recordings.",
            "Runtime RPM follows NWH transmission.currentGearRatio and final drive.",
            "High-rpm target reaches ~520 Hz V8 firing frequency near the limiter.",
            "Horn is a deeper 285/355 Hz dual-reed synthesis; gain is not increased.",
        ],
    }
    for name, frequency in LAYERS:
        report["layers"].append(
            write_wav(
                OUT / f"{name}.wav",
                engine_layer(frequency, False),
                "deterministic bright high-rev V8 harmonic synthesis",
            )
        )
        report["layers"].append(
            write_wav(
                OUT / f"{name}Load.wav",
                engine_layer(frequency, True),
                "deterministic loaded high-rev V8 intake/exhaust synthesis",
            )
        )

    report["horn"].append(
        write_wav(
            OUT / "HornLow.wav",
            horn_layer(285.0, False),
            "deterministic deep dual-reed horn synthesis",
        )
    )
    report["horn"].append(
        write_wav(
            OUT / "HornHigh.wav",
            horn_layer(355.0, True),
            "deterministic supporting dual-reed horn synthesis",
        )
    )
    (OUT / "generation.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
