"""Validate the Revuelto horn's level, character, and loop boundary."""
from pathlib import Path
from array import array
import math
import sys
import wave


PATH = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"


with wave.open(str(PATH), "rb") as wav:
    assert wav.getparams()[:4] == (1, 2, 44100, 44100)
    audio = array("h")
    audio.frombytes(wav.readframes(wav.getnframes()))
if sys.byteorder != "little":
    audio.byteswap()
audio = [sample / 32767.0 for sample in audio]

rms = math.sqrt(sum(sample * sample for sample in audio) / len(audio))
peak = max(abs(sample) for sample in audio)
assert .135 < rms < .145 and peak < .55, "Horn level/headroom failed"
assert abs(audio[0] - audio[-1]) < .01, "Horn loop boundary has an audible step"

def tone_amplitude(frequency):
    real = 0.0
    imaginary = 0.0
    for index, sample in enumerate(audio):
        phase = 2 * math.pi * frequency * index / 44100
        real += sample * math.cos(phase)
        imaginary -= sample * math.sin(phase)
    return 2 * math.hypot(real, imaginary) / len(audio)


assert tone_amplitude(330) > .08, "Missing stable horn fundamental near 330 Hz"
assert tone_amplitude(660) > .02, "Missing road-horn second harmonic near 660 Hz"
print(
    f"PASS Horn.wav: seconds=1 RMS={rms:.4f} peak={peak:.3f} "
    f"seamStep={abs(audio[0] - audio[-1]):.5f}"
)
