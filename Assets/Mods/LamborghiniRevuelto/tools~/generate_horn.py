"""Generate a seamless, compact supercar horn tone for the Revuelto."""
from pathlib import Path
from array import array
import math
import wave


RATE = 44100
SECONDS = 1
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"


def main():
    # Two fixed lower reeds, separated by 60 Hz, remain individually audible
    # without the slow beating of the previous near-unison attempt. Integer-Hz
    # fundamentals also keep the one-second loop seamless.
    horn = []
    for sample in range(RATE * SECONDS):
        t = sample / RATE
        low_phase = 2 * math.pi * 330 * t
        high_phase = 2 * math.pi * 390 * t
        value = (
            math.cos(low_phase)
            + .68 * math.cos(high_phase)
            + .28 * math.cos(2 * low_phase)
            + .20 * math.cos(2 * high_phase)
            + .10 * math.cos(3 * low_phase)
        )
        horn.append(math.tanh(1.22 * value))

    mean = sum(horn) / len(horn)
    horn = [sample - mean for sample in horn]
    unscaled_rms = math.sqrt(sum(sample * sample for sample in horn) / len(horn))
    horn = [sample * (.14 / unscaled_rms) for sample in horn]
    peak = max(abs(sample) for sample in horn)
    if peak >= .55:
        raise ValueError("Horn lacks PCM headroom")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        pcm = array("h", (round(sample * 32767) for sample in horn))
        wav.writeframes(pcm.tobytes())

    seam = abs(horn[0] - horn[-1])
    print(
        f"Wrote {OUT}: seconds={SECONDS} rms={math.sqrt(sum(x * x for x in horn) / len(horn)):.4f} "
        f"peak={peak:.4f} seamStep={seam:.5f}"
    )


if __name__ == "__main__":
    main()
