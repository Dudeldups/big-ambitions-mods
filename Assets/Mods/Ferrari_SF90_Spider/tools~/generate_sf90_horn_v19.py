#!/usr/bin/env python3
"""Generate a firm European dual-tone road horn for the SF90 V19.

The previous 190/232 Hz pair read like a ship horn.  V19 moves back into road-
horn territory while staying fuller than the early thin version.  No samples.
"""
from __future__ import annotations

from array import array
import math
from pathlib import Path
import wave

RATE = 44_100
SECONDS = 1
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio"


def make_reed(fundamental: float, weights: tuple[float, ...], rms_target: float) -> list[float]:
    data: list[float] = []
    for i in range(RATE * SECONDS):
        t = i / RATE
        phase = math.tau * fundamental * t
        value = sum(weight * math.cos((n + 1) * phase + 0.035 * n) for n, weight in enumerate(weights))
        # Mild reed saturation gives authority without adding a sub-bass note.
        data.append(math.tanh(0.84 * value))
    mean = sum(data) / len(data)
    data = [x - mean for x in data]
    rms = math.sqrt(sum(x*x for x in data) / len(data))
    return [x * (rms_target / max(rms, 1e-12)) for x in data]


def write(name: str, data: list[float]) -> None:
    peak = max(abs(x) for x in data)
    if peak > 0.62:
        scale = 0.62 / peak
        data = [x * scale for x in data]
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(array("h", (round(max(-1, min(1, x)) * 32767) for x in data)).tobytes())


def main() -> None:
    # 278/352 Hz: firm/deep for a sports car, but firmly above the ship-horn
    # register.  High reed is cleaner and quieter so the pair sounds compact.
    write("HornLow", make_reed(278.0, (1.0, .27, .10, .035), .175))
    write("HornHigh", make_reed(352.0, (1.0, .18, .055), .120))


if __name__ == "__main__":
    main()
