"""Check the packaged PCM pop timbre/headroom without simulating Unity DSP."""
from pathlib import Path
import re
import wave
import numpy as np


root = Path(__file__).resolve().parents[1]
model = (root / 'Scripts/AudiRS6RAudioModel.cs').read_text(encoding='utf8')
pitch_max = float(re.search(r'PopPitchMax = ([.\d]+)f;', model).group(1))
pitch_min = float(re.search(r'PopPitchMin = ([.\d]+)f;', model).group(1))
assert .94 <= pitch_min < pitch_max <= 1.02, 'Pop pitch variation is too wide/bright'
prior_rms = (.06683494957100689, .06753696071011568, .05516521685410747)
samples = []
for index in range(3):
    path = root / 'Config/Audio' / f'ExhaustPop{index+1}.wav'
    with wave.open(str(path)) as wav:
        assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 44100)
        rate = wav.getframerate()
        x = np.frombuffer(wav.readframes(wav.getnframes()), '<i2').astype(float)/32768
    samples.append(x.tobytes())
    assert .18 <= len(x)/rate <= .28, 'Pop became a long explosion'
    assert np.max(abs(x)) < .5, 'Excessive transient or insufficient headroom'
    assert abs(x.mean()) < 1e-5 and max(abs(x[0]), abs(x[-1])) < 1e-4, 'DC/boundary click'
    rms = np.sqrt(np.mean(x*x))
    assert abs(rms/prior_rms[index]-1) < .02, 'Pop energy changed instead of just timbre'
    energy = x*x
    opening = energy[:round(.005*rate)].sum()/energy.sum()
    body = energy[round(.005*rate):round(.08*rate)].sum()/energy.sum()
    tail = energy[round(.1*rate):].sum()/energy.sum()
    assert opening < .15 and body > .70, 'Opening click dominates exhaust body'
    assert .001 < tail < .03, 'Missing short tail or excessive ringing'
    power = abs(np.fft.rfft(x))**2
    # Inspect both ends of runtime pitch variation, including the brightest.
    for pitch in (pitch_min, pitch_max):
        f = np.fft.rfftfreq(len(x), 1/rate)*pitch
        centroid = (f*power).sum()/power.sum()
        low_mid = power[(f >= 70) & (f <= 700)].sum()/power.sum()
        highs = power[f > 2000].sum()/power.sum()
        assert 180 < centroid < 650, 'Pop is too sub-heavy or too bright'
        assert low_mid > .78 and highs < .005, 'Pop lacks low-mid body or regained upper clack'
    print(f'PASS {path.name}: RMS={rms:.4f}, peak={max(abs(x)):.3f}, '
          f'body70-700Hz={low_mid:.1%}, above2kHz={highs:.4%}, first5ms={opening:.1%}')
assert len(set(samples)) == 3, 'Pop variants must remain distinct'
