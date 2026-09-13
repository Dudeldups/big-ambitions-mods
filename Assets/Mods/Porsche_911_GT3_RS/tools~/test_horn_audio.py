"""Validate the GT3 RS horn stems' levels, character, and loop boundaries."""
from pathlib import Path
from array import array
import math
import sys
import wave


ROOT = Path(__file__).resolve().parents[1] / "Config" / "Audio"


def load(name):
    with wave.open(str(ROOT / f"{name}.wav"), "rb") as wav:
        assert wav.getparams()[:4] == (1, 2, 44100, 44100)
        audio = array("h")
        audio.frombytes(wav.readframes(wav.getnframes()))
    if sys.byteorder != "little":
        audio.byteswap()
    return [sample / 32767.0 for sample in audio]


def tone_amplitude(audio, frequency):
    real = 0.0
    imaginary = 0.0
    for index, sample in enumerate(audio):
        phase = 2 * math.pi * frequency * index / 44100
        real += sample * math.cos(phase)
        imaginary -= sample * math.sin(phase)
    return 2 * math.hypot(real, imaginary) / len(audio)


for name, frequency, minimum_rms, maximum_rms in (
    ("HornLow", 320, .165, .175),
    ("HornHigh", 400, .110, .120),
):
    audio = load(name)
    rms = math.sqrt(sum(sample * sample for sample in audio) / len(audio))
    peak = max(abs(sample) for sample in audio)
    fundamental = tone_amplitude(audio, frequency)
    assert minimum_rms < rms < maximum_rms and peak < .50, f"{name} level/headroom failed"
    assert abs(audio[0] - audio[-1]) < .01, f"{name} loop boundary has an audible step"
    assert fundamental > .10, f"{name} is missing its {frequency} Hz reed"
    assert tone_amplitude(audio, frequency * 2) > .01, f"{name} lacks road-horn harmonics"
    print(
        f"PASS {name}.wav: seconds=1 RMS={rms:.4f} peak={peak:.3f} "
        f"seamStep={abs(audio[0] - audio[-1]):.5f} fundamental={fundamental:.4f}"
    )

