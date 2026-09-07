"""Validate the packaged horn's level, spectrum and loop boundary."""
from pathlib import Path
import wave
import numpy as np


path = Path(__file__).resolve().parents[1] / "Config" / "Audio" / "Horn.wav"
with wave.open(str(path)) as wav:
    assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 44100)
    assert wav.getnframes() == 44100
    rate = wav.getframerate()
    audio = np.frombuffer(wav.readframes(wav.getnframes()), "<i2").astype(float) / 32768
rms = np.sqrt(np.mean(audio * audio))
peak = np.max(np.abs(audio))
assert .115 < rms < .125 and peak < .5, "Horn level/headroom failed"
assert abs(audio[0] - audio[-1]) < .01, "Horn loop boundary has an audible step"
power = np.abs(np.fft.rfft(audio)) ** 2
frequencies = np.fft.rfftfreq(len(audio), 1 / rate)
total = power.sum()
for tone in (405, 510):
    band = power[(frequencies >= tone - 8) & (frequencies <= tone + 8)].sum()
    assert band / total > .12, f"Missing horn tone near {tone} Hz"
assert power[frequencies > 4000].sum() / total < .001, "Horn is excessively bright"
print(f"PASS Horn.wav: seconds=1 RMS={rms:.4f} peak={peak:.3f} "
      f"seamStep={abs(audio[0]-audio[-1]):.5f}; dual 405/510 Hz tones present")
