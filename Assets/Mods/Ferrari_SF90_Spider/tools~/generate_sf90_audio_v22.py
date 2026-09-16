"""Generate deterministic SF90 V22 engine bands.

The architecture mirrors the *method* proven by the other working sports-car
mods: three RPM reference bands, coast/load variants, common harmonic phase and
DSP-synchronous crossfades.  The actual waveform, reference frequencies and
harmonic balance are unique to the SF90's 4.0L twin-turbo flat-plane V8.
No recorded or third-party engine audio is embedded.
"""
from __future__ import annotations
import json, math, random, struct, wave
from pathlib import Path

RATE=44100
SECONDS=2
COUNT=RATE*SECONDS
OUT=Path(__file__).resolve().parents[1]/'Config'/'Audio'
LAYERS=(("EngineLow",76.0),("EngineMid",205.0),("EngineHigh",410.0))

# Identical phases across all bands are important: after pitch correction the
# adjacent bands crossfade as one engine instead of beating against each other.
RNG=random.Random(9016)
PHASES=[RNG.random()*math.tau for _ in range(14)]


def normalize(samples,target_rms=.115):
    mean=sum(samples)/len(samples)
    centered=[x-mean for x in samples]
    rms=math.sqrt(sum(x*x for x in centered)/len(centered))
    return [x*target_rms/max(rms,1e-12) for x in centered]


def band(reference_hz:float, loaded:bool):
    # More 2nd-6th harmonic energy than V21 creates the metallic/exhaust edge a
    # modern flat-plane V8 needs, but high-order terms roll off smoothly so it
    # does not turn into a whistle or brass-like chord.
    amps=(
        (1.00,.58,.46,.34,.27,.21,.16,.12,.090,.067,.050,.038)
        if loaded else
        (1.00,.43,.31,.22,.16,.12,.088,.066,.050,.038,.029,.022)
    )
    out=[]
    for i in range(COUNT):
        t=i/RATE
        v=0.0
        for h,a in enumerate(amps,1):
            roll=math.exp(-((reference_hz*h)/6500.0)**2)
            v += a*roll*math.sin(math.tau*reference_hz*h*t+PHASES[h-1])
        # Flat-plane V8 exhaust/body: even firing with a restrained half-order
        # resonance and a broad intake term rather than a second tonal engine.
        v += (0.13 if loaded else 0.075)*math.sin(
            math.tau*(reference_hz*.5)*t+PHASES[12])
        v += (0.075 if loaded else 0.035)*math.sin(
            math.tau*(reference_hz*1.5)*t+PHASES[13])
        # Deterministic short pressure-pulse shaping adds combustion attack.
        pulse=(0.5+0.5*math.sin(math.tau*reference_hz*t+0.17))**5
        v += (0.18 if loaded else 0.075)*(pulse-.246)
        if loaded:
            v=math.tanh(v*1.15)
        out.append(v)
    return normalize(out,.118 if loaded else .108)


def write(path,samples):
    peak=max(abs(x) for x in samples)
    scale=min(.92/max(peak,1e-12),1.0)
    payload=b''.join(struct.pack('<h',round(max(-1,min(1,x*scale))*32767)) for x in samples)
    with wave.open(str(path),'wb') as w:
        w.setparams((1,2,RATE,0,'NONE','not compressed'))
        w.writeframes(payload)
    return {'name':path.name,'seconds':len(samples)/RATE,'peak':peak*scale}


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    stats=[]
    for name,hz in LAYERS:
        stats.append(write(OUT/(name+'.wav'),band(hz,False)))
        stats.append(write(OUT/(name+'Load.wav'),band(hz,True)))
    report={
        'engine':'Ferrari SF90 Spider 4.0L twin-turbo flat-plane V8 inspired synthetic voice',
        'recorded_samples':False,
        'architecture':'phase-aligned three-band coast/load synthesis; constant-power RPM crossfade',
        'references_hz':[x[1] for x in LAYERS],
        'layers':stats,
        'notes':'No native donor engine bed is audible; turbo/crackle are intentionally very low non-engine texture.'
    }
    (OUT/'generation_v22.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__': main()
