from pathlib import Path
import json, math, random, re, shutil, struct, wave

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "Assets/Mods/Porsche_911_GT3_RS"
DST = REPO / "Assets/Mods/Volkswagen_Amarok"

if DST.exists():
    shutil.rmtree(DST)
shutil.copytree(SRC, DST)

# Generated/binary Porsche assets are not valid Amarok inputs. The Amarok GLB and
# Blender light-groups file are supplied separately and intentionally not faked.
for name in ["AssetBundles", "Models", "releases", "tools~"]:
    p = DST / name
    if p.exists():
        shutil.rmtree(p)
for p in list(DST.rglob("*.meta")):
    p.unlink()
for name in ["Porsche911GT3RS.asset", "Porsche911GT3RS.prefab", "thumbnail.png"]:
    p = DST / name
    if p.exists(): p.unlink()
(DST / "Models").mkdir(parents=True, exist_ok=True)

# Rename source files/directories and donor symbols first.
for p in sorted(DST.rglob("*"), key=lambda x: len(str(x)), reverse=True):
    n = p.name.replace("Porsche911GT3RS", "VolkswagenAmarok").replace("Porsche_911_GT3_RS", "Volkswagen_Amarok")
    if n != p.name:
        p.rename(p.with_name(n))
for p in DST.rglob("*"):
    if not p.is_file(): continue
    try: s = p.read_text(encoding="utf-8")
    except UnicodeDecodeError: continue
    s = (s.replace("Porsche911GT3RS", "VolkswagenAmarok")
           .replace("Porsche 911 GT3 RS", "Volkswagen Amarok")
           .replace("Porsche_911_GT3_RS", "Volkswagen_Amarok")
           .replace("porsche911gt3rs", "volkswagenamarok")
           .replace("Porsche", "Amarok")
           .replace("porsche", "amarok"))
    p.write_text(s, encoding="utf-8")

def patch(path, replacements):
    p = DST / path
    s = p.read_text(encoding="utf-8")
    for a,b in replacements:
        s = s.replace(a,b)
    p.write_text(s, encoding="utf-8")

setup = DST / "Editor/VolkswagenAmarokSetup.cs"
s = setup.read_text(encoding="utf-8")
for a,b in [
    ('ModRoot + "/Models/2023_amarok_911_gt3_rs_992.glb"','ModRoot + "/Models/2017_volkswagen_amarok_v6.glb"'),
    ('private const float TargetLength = 4.572f;','private const float TargetLength = 5.254f;'),
    ('private const float TargetWidth = 1.900f;','private const float TargetWidth = 1.954f;'),
    ('private const float TargetHeight = 1.322f;','private const float TargetHeight = 1.834f;'),
    ('private const float FrontTrack = 1.630f;','private const float FrontTrack = 1.654f;'),
    ('private const float RearTrack = 1.582f;','private const float RearTrack = 1.658f;'),
    ('private const float Wheelbase = 2.457f;','private const float Wheelbase = 3.097f;'),
    ('private const float FrontTireRadius = 0.35025f;','private const float FrontTireRadius = 0.382f;'),
    ('private const float RearTireRadius = 0.36720f;','private const float RearTireRadius = 0.382f;'),
    ('private const float FrontTireWidth = 0.275f;','private const float FrontTireWidth = 0.255f;'),
    ('private const float RearTireWidth = 0.335f;','private const float RearTireWidth = 0.255f;'),
    ('private const float WheelInset = 0.085f;','private const float WheelInset = 0f;'),
    ('private const float VehicleLinearDrag = 0.035f;','private const float VehicleLinearDrag = 0.045f;'),
    ('private const float VehicleBrakeForce = 2200f;','private const float VehicleBrakeForce = 2800f;'),
    ('private const float BrakeMaxTorque = 2200f;','private const float BrakeMaxTorque = 2800f;'),
    ('private const float AntiRollBarForce = 9000f;','private const float AntiRollBarForce = 3800f;'),
    ('private const float FrontSuspensionTravel = 0.075f;','private const float FrontSuspensionTravel = 0.160f;'),
    ('private const float RearSuspensionTravel = 0.080f;','private const float RearSuspensionTravel = 0.180f;'),
    ('private const float DeformationStrength = 0.17f;','private const float DeformationStrength = 0.12f;'),
    ('private const float DeformationRadius = 0.24f;','private const float DeformationRadius = 0.30f;'),
    ('private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.08f, -0.28f);','private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.34f, -0.05f);'),
    ('SetNumber(serialized, "price", 223800f);','SetNumber(serialized, "price", 49900f);'),
    ('SetNumber(serialized, "maxFuel", 64f);','SetNumber(serialized, "maxFuel", 80f);'),
    ('SetNumber(serialized, "maxCargoCapacity", 2f);','SetNumber(serialized, "maxCargoCapacity", 0f); // No invented pickup cargo capacity.'),
    ('SetNumber(serialized, "maxSpeed", 296f);','SetNumber(serialized, "maxSpeed", 193f);'),
    ('SetNumber(serialized, "enginePower", 386f);','SetNumber(serialized, "enginePower", 165f);'),
    ('SetNumber(serialized, "turnRadius", 26f);','SetNumber(serialized, "turnRadius", 35f);'),
    ('SetNumber(serialized, "damageIntensity", 0.38f);','SetNumber(serialized, "damageIntensity", 0.28f);'),
    ('SetBool(serialized, "isATruck", false);','SetBool(serialized, "isATruck", true);'),
    ('SetBool(serialized, "autoParkSupported", true);','SetBool(serialized, "autoParkSupported", false);'),
    ('SetBool(serialized, "isLuxuryCar", true);','SetBool(serialized, "isLuxuryCar", false);'),
    ('SetRelativeNumber(serialized, "powertrain.engine.maxPower", 386f);','SetRelativeNumber(serialized, "powertrain.engine.maxPower", 165f);'),
    ('SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);','SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 725f);'),
    ('SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 9000f);','SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 4500f);'),
    ('SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);','SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);'),
    ('SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);','SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.32f);'),
    ('SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 4.27f);','SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.70f);'),
    ('SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);','SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);'),
    ('SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);','SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.28f);'),
    ('SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 5500f);','SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1900f);'),
    ('SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 8850f);','SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 4200f);'),
    ('bias.floatValue = 1f;','bias.floatValue = 0.60f; // permanent 4MOTION, rear-biased 40:60 baseline'),
    ('SetRelativeNumber(serialized, "module.speedLimit", 296f);','SetRelativeNumber(serialized, "module.speedLimit", 193f);'),
    ('"gt3rs_bumper_F"','"bump_front_ok"'), ('"gt3rs_bumper_R"','"bump_rear_ok"'),
]: s=s.replace(a,b)
s = re.sub(r'private static readonly float\[] AmarokGears =\s*\{.*?\};', '''private static readonly float[] AmarokGears =\n    {\n        -3.317f, 0f, 4.714f, 3.143f, 2.106f, 1.667f, 1.285f, 1.000f, 0.839f, 0.667f,\n    };''', s, flags=re.S)
s = re.sub(r'private static AnimationCurve CreateAmarokPowerCurve\(\) =>\s*new AnimationCurve\(.*?\);', '''private static AnimationCurve CreateAmarokPowerCurve() =>\n        new AnimationCurve(\n            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f), new Keyframe(0.31f, 0.49f),\n            new Keyframe(0.44f, 0.70f), new Keyframe(0.61f, 0.96f), new Keyframe(0.67f, 1.00f),\n            new Keyframe(0.78f, 1.00f), new Keyframe(0.89f, 1.00f), new Keyframe(1.00f, 0.99f));''', s, flags=re.S)
s=s.replace('Math.Abs(price - 223800f) > 0.5f','Math.Abs(price - 49900f) > 0.5f').replace('Math.Abs(maxFuel - 64f) > 0.5f','Math.Abs(maxFuel - 80f) > 0.5f').replace('Math.Abs(maxSpeed - 296f) > 0.5f','Math.Abs(maxSpeed - 193f) > 0.5f').replace('Math.Abs(enginePower - 386f) > 0.5f','Math.Abs(enginePower - 165f) > 0.5f').replace('!luxury ||','luxury ||')
setup.write_text(s, encoding="utf-8")

runtime = DST / "Scripts/VolkswagenAmarokRuntime.cs"
r=runtime.read_text(encoding="utf-8")
for a,b in [
 ('private const float VehicleMass = 1450f;','private const float VehicleMass = 2078f;'),
 ('private const float EnginePowerKw = 386f;','private const float EnginePowerKw = 165f;'),
 ('private const float EngineIdleRpm = 900f;','private const float EngineIdleRpm = 725f;'),
 ('private const float EngineLimitRpm = 9000f;','private const float EngineLimitRpm = 4500f;'),
 ('private const float SpeedLimitKph = 296f;','private const float SpeedLimitKph = 193f;'),
 ('private const float FinalDriveRatio = 4.27f;','private const float FinalDriveRatio = 3.70f;'),
 ('private const float EngineInertia = 0.075f;','private const float EngineInertia = 0.16f;'),
 ('private const float TireFrictionCircleStrength = 1.02f;','private const float TireFrictionCircleStrength = 1.08f;'),
 ('private const float AntiRollBarForce = 9000f;','private const float AntiRollBarForce = 3800f;'),
 ('private const float FrontSuspensionTravel = 0.075f;','private const float FrontSuspensionTravel = 0.160f;'),
 ('private const float RearSuspensionTravel = 0.080f;','private const float RearSuspensionTravel = 0.180f;'),
 ('private const float FrontTireRadius = 0.35025f;','private const float FrontTireRadius = 0.382f;'),
 ('private const float RearTireRadius = 0.36720f;','private const float RearTireRadius = 0.382f;'),
 ('private const float FrontTireWidth = 0.275f;','private const float FrontTireWidth = 0.255f;'),
 ('private const float RearTireWidth = 0.335f;','private const float RearTireWidth = 0.255f;'),
 ('private const float DeformationStrength = 0.17f;','private const float DeformationStrength = 0.12f;'),
 ('private const float DeformationRadius = 0.24f;','private const float DeformationRadius = 0.30f;'),
 ('private const float DamageIntensity = 1f;','private const float DamageIntensity = 0.72f;'),
 ('private const float DamageDecelerationThreshold = 500f;','private const float DamageDecelerationThreshold = 650f;'),
 ('private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.08f, -0.28f);','private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.34f, -0.05f);'),
 ('bias.floatValue = 1f;','bias.floatValue = 0.60f;'),
]: r=r.replace(a,b)
r=re.sub(r'private static readonly float\[] AmarokGears =\s*\{.*?\};','''private static readonly float[] AmarokGears =\n    { -3.317f, 0f, 4.714f, 3.143f, 2.106f, 1.667f, 1.285f, 1.000f, 0.839f, 0.667f };''',r,flags=re.S)
r=re.sub(r'private static AnimationCurve CreateAmarokPowerCurve\(\) =>\s*new AnimationCurve\(.*?\);','''private static AnimationCurve CreateAmarokPowerCurve() => new AnimationCurve(\n        new Keyframe(0f,0f), new Keyframe(.16f,.18f), new Keyframe(.31f,.49f), new Keyframe(.44f,.70f),\n        new Keyframe(.61f,.96f), new Keyframe(.67f,1f), new Keyframe(.78f,.96f), new Keyframe(.89f,.88f), new Keyframe(1f,.70f));''',r,flags=re.S)
r=r.replace('transmission=7-speed-PDK, rwd=true','transmission=8-speed-automatic, awd=true').replace('official-flat-six-profile','V6-TDI-low-rpm-profile')
runtime.write_text(r,encoding="utf-8")

# Diesel audio model: no crackle and no sport-car pitch range.
a = DST / "Scripts/VolkswagenAmarokAudioModel.cs"
t=a.read_text(encoding="utf-8").replace('flat-six','V6 TDI').replace('naturally aspirated','turbo-diesel')
t=re.sub(r'internal const float CrackleIdleVolume = .*?;', 'internal const float CrackleIdleVolume = 0f;', t)
t=re.sub(r'internal const float CrackleLoadVolume = .*?;', 'internal const float CrackleLoadVolume = 0f;', t)
t=t.replace('internal static float ReferenceHz(int layer) => layer == 0 ? 70f : layer == 1 ? 220f : 420f;','internal static float ReferenceHz(int layer) => layer == 0 ? 48f : layer == 1 ? 95f : 165f;')
a.write_text(t,encoding="utf-8")

# Source notes and exact Blender vertex-group contract.
(DST/"Config/VEHICLE_SPECS.md").write_text('''# Volkswagen Amarok MY2017 V6 TDI\n\nPrice $49,900; 3.0 V6 TDI 2967 cm3; 165 kW / 224 PS; 550 Nm; permanent 4MOTION AWD; 8-speed automatic 4.714 / 3.143 / 2.106 / 1.667 / 1.285 / 1.000 / 0.839 / 0.667; reverse 3.317; final drive 3.70; 0-100 km/h measured target 8.0 s; 100-0 km/h braking target 36.7-37.0 m; top speed 193 km/h; 2078 kg; 80 L; 5.254 x 1.954 x 1.834 m; wheelbase 3.097 m; ground clearance 0.192 m; 255/60 R18; track 1.654 / 1.658 m.\n\nCargo bed remains visual only until a verified vanilla pickup/van storage path is wired.\n''',encoding='utf-8')
(DST/"ATTRIBUTION.md").write_text('''# Attribution\n\n2017 Volkswagen Amarok V6 by Ddiaz Design\nhttps://sketchfab.com/3d-models/2017-volkswagen-amarok-v6-3272be01e2f946c4bda1c1b5ed73d3a4\nLicense shown by the source: CC Attribution-NonCommercial-ShareAlike (CC BY-NC-SA). The listing also credits a Volkswagen 3D model / https://vk.com/3d_car_models.\n''',encoding='utf-8')
(DST/"Models/README.md").write_text('''Place the supplied files here before running Setup Volkswagen Amarok:\n- 2017_volkswagen_amarok_v6.glb\n- VolkswagenAmarokLightOverlays.blend\n\nBlender vertex groups supplied with the mod: BHeadlights, BDRL_Indicator_FL, BDRL_Indicator_FR, 1RearDrivingLights, 1BrakeLights, ThirdBrakeLight, ReverseLights, 1IndicatorRL, 1IndicatorRR. The two BDRL_Indicator groups intentionally drive both white DRL and amber indicator overlays.\n''',encoding='utf-8')

# Generate deterministic Amarok-specific diesel loops. Load clips add a subtle turbo whistle.
aud = DST/"Config/Audio"
if aud.exists(): shutil.rmtree(aud)
aud.mkdir(parents=True)
SR=44100; DUR=2; N=SR*DUR
def wav(name,freq,load=False,horn=False):
    rng=random.Random(4200+int(freq)+(1 if load else 0))
    samples=[]
    for i in range(N):
        x=i/SR
        if horn:
            v=.58*math.sin(2*math.pi*freq*x)+.24*math.sin(2*math.pi*freq*1.5*x)+.12*math.sin(2*math.pi*freq*2*x)
        else:
            v=.50*math.sin(2*math.pi*freq*x)+.24*math.sin(2*math.pi*2*freq*x)+.13*math.sin(2*math.pi*3*freq*x)+.06*math.sin(2*math.pi*.5*freq*x)
            if load: v+=.055*math.sin(2*math.pi*(620+freq*1.7)*x)+.025*math.sin(2*math.pi*(980+freq*2.1)*x)
            v+=(rng.random()*2-1)*(.025 if load else .018)
        samples.append(int(max(-.95,min(.95,v*.72))*32767))
    with wave.open(str(aud/(name+'.wav')),'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(struct.pack('<'+'h'*N,*samples))
for n,f in [('EngineLow',48),('EngineMid',95),('EngineHigh',165)]: wav(n,f); wav(n+'Load',f,True)
wav('HornLow',220,horn=True); wav('HornHigh',330,horn=True)
(aud/'generation.json').write_text(json.dumps({'engine':'3.0 V6 TDI','idle_rpm':725,'upper_rpm':4500,'turbo_whistle':'subtle load-layer component','pops_crackles':False},indent=2),encoding='utf-8')

print('Generated Assets/Mods/Volkswagen_Amarok from the current Porsche architecture.')
