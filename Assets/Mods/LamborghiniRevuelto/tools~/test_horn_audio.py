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


low_reed = tone_amplitude(330)
high_reed = tone_amplitude(390)
assert low_reed > .065, "Missing lower horn reed near 330 Hz"
assert high_reed > .04, "Missing upper horn reed near 390 Hz"
assert high_reed / low_reed > .55, "Upper horn reed is not distinct enough"
assert tone_amplitude(660) > .012, "Missing lower-reed harmonic near 660 Hz"
print(
    f"PASS Horn.wav: seconds=1 RMS={rms:.4f} peak={peak:.3f} "
    f"seamStep={abs(audio[0] - audio[-1]):.5f} "
    f"reeds={low_reed:.4f}/{high_reed:.4f}"
)
