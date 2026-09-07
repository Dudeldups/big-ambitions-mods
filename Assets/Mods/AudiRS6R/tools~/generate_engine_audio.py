"""Generate held engine layers from one user-supplied WAV; Python + NumPy only.

Usage: python generate_engine_audio.py E:/Downloads/AudiRevving.wav
Output is Config/Audio. This averages the spectral character of selected rev
sections and reconstructs periodic harmonics plus stationary noise. It does not
loop a complete rev or infer physical RPM from the recording.
"""
import hashlib
import json
from pathlib import Path
import sys
import wave
import numpy as np

OUT = Path(__file__).resolve().parents[1] / 'Config' / 'Audio'
RATE = 44100
SECONDS = 2
LAYERS = [('EngineLow', .32, .62, 188.46, 96),
          ('EngineMid', 1.50, 2.08, 242.31, 176),
          ('EngineHigh', 3.82, 4.35, 246.14, 320)]


def write(name, audio):
    if not np.all(np.isfinite(audio)):
        raise ValueError('Non-finite samples')
    peak = np.max(np.abs(audio))
    if peak >= .96:
        raise ValueError(f'{name} peak exceeds headroom: {peak}')
    with wave.open(str(OUT / (name + '.wav')), 'wb') as wav:
        wav.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        wav.writeframes(np.round(audio * 32767).astype('<i2').tobytes())
    window = int(RATE*.1)
    rms = [np.sqrt(np.mean(audio[i:i+window]**2))
           for i in range(0, len(audio)-window+1, window)]
    return dict(name=name, seconds=len(audio)/RATE, peak=float(peak),
                rms=float(np.sqrt(np.mean(audio**2))),
                rms_span_db=float(20*np.log10(max(rms)/max(min(rms), 1e-9))),
                seam_step=float(abs(audio[-1]-audio[0])))


def unit_rms(audio):
    return audio / max(np.sqrt(np.mean(audio*audio)), 1e-12)


def make_layer(source, sr, start, end, source_hz, target_hz, seed):
    excerpt = source[int(start*sr):int(end*sr)]
    size = 4096
    frames = np.array([excerpt[i:i+size] for i in range(0,len(excerpt)-size+1,512)])
    if not len(frames):
        raise ValueError('Source is too short for configured analysis sections')
    frames -= frames.mean(axis=1,keepdims=True)
    power = np.mean(abs(np.fft.rfft(frames*np.hanning(size)))**2,axis=0)
    frequencies = np.fft.rfftfreq(size,1/sr)
    # Average spectral envelope retains colour but discards rev/envelope motion.
    envelope = np.convolve(power,np.ones(9)/9,mode='same')
    n = RATE*SECONDS
    f = np.fft.rfftfreq(n,1/RATE)
    noise_rng = np.random.default_rng(seed)
    shape = np.interp(f*source_hz/target_hz,frequencies,np.sqrt(envelope),right=0)
    shape *= (1-np.exp(-(f/180)**4))*np.exp(-(f/6500)**4)
    # Keep random noise clear of the held harmonics to avoid slow beating.
    distance = abs((f+target_hz/2) % target_hz-target_hz/2)
    shape *= np.minimum(distance/30,1)**2
    noise = np.fft.irfft(shape*np.exp(2j*np.pi*noise_rng.random(len(f))),n)
    harmonics = np.zeros(len(f),dtype=complex)
    phase_rng = np.random.default_rng(615)
    for harmonic in range(1,int(7000/target_hz)+1):
        center = harmonic*source_hz
        band = (frequencies>=center-source_hz*.18)&(frequencies<=center+source_hz*.18)
        strength = np.sqrt(np.max(power[band])) if np.any(band) else 0
        strength *= np.exp(-(harmonic*target_hz/5500)**2)
        phase = phase_rng.uniform(0,2*np.pi)
        harmonics[round(harmonic*target_hz*SECONDS)] = strength*np.exp(1j*phase)
    periodic = np.fft.irfft(harmonics,n)
    audio = .90*unit_rms(periodic)+.24*unit_rms(noise)
    audio -= audio.mean()
    audio = .12*unit_rms(audio)
    # Exact DFT bins create a periodic boundary without an amplitude fade/dip.
    return audio


def make_pop(index):
    rng=np.random.default_rng(860+index)
    duration=.20+index*.025
    t=np.arange(round(duration*RATE))/RATE
    noise=rng.normal(size=len(t))
    noise=np.convolve(noise,[.2,.6,.2],mode='same')
    body=np.sin(2*np.pi*(95*t+75*.018*(1-np.exp(-t/.018))))
    crack=noise*np.exp(-t/.014)
    rumble=(.65*body+.28*noise)*np.exp(-t/.043)
    attack=np.minimum(t/.0008,1)
    tail=np.minimum((duration-t)/.018,1)
    audio=(.8*crack+rumble)*attack*tail
    audio-=audio.mean()
    audio*=np.sin(np.minimum(t/.001,1)*np.pi/2)*np.minimum((duration-t)/.012,1)
    return audio*(.72/np.max(abs(audio)))


def make_loaded(base, reference):
    # Add a steady lower firing texture and soft saturation for throttle load.
    # All partials use exact periodic bins; no slow rev or loudness wobble.
    t=np.arange(len(base))/RATE
    phase=2*np.pi*(reference/2)*t
    body=(np.cos(phase)+.42*np.cos(2*phase+.3)+.38*np.cos(3*phase+.7)
          +.26*np.cos(5*phase+1.1)+.14*np.cos(7*phase+.4))
    driven=.68*unit_rms(base)+.72*unit_rms(body)
    shaped=np.tanh(1.65*driven)
    # Stronger firing texture with a wider harmonic band. A parallel base
    # component preserves upper-mid detail instead of merely lowering pitch.
    # The borrowed Car idle and the coast layers never enter this processing.
    f=np.fft.rfftfreq(len(shaped),1/RATE)
    shaped=np.fft.irfft(np.fft.rfft(shaped)*np.exp(-(f/(reference*20))**4),len(shaped))
    shaped=.78*unit_rms(shaped)+.22*unit_rms(base)
    shaped-=shaped.mean()
    return .12*unit_rms(shaped)


def main():
    path=Path(sys.argv[1])
    with wave.open(str(path),'rb') as wav:
        if wav.getsampwidth()!=2 or wav.getcomptype()!='NONE':
            raise ValueError('Expected 16-bit PCM source')
        sr=wav.getframerate()
        source=np.frombuffer(wav.readframes(wav.getnframes()),'<i2').astype(float)
        source=source.reshape(-1,wav.getnchannels()).mean(axis=1)/32768
    if len(source)/sr<4.35:
        raise ValueError('Expected AudiRevving.wav, at least 4.35 seconds')
    OUT.mkdir(parents=True,exist_ok=True)
    report={'source':path.name,'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'method':'stationary harmonic/noise resynthesis; acoustic references are not engine RPM',
            'layers':[],'loaded_layers':[],'pops':[]}
    for i,(name,start,end,ref,target) in enumerate(LAYERS):
        base=make_layer(source,sr,start,end,ref,target,100+i)
        stats=write(name,base)
        stats.update(section=[start,end],source_harmonic_hz=ref,reference_hz=target)
        if stats['rms_span_db']>1:
            raise ValueError(f'{name} retains excessive loudness motion')
        report['layers'].append(stats)
        loaded=write(name+'Load',make_loaded(base,target))
        if loaded['rms_span_db']>1:
            raise ValueError(f'{name} loaded layer retains excessive loudness motion')
        loaded.update(reference_hz=target,method='stronger lower/odd partials and saturation with parallel base detail, same RMS as base')
        report['loaded_layers'].append(loaded)
    for i in range(3):
        report['pops'].append(write('ExhaustPop'+str(i+1),make_pop(i)))
    (OUT/'generation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    main()
