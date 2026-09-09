"""Generate a seamless, compact supercar horn tone for the Revuelto."""
from pathlib import Path
from array import array
import math
import wave


RATE = 44100
SECONDS = 1
OUT = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"


def main():
    # One lower fundamental and its fixed harmonics create a compact road-horn
    # timbre without either a musical interval or slow beating. The integer-Hz
    # fundamental also keeps the one-second loop seamless.
    horn = []
    for sample in range(RATE * SECONDS):
        t = sample / RATE
        phase = 2 * math.pi * 330 * t
        value = (
            math.cos(phase)
            + .36 * math.cos(2 * phase)
            + .17 * math.cos(3 * phase)
            + .07 * math.cos(5 * phase)
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
