"""Derive one continuous, bass-focused engine voice from the selected passage_01.

Requires numpy and soundfile. No overlapping independently pitched recordings.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
import soundfile as sf

root = Path(__file__).resolve().parent.parent / 'Audio' / 'Passage01'
source = root / 'EngineHigh.wav'
x, sr = sf.read(source)
assert x.ndim == 1 and sr == 44100
# Periodic frequency-domain EQ preserves the loop period. Remove subsonic rumble,
# emphasize exhaust body, and attenuate the thin upper-frequency background.
f = np.fft.rfftfreq(len(x), 1 / sr)
highpass = 1 / np.sqrt(1 + (38 / np.maximum(f, .001)) ** 4)
lowpass = 1 / np.sqrt(1 + (f / 1400) ** 8)
body = 1 + .65 * np.exp(-.5 * (np.log2(np.maximum(f, 1) / 190) / 1.15) ** 2)
y = np.fft.irfft(np.fft.rfft(x - x.mean()) * highpass * lowpass * body, n=len(x))
y *= min(.21 / np.sqrt(np.mean(y*y)), .78 / np.max(np.abs(y)))
# Rotate the periodic waveform to put its seam at a quiet, smooth sample pair.
seam_score = np.abs(y - np.roll(y, 1)) + .1 * np.abs(y)
y = np.roll(y, -int(np.argmin(seam_score)))
output = root / 'EngineBody.wav'
sf.write(output, y, sr, subtype='PCM_16')
y, _ = sf.read(output)
def stats(z):
    power = np.abs(np.fft.rfft(z)) ** 2
    return dict(rms=float(np.sqrt(np.mean(z*z))), peak=float(np.max(np.abs(z))),
                seam_step=float(abs(z[0]-z[-1])),
                body_power_share=float(power[(f>=60)&(f<=400)].sum()/power.sum()),
                upper_power_share=float(power[f>=2000].sum()/power.sum()))
report = dict(source='EngineHigh.wav', source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
              output_sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
              sample_rate=sr, channels=1, duration_seconds=len(y)/sr,
              method='One voice; periodic EQ: 38 Hz high-pass, broad 190 Hz body emphasis, 1400 Hz low-pass; PCM16.',
              before=stats(x), after=stats(y),
              calibration='Artistic timbre and pitch mapping, not measured engine RPM or cylinder-count synthesis.')
assert report['after']['peak'] < .8 and report['after']['seam_step'] < .02
assert report['after']['body_power_share'] > report['before']['body_power_share']
assert report['after']['upper_power_share'] < report['before']['upper_power_share']
(root/'body-preparation.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
