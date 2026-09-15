#!/usr/bin/env python3
"""Static checks for the Ferrari SF90 Spider source folder before opening Unity."""
from __future__ import annotations
import json, re, struct, sys, wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    'FerrariSF90Spider.asmdef',
    'Editor/FerrariSF90Spider.Editor.asmdef',
    'Editor/FerrariSF90SpiderSetup.cs',
    'Scripts/FerrariSF90SpiderMod.cs',
    'Scripts/FerrariSF90SpiderRuntime.cs',
    'Locales/en.json',
    'Models/2021_ferrari_sf90_spider.glb',
    'Config/Audio/EngineLow.wav', 'Config/Audio/EngineLowLoad.wav',
    'Config/Audio/EngineMid.wav', 'Config/Audio/EngineMidLoad.wav',
    'Config/Audio/EngineHigh.wav', 'Config/Audio/EngineHighLoad.wav',
    'Config/Audio/HornLow.wav', 'Config/Audio/HornHigh.wav',
]

def fail(msg: str) -> None:
    print('FAIL:', msg)
    raise SystemExit(1)

def glb_json(path: Path) -> dict:
    raw = path.read_bytes()
    if len(raw) < 20 or raw[:4] != b'glTF':
        fail('model is not a GLB')
    version, total = struct.unpack_from('<II', raw, 4)
    if version != 2 or total != len(raw):
        fail(f'GLB header mismatch version={version} declared={total} actual={len(raw)}')
    chunk_len, chunk_type = struct.unpack_from('<II', raw, 12)
    if chunk_type != 0x4E4F534A:
        fail('first GLB chunk is not JSON')
    return json.loads(raw[20:20+chunk_len].decode('utf-8').rstrip('\x00 \t\r\n'))

for rel in REQUIRED:
    if not (ROOT / rel).is_file():
        fail(f'missing required file: {rel}')

runtime_text = '\n'.join(p.read_text(encoding='utf-8', errors='ignore') for p in (ROOT/'Scripts').glob('*.cs'))
for foreign in ('Koenigsegg', 'Jesko', 'Cadillac', 'Escalade', 'Lamborghini', 'Revuelto', 'AudiRS6R'):
    if foreign.lower() in runtime_text.lower():
        fail(f'foreign vehicle name leaked into runtime scripts: {foreign}')
if 'SetInt(transmission, "forwardGearCount", 8)' not in runtime_text:
    fail('runtime 8-speed transmission assignment not found')

asm = json.loads((ROOT/'FerrariSF90Spider.asmdef').read_text(encoding='utf-8'))
if asm.get('name') != 'FerrariSF90Spider':
    fail('runtime asmdef name is not FerrariSF90Spider')
locale = json.loads((ROOT/'Locales/en.json').read_text(encoding='utf-8'))
if locale.get('ferrarisf90spider-vehicle:vehicletype_ferrarisf90spider') != 'Ferrari SF90 Spider':
    fail('vehicle locale key/value mismatch')

gltf = glb_json(ROOT/'Models/2021_ferrari_sf90_spider.glb')
asset = gltf.get('asset', {})
extras = asset.get('extras', {}) or {}
flat = json.dumps(extras, sort_keys=True).lower()
if 'ddiaz' not in flat or 'cc-by-4.0' not in flat:
    fail('expected Ddiaz Design / CC-BY-4.0 metadata not found in supplied GLB')
materials = [m.get('name','') for m in gltf.get('materials', [])]
for marker in ('2021Paint_Material', 'Window_Material', 'LightA_Material', 'CallipersCalliperA_Zone_Material'):
    if not any(marker.lower() in name.lower() for name in materials):
        fail(f'expected model material marker missing: {marker}')

for wav in sorted((ROOT/'Config/Audio').glob('*.wav')):
    with wave.open(str(wav), 'rb') as w:
        if (w.getnchannels(), w.getsampwidth(), w.getframerate()) != (1, 2, 44100):
            fail(f'unsupported WAV format for {wav.name}: channels={w.getnchannels()} width={w.getsampwidth()} rate={w.getframerate()}')
        if w.getnframes() > 44100 * 5:
            fail(f'WAV exceeds runtime 5-second parser limit: {wav.name}')

# Unity meta GUID uniqueness inside this mod.
guids = {}
for meta in ROOT.rglob('*.meta'):
    match = re.search(r'^guid:\s*(\w+)', meta.read_text(encoding='utf-8', errors='ignore'), re.M)
    if not match:
        fail(f'meta without guid: {meta.relative_to(ROOT)}')
    guid = match.group(1)
    if guid in guids:
        fail(f'duplicate Unity GUID: {guid} in {guids[guid]} and {meta.relative_to(ROOT)}')
    guids[guid] = meta.relative_to(ROOT)

print('PASS: Ferrari SF90 Spider pre-Unity static validation')
print(f'  runtime scripts: {len(list((ROOT/"Scripts").glob("*.cs")))}')
print(f'  Unity meta GUIDs: {len(guids)} unique')
print(f'  audio WAVs: {len(list((ROOT/"Config/Audio").glob("*.wav")))} mono 44.1kHz PCM')
print(f'  GLB nodes={len(gltf.get("nodes", []))} meshes={len(gltf.get("meshes", []))} materials={len(gltf.get("materials", []))}')
print('  note: actual C# compilation, prefab serialization and AssetBundle build still require Unity/project toolchain')
