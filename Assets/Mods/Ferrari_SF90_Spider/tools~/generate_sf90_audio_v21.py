"""Generate deterministic SF90 V21 engine audio.

A single coherent combustion-pulse loop is used for the audible engine.  This
avoids the double-engine / beating character caused by overlapping tonal bands.
No recorded audio or third-party samples are used.
"""
from __future__ import annotations
import json, math, struct, wave
from pathlib import Path

RATE=44100
SECONDS=2
COUNT=RATE*SECONDS
REF_HZ=88.0  # 176 pulses in two seconds, exact seamless 8-pulse repetition
OUT=Path(__file__).resolve().parents[1]/'Config'/'Audio'


def normalize(samples, target_rms):
    mean=sum(samples)/len(samples)
    samples=[x-mean for x in samples]
    rms=math.sqrt(sum(x*x for x in samples)/len(samples))
    scale=target_rms/max(rms,1e-12)
    return [x*scale for x in samples]


def engine_core():
    out=[0.0]*COUNT
    pulse_count=round(REF_HZ*SECONDS)
    spacing=RATE/REF_HZ
    tail=round(RATE*3.7/REF_HZ)
    gains=(1.00,0.96,0.91,0.97,1.01,0.95,0.90,0.98)
    offsets=(0.0,0.010,-0.006,0.008,-0.009,0.006,-0.004,0.007)
    for p in range(pulse_count):
        phase_idx=p%8
        start=round(p*spacing+offsets[phase_idx]*spacing)
        gain=gains[phase_idx]
        for o in range(tail):
            cyc=o*REF_HZ/RATE
            # Fast pressure rise, long exhaust decay.
            env=(1.0-math.exp(-cyc*18.0))*math.exp(-cyc*1.75)
            # Broad, low-mid resonances rather than narrow horn-like partials.
            res=(
                0.82*math.sin(math.tau*0.72*cyc+0.10)
                +0.40*math.sin(math.tau*1.46*cyc+0.31)
                +0.18*math.sin(math.tau*2.35*cyc+0.57)
                +0.08*math.sin(math.tau*3.90*cyc+0.91)
            )
            # Small rasp term gives the flat-plane edge without becoming a whine.
            rasp=0.035*math.sin(math.tau*7.25*cyc+phase_idx*0.37)
            idx=(start+o)%COUNT
            out[idx]+=gain*env*(res+rasp)
    # Gentle low exhaust body that is phase-coherent with the loop.
    for i in range(COUNT):
        t=i/RATE
        out[i]+=0.11*math.sin(math.tau*(REF_HZ/2.0)*t+0.24)
        out[i]=math.tanh(out[i]*1.10)
    return normalize(out,0.105)


def write(path,samples):
    peak=max(abs(x) for x in samples)
    gain=min(0.92/max(peak,1e-12),1.0)
    payload=b''.join(struct.pack('<h',round(max(-1,min(1,x*gain))*32767)) for x in samples)
    with wave.open(str(path),'wb') as w:
        w.setparams((1,2,RATE,0,'NONE','not compressed'))
        w.writeframes(payload)
    return {'name':path.name,'seconds':len(samples)/RATE,'peak':peak*gain}


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    stats=write(OUT/'EngineCore.wav',engine_core())
    report={
        'engine':'Ferrari SF90 Spider 4.0L twin-turbo flat-plane V8 inspired synthetic voice',
        'recorded_samples':False,
        'architecture':'single coherent combustion-pulse engine core + procedural filters',
        'reference_hz':REF_HZ,
        'layer':stats,
        'notes':'One tonal engine source only; turbo/crackle are non-engine texture layers.'
    }
    (OUT/'generation_v21.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__': main()
