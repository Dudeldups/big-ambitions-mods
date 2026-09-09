"""Generate seamless low and high road-horn reeds for the Revuelto."""
from pathlib import Path
from array import array
import math
import wave


RATE = 44100
SECONDS = 1
OUT_DIR = Path(__file__).resolve().parents[1] / "Config" / "Audio"


def make_reed(fundamental, harmonic_weights, target_rms):
    horn = []
    for sample in range(RATE * SECONDS):
        t = sample / RATE
        phase = 2 * math.pi * fundamental * t
        value = sum(weight * math.cos(index * phase) for index, weight in enumerate(harmonic_weights, 1))
        horn.append(math.tanh(.92 * value))

    mean = sum(horn) / len(horn)
    horn = [sample - mean for sample in horn]
    unscaled_rms = math.sqrt(sum(sample * sample for sample in horn) / len(horn))
    return [sample * (target_rms / unscaled_rms) for sample in horn]


def write_reed(name, horn):
    peak = max(abs(sample) for sample in horn)
    if peak >= .50:
        raise ValueError("Horn lacks PCM headroom")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{name}.wav"
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        pcm = array("h", (round(sample * 32767) for sample in horn))
        wav.writeframes(pcm.tobytes())

    seam = abs(horn[0] - horn[-1])
    print(
        f"Wrote {path}: seconds={SECONDS} rms={math.sqrt(sum(x * x for x in horn) / len(horn)):.4f} "
        f"peak={peak:.4f} seamStep={seam:.5f}"
    )


def main():
    # Independent stems avoid the doubled, phase-coherent clip that made the
    # previous horn harsh. 320/400 Hz is a conventional deep dual-horn interval;
    # the high reed is deliberately cleaner and quieter than the low reed.
    write_reed("HornLow", make_reed(320, (1.0, .24, .08, .025), .17))
    write_reed("HornHigh", make_reed(400, (1.0, .16, .045), .115))


if __name__ == "__main__":
    main()
