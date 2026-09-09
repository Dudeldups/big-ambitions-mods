"""Generate a seamless, compact supercar horn tone for the Revuelto."""
from pathlib import Path
from array import array
import math
import wave


RATE = 44100
SECONDS = 1
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"


def main():
    # Two close fundamentals create the dense beating of an electromagnetic
    # road horn without the musical interval that made the Audi sample sound
    # like a fanfare. Integer-Hz components keep the one-second loop seamless.
    horn = []
    for sample in range(RATE * SECONDS):
        t = sample / RATE
        phase_a = 2 * math.pi * 392 * t
        phase_b = 2 * math.pi * 398 * t
        value = (
            math.cos(phase_a)
            + .48 * math.cos(phase_b)
            + .30 * math.cos(2 * phase_a)
            + .12 * math.cos(3 * phase_a)
            + .08 * math.cos(2 * phase_b)
        )
        horn.append(math.tanh(1.30 * value))

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
