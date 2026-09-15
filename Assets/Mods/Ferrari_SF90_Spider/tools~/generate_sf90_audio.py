"""Generate deterministic, seamless synthetic Ferrari SF90-style V8 layers.

These clips use only mathematical synthesis and deterministic noise. They are
not recordings and contain no third-party audio samples.
"""
from __future__ import annotations
import json, math, random, struct, wave
from pathlib import Path

RATE=44100
SECONDS=2
COUNT=RATE*SECONDS
OUT=Path(__file__).resolve().parents[1]/"Config"/"Audio"
LAYERS=(("EngineLow",90.0),("EngineMid",180.0),("EngineHigh",360.0))

def normalize(x,target):
    m=sum(x)/len(x); x=[v-m for v in x]
    rms=math.sqrt(sum(v*v for v in x)/len(x))
    return [v*target/max(rms,1e-12) for v in x]

def unloaded(freq):
    rng=random.Random(3990)
    phases=[rng.random()*math.tau for _ in range(14)]
    amps=(1,.46,.31,.23,.17,.135,.105,.082,.064,.050,.039,.030,.022,.016)
    out=[]
    for i in range(COUNT):
        t=i/RATE
        v=0.0
        for h,a in enumerate(amps,1):
            # Bright, smooth high-rev 90-degree V8 spectrum.
            roll=math.exp(-((freq*h)/7200.0)**2)
            v+=a*roll*math.sin(math.tau*freq*h*t+phases[h-1])
        # Small half-order bed keeps idle/midrange from sounding like a pure synth.
        v+=.055*math.sin(math.tau*(freq/2)*t+.31)
        out.append(v)
    return normalize(out,.105)

def loaded(freq):
    # Four firing events per crank revolution, shaped into a tighter turbo-V8 bark.
    out=[0.0]*COUNT
    pulses=round(freq*SECONDS)
    spacing=RATE/freq
    gains=(1.00,.89,.96,.82,.92,.86,.98,.84)
    offsets=(0,.012,-.010,.016,-.012,.009,-.006,.011)
    tail=round(RATE*2.7/freq)
    for p in range(pulses):
        c=p%8; start=round(p*spacing+offsets[c]*spacing)
        for k in range(tail):
            u=k*freq/RATE
            env=(1-math.exp(-u*28))*math.exp(-u*2.25)
            resonance=(.68*math.sin(math.tau*1.75*u)+
                       .23*math.sin(math.tau*3.4*u+.18)+
                       .09*math.sin(math.tau*5.8*u+.43))
            out[(start+k)%COUNT]+=gains[c]*env*resonance
    # Deterministic broadband turbo/exhaust texture; periodic envelope keeps seam exact.
    rng=random.Random(9000+round(freq))
    noise=[rng.uniform(-1,1) for _ in range(COUNT)]
    period=COUNT
    for i in range(COUNT):
        phase=math.tau*i/period
        out[i]+=.035*noise[i]*(0.85+.15*math.sin(phase))
    return normalize(out,.125)

def write(path,samples,method):
    peak=max(abs(v) for v in samples); scale=min(.90/max(peak,1e-12),1.0)
    payload=b''.join(struct.pack('<h',round(max(-1,min(1,v*scale))*32767)) for v in samples)
    with wave.open(str(path),'wb') as w:
        w.setparams((1,2,RATE,0,'NONE','not compressed')); w.writeframes(payload)
    return {'name':path.name,'samples':len(samples),'seconds':len(samples)/RATE,
            'peak':peak*scale,'method':method}

def horn(freq,weights,rms):
    x=[]
    for i in range(RATE):
        ph=math.tau*freq*i/RATE
        x.append(math.tanh(.86*sum(a*math.cos((j+1)*ph) for j,a in enumerate(weights))))
    return normalize(x,rms)

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    report={'engine':'3.990 L 90-degree twin-turbo V8 + hybrid system (acoustic synthesis only)',
            'recorded_samples':False,'layers':[],'horn':[],'notes':[
                'Mathematical synthesis only; no Ferrari or third-party recordings.',
                'Runtime pitch/crossfade is calibrated by FerrariSF90SpiderAudioModel.'
            ]}
    for name,f in LAYERS:
        report['layers'].append(write(OUT/(name+'.wav'),unloaded(f),'deterministic additive high-rev V8 synthesis'))
        report['layers'].append(write(OUT/(name+'Load.wav'),loaded(f),'phase-aligned combustion-pulse/turbo texture synthesis'))
    for name,f,w,r in [('HornLow',335,(1,.23,.07,.02),.16),('HornHigh',420,(1,.15,.04),.105)]:
        report['horn'].append(write(OUT/(name+'.wav'),horn(f,w,r),'deterministic dual-reed road-horn synthesis'))
    (OUT/'generation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
if __name__=='__main__': main()
