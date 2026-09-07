"""Generate the Audi's seamless dual-tone horn WAV; Python + NumPy only."""
from pathlib import Path
import wave
import numpy as np


RATE = 44100
SECONDS = 1
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"


def main():
    t = np.arange(RATE * SECONDS) / RATE
    # Integer-Hz tones and modulation make the one-second loop periodic.
    wobble = .025 * np.sin(2 * np.pi * 6 * t)
    low_phase = 2 * np.pi * 405 * t + wobble
    high_phase = 2 * np.pi * 510 * t - .7 * wobble
    # Cosine phases place the periodic boundary at zero slope, avoiding a
    # once-per-second click while the player holds the horn.
    horn = (np.cos(low_phase) + .86 * np.cos(high_phase)
            + .22 * np.cos(2 * low_phase)
            + .18 * np.cos(2 * high_phase)
            + .07 * np.cos(3 * low_phase))
    horn = np.tanh(1.15 * horn)
    horn -= horn.mean()
    horn *= .12 / np.sqrt(np.mean(horn * horn))
    if np.max(np.abs(horn)) >= .5:
        raise ValueError("Horn lacks PCM headroom")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(np.round(horn * 32767).astype("<i2").tobytes())
    seam = abs(horn[0] - horn[-1])
    print(f"Wrote {OUT}: seconds=1 rms={np.sqrt(np.mean(horn*horn)):.4f} "
          f"peak={np.max(np.abs(horn)):.4f} seamStep={seam:.5f}")


if __name__ == "__main__":
    main()
