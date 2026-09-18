from pathlib import Path
import math
import random
import re
import struct
import wave

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
DRIVER = MOD / "Scripts/VolkswagenAmarokDriverController.cs"
LIGHTING = MOD / "Scripts/VolkswagenAmarokLightingController.cs"
AUDIO_MODEL = MOD / "Scripts/VolkswagenAmarokAudioModel.cs"
PRIVATE_DRIVER = MOD / "Scripts/VolkswagenAmarokPrivateDriverSupport.cs"
AUDIO_DIR = MOD / "Config/Audio"

for path in (SETUP, RUNTIME, DRIVER, LIGHTING, AUDIO_MODEL, PRIVATE_DRIVER):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Body / wheels / gearbox / damage.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

# Split the difference between the original too-low -5.5 cm position and the
# second-feedback too-high -2.5 cm position.
setup, count = re.subn(
    r"private const float BodyVisualBottomY = [^;]+;",
    "private const float BodyVisualBottomY = -0.040f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok BodyVisualBottomY to -0.040 m.")

# One centimetre farther out than the real track-center baseline requested in
# the previous pass. A negative inset means outward on both left and right sides
# because the existing wheel-position formulas add it on the left and subtract
# it on the right.
setup, count = re.subn(
    r"private const float WheelInset = [^;]+;",
    "private const float WheelInset = -0.010f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok WheelInset to -0.010 m.")

# Existing-prefab feedback uses explicit wheel positions because the generated
# prefab may already predate the dictionary constants.
wheel_replacements = {
    "var frontLeftWheel = new Vector3(-FrontTrack * 0.5f, 0.306f, 1.733f);":
        "var frontLeftWheel = new Vector3(-FrontTrack * 0.5f - 0.010f, 0.306f, 1.733f);",
    "var frontRightWheel = new Vector3(FrontTrack * 0.5f, 0.306f, 1.733f);":
        "var frontRightWheel = new Vector3(FrontTrack * 0.5f + 0.010f, 0.306f, 1.733f);",
    "var rearLeftWheel = new Vector3(-RearTrack * 0.5f, 0.306f, -1.361f);":
        "var rearLeftWheel = new Vector3(-RearTrack * 0.5f - 0.010f, 0.306f, -1.361f);",
    "var rearRightWheel = new Vector3(RearTrack * 0.5f, 0.306f, -1.361f);":
        "var rearRightWheel = new Vector3(RearTrack * 0.5f + 0.010f, 0.306f, -1.361f);",
}
for old, new in wheel_replacements.items():
    setup = setup.replace(old, new)

# Keep the replacement idempotent if this patch has already run once.
setup = setup.replace(
    "-FrontTrack * 0.5f - 0.010f - 0.010f",
    "-FrontTrack * 0.5f - 0.010f",
).replace(
    "FrontTrack * 0.5f + 0.010f + 0.010f",
    "FrontTrack * 0.5f + 0.010f",
).replace(
    "-RearTrack * 0.5f - 0.010f - 0.010f",
    "-RearTrack * 0.5f - 0.010f",
).replace(
    "RearTrack * 0.5f + 0.010f + 0.010f",
    "RearTrack * 0.5f + 0.010f",
)

# Shift automatic downshifts up to the requested 2800 rpm.
setup = re.sub(
    r'SetRelativeNumber\(serialized, "powertrain\.transmission\._downshiftRPM", [^;]+;',
    'SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 2800f);',
    setup,
)

# Stronger visible deformation for a high-speed crash without turning the body
# into soft foil. Damage percentage itself is increased separately below.
setup, count = re.subn(
    r"private const float DeformationStrength = [^;]+;",
    "private const float DeformationStrength = 0.18f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok deformation strength to 0.18.")

# DamageHandler is the live collision-damage side; the VehicleType value controls
# the game's vehicle-condition damage scaling. Raise both slightly.
setup = setup.replace(
    'SetNumber(serialized, "damageIntensity", 0.72f);',
    'SetNumber(serialized, "damageIntensity", 0.76f);',
)

# Apply the updated DamageHandler during the existing-prefab build as well. The
# older feedback method only refreshed the deformation controller.
setup = setup.replace(
    "            ConfigureVehicleDeformation(root, damageBody);\n            ConfigurePowertrain(root);",
    "            ConfigureVehicleDeformation(root, damageBody);\n            ConfigurePickupDamageHandler(root);\n            ConfigurePowertrain(root);",
    1,
)

# Update the VehicleType asset during this feedback build so a pull + normal
# isolated build is sufficient; no full donor regeneration is required.
asset_marker = "        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);\n        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);"
asset_insert = '''        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var feedbackVehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath) ??
                                  throw new InvalidOperationException("Existing Amarok VehicleType asset is missing.");
        var feedbackVehicleSerialized = new SerializedObject(feedbackVehicleType);
        SetNumber(feedbackVehicleSerialized, "damageIntensity", 0.31f);
        feedbackVehicleSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feedbackVehicleType);

        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);'''
if "feedbackVehicleSerialized" not in setup:
    if asset_marker not in setup:
        raise SystemExit("Could not locate existing-prefab Amarok feedback asset-load marker.")
    setup = setup.replace(asset_marker, asset_insert, 1)

# ---------------------------------------------------------------------------
# Light-source root cause fix.
# The lamp GLB and the vehicle GLB are imported separately. The previous code
# copied the vehicle root's non-uniform scale/rotation onto the second GLB root,
# which means those values were interpreted in the wrong local axis system. That
# is why all lights stayed consistently displaced rather than merely needing a
# few centimetres of tuning. Make the overlay GLB a child of the already-
# normalized AmarokVisual and use identity local transform instead.
# ---------------------------------------------------------------------------
light_start = '            var lightSources = FindTransform(root.transform, "AmarokLightSources");\n'
light_end = "            // The visible outer shell is the root-level deformable body baked from\n"
light_start_index = setup.find(light_start)
light_end_index = setup.find(light_end, light_start_index)
if light_start_index < 0 or light_end_index < 0:
    raise SystemExit("Could not locate Amarok authored-light alignment block.")
light_block = '''            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.SetParent(visual, false);
                lightSources.localPosition = Vector3.zero;
                lightSources.localRotation = Quaternion.identity;
                lightSources.localScale = Vector3.one;
                Debug.Log(
                    "VolkswagenAmarok feedback light alignment: parented AmarokLightSources " +
                    "under normalized AmarokVisual with identity local transform.");
            }

'''
setup = setup[:light_start_index] + light_block + setup[light_end_index:]

# Keep diagnostic text current.
setup = setup.replace("downshiftRPM=1900.", "downshiftRPM=2800.")

save(SETUP, setup)


runtime = RUNTIME.read_text(encoding="utf-8")
runtime = re.sub(
    r'SetFloat\(transmission, "_downshiftRPM", [^;]+;',
    'SetFloat(transmission, "_downshiftRPM", 2800f);',
    runtime,
)
runtime, count = re.subn(
    r"private const float DeformationStrength = [^;]+;",
    "private const float DeformationStrength = 0.18f;",
    runtime,
    count=1,
)
if count != 1:
    raise SystemExit("Could not update Amarok runtime deformation strength.")
runtime = re.sub(
    r"private const float DamageIntensity = [^;]+;",
    "private const float DamageIntensity = 0.76f;",
    runtime,
    count=1,
)
if re.search(r"private const float WheelInset = [^;]+;", runtime):
    runtime = re.sub(
        r"private const float WheelInset = [^;]+;",
        "private const float WheelInset = -0.010f;",
        runtime,
        count=1,
    )
save(RUNTIME, runtime)


# ---------------------------------------------------------------------------
# Seated player: 10 cm lower from the last pass, another 2 degrees reclined.
# ---------------------------------------------------------------------------
driver = DRIVER.read_text(encoding="utf-8")
driver, count = re.subn(
    r"private static readonly Vector3 SeatOffset = new\([^;]+;",
    "private static readonly Vector3 SeatOffset = new(0f, -0.23f, -0.45f);",
    driver,
    count=1,
)
if count != 1:
    raise SystemExit("Could not lower Amarok seated-player offset.")
driver, count = re.subn(
    r"private const float SeatBackLeanDegrees = [^;]+;",
    "private const float SeatBackLeanDegrees = 17f;",
    driver,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok seated-player lean to 17 degrees.")
save(DRIVER, driver)


# ---------------------------------------------------------------------------
# Private-driver / NPC body fitment.
# Keep Gley/template wheel motion untouched. Only the Amarok body visuals get a
# small presentation correction: +2.5 cm at the centre plus -0.56 degrees X,
# which is approximately +4 cm at the front axle and +1 cm at the rear axle over
# the 3.097 m wheelbase requested by the user.
# ---------------------------------------------------------------------------
private_driver = PRIVATE_DRIVER.read_text(encoding="utf-8")
call_marker = "        VolkswagenAmarokMaterials.FixSolidMaterials(clone);\n"
if "ApplyNpcBodyFitment(clone);" not in private_driver:
    if call_marker not in private_driver:
        raise SystemExit("Could not locate Amarok private-driver material setup.")
    private_driver = private_driver.replace(
        call_marker,
        "        ApplyNpcBodyFitment(clone);\n" + call_marker,
        1,
    )

helper_marker = "    private static GameObject? LoadAiTemplate()\n"
helper = '''    private static void ApplyNpcBodyFitment(GameObject clone)
    {
        foreach (var name in new[] { "AmarokVisual", "AmarokDamageBody" })
        {
            var body = FindTransform(clone.transform, name);
            if (body == null)
                continue;

            body.localPosition += new Vector3(0f, 0.025f, 0f);
            body.localRotation = Quaternion.Euler(-0.56f, 0f, 0f) * body.localRotation;
        }
    }

'''
if "private static void ApplyNpcBodyFitment" not in private_driver:
    if helper_marker not in private_driver:
        raise SystemExit("Could not locate Amarok private-driver fitment helper insertion point.")
    private_driver = private_driver.replace(helper_marker, helper + helper_marker, 1)
save(PRIVATE_DRIVER, private_driver)


# ---------------------------------------------------------------------------
# Rear red lens cover + lighting runtime.
# The Blender overlay GLB intentionally contains geometry only, not source
# materials. The previous implementation turned the extracted red-lamp mesh into
# an emissive-only renderer, so when emission was off the rear lamp had no dark
# red lens surface. Add a non-emissive HDRP/Lit cover from the same authored mesh.
# ---------------------------------------------------------------------------
lighting = LIGHTING.read_text(encoding="utf-8")

field_marker = "    private MeshRenderer? rearTailOverlay;\n"
if "rearTailLensCover" not in lighting:
    if field_marker not in lighting:
        raise SystemExit("Could not locate Amarok rear-tail lighting field.")
    lighting = lighting.replace(
        field_marker,
        "    private MeshRenderer? rearTailLensCover;\n"
        "    private MeshRenderer? rearBrakeLensCover;\n"
        + field_marker,
        1,
    )

init_marker = 'rearTailOverlay=PrepareSourceOverlay(tail,"RearRunning",red,2.6f);'
if "RearTailLensCover" not in lighting:
    if init_marker not in lighting:
        raise SystemExit("Could not locate Amarok rear-tail overlay initialization.")
    lighting = lighting.replace(
        init_marker,
        'rearTailLensCover=CreateLensCover(tail,"RearTailLensCover",new Color(.34f,.012f,.008f,1f));\n'
        '        rearBrakeLensCover=CreateLensCover(brake,"RearBrakeLensCover",new Color(.30f,.010f,.007f,1f));\n'
        '        ' + init_marker,
        1,
    )

update_marker = "    private void Update()\n"
lens_helper = '''    private MeshRenderer? CreateLensCover(
        MeshRenderer? source,
        string suffix,
        Color color)
    {
        var sourceMesh = source?.GetComponent<MeshFilter>()?.sharedMesh;
        if (source == null || sourceMesh == null)
            return null;

        var host = new GameObject("VolkswagenAmarok_" + suffix);
        host.transform.SetParent(source.transform, false);
        host.transform.localScale = Vector3.one * 1.0015f;
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = sourceMesh;

        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit") ??
                     throw new InvalidOperationException("HDRP/Lit is unavailable for Amarok rear-lens cover.");
        var material = new Material(shader) { name = host.name + " Material" };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Metallic", 0f);
        SetFloat(material, "_Smoothness", .72f);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_ZWrite", 1f);

        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.enabled = true;
        generatedObjects.Add(host);
        generatedMaterials.Add(material);
        return renderer;
    }

'''
if "private MeshRenderer? CreateLensCover(" not in lighting:
    if update_marker not in lighting:
        raise SystemExit("Could not locate Amarok lighting Update method for lens helper.")
    lighting = lighting.replace(update_marker, lens_helper + update_marker, 1)

state_marker = "        SetEnabled(rearTailOverlay, lightsOn && !braking);\n"
if "SetEnabled(rearTailLensCover, true);" not in lighting:
    if state_marker not in lighting:
        raise SystemExit("Could not locate Amarok rear-tail ApplyState line.")
    lighting = lighting.replace(
        state_marker,
        "        SetEnabled(rearTailLensCover, true);\n"
        "        SetEnabled(rearBrakeLensCover, true);\n"
        + state_marker,
        1,
    )

save(LIGHTING, lighting)


# ---------------------------------------------------------------------------
# Audio: slightly quieter engine, substantially subtler turbo whistle, and a
# dual-tone automotive horn instead of the old low chime-like sine blend.
# ---------------------------------------------------------------------------
audio_model = AUDIO_MODEL.read_text(encoding="utf-8")
audio_model = re.sub(
    r"internal const float EngineBaseVolume = [^;]+;",
    "internal const float EngineBaseVolume = .50f;",
    audio_model,
    count=1,
)
audio_model = re.sub(
    r"internal const float EngineThrottleVolume = [^;]+;",
    "internal const float EngineThrottleVolume = .51f;",
    audio_model,
    count=1,
)
audio_model = re.sub(
    r"internal const float HornLowVolume = [^;]+;",
    "internal const float HornLowVolume = .82f;",
    audio_model,
    count=1,
)
audio_model = re.sub(
    r"internal const float HornHighVolume = [^;]+;",
    "internal const float HornHighVolume = .46f;",
    audio_model,
    count=1,
)
audio_model = re.sub(
    r"internal static float IdleVolume\(float drivingBlend\) =>\s*[^;]+;",
    "internal static float IdleVolume(float drivingBlend) =>\n        .51f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));",
    audio_model,
    count=1,
)
save(AUDIO_MODEL, audio_model)

AUDIO_DIR.mkdir(parents=True, exist_ok=True)
SAMPLE_RATE = 44100
DURATION = 2.0
SAMPLE_COUNT = int(SAMPLE_RATE * DURATION)


def write_mono_wav(path: Path, samples) -> None:
    values = [int(max(-0.97, min(0.97, value)) * 32767) for value in samples]
    with wave.open(str(path), "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(SAMPLE_RATE)
        wav_file.writeframes(struct.pack("<" + "h" * len(values), *values))


def make_load_layer(frequency: float):
    rng = random.Random(9200 + int(frequency))
    result = []
    for index in range(SAMPLE_COUNT):
        t = index / SAMPLE_RATE
        combustion = (
            .50 * math.sin(2 * math.pi * frequency * t)
            + .24 * math.sin(2 * math.pi * frequency * 2 * t)
            + .13 * math.sin(2 * math.pi * frequency * 3 * t)
            + .06 * math.sin(2 * math.pi * frequency * .5 * t)
        )
        # Keep the turbo present but well behind the V6 combustion layer. The
        # previous .022/.009 pair was still too prominent in-game.
        whistle = (
            .012 * math.sin(2 * math.pi * (620 + frequency * 1.7) * t)
            + .004 * math.sin(2 * math.pi * (980 + frequency * 2.1) * t)
        )
        noise = (rng.random() * 2 - 1) * .022
        result.append((combustion + whistle + noise) * .72)
    return result


for clip_name, frequency in (("EngineLowLoad", 48), ("EngineMidLoad", 95), ("EngineHighLoad", 165)):
    write_mono_wav(AUDIO_DIR / (clip_name + ".wav"), make_load_layer(frequency))


def make_automotive_horn(frequency: float, phase: float):
    result = []
    for index in range(SAMPLE_COUNT):
        t = index / SAMPLE_RATE
        # A diaphragm horn is a strong fundamental with gritty harmonics, not a
        # clean musical sine. Integer tones keep the 2-second looping clip seamless.
        raw = (
            .66 * math.sin(2 * math.pi * frequency * t + phase)
            + .23 * math.sin(2 * math.pi * frequency * 2 * t + .35 + phase)
            + .10 * math.sin(2 * math.pi * frequency * 3 * t + .75)
            + .045 * math.sin(2 * math.pi * frequency * 4 * t + 1.10)
        )
        diaphragm = math.tanh(raw * 1.55)
        slow_beating = .965 + .035 * math.sin(2 * math.pi * 6 * t)
        result.append(diaphragm * slow_beating * .58)
    return result


# Typical two-note road-horn spacing, deliberately not copied from any existing
# game vehicle recording.
write_mono_wav(AUDIO_DIR / "HornLow.wav", make_automotive_horn(410.0, 0.0))
write_mono_wav(AUDIO_DIR / "HornHigh.wav", make_automotive_horn(510.0, .17))


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------
checks = {
    SETUP: [
        "private const float BodyVisualBottomY = -0.040f;",
        "private const float WheelInset = -0.010f;",
        "private const float DeformationStrength = 0.18f;",
        '"powertrain.transmission._downshiftRPM", 2800f',
        'SetNumber(feedbackVehicleSerialized, "damageIntensity", 0.31f);',
        "ConfigurePickupDamageHandler(root);",
        "lightSources.SetParent(visual, false);",
        "lightSources.localRotation = Quaternion.identity;",
        "lightSources.localScale = Vector3.one;",
    ],
    RUNTIME: [
        'SetFloat(transmission, "_downshiftRPM", 2800f);',
        "private const float DeformationStrength = 0.18f;",
        "private const float DamageIntensity = 0.76f;",
    ],
    DRIVER: [
        "private static readonly Vector3 SeatOffset = new(0f, -0.23f, -0.45f);",
        "private const float SeatBackLeanDegrees = 17f;",
    ],
    PRIVATE_DRIVER: [
        "ApplyNpcBodyFitment(clone);",
        "new Vector3(0f, 0.025f, 0f)",
        "Quaternion.Euler(-0.56f, 0f, 0f)",
    ],
    LIGHTING: [
        "rearTailLensCover",
        "CreateLensCover(tail",
        "SetEnabled(rearTailLensCover, true);",
    ],
    AUDIO_MODEL: [
        "EngineBaseVolume = .50f;",
        "EngineThrottleVolume = .51f;",
        "HornLowVolume = .82f;",
        "HornHighVolume = .46f;",
        ".51f * (float)Math.Sqrt",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
for name in ("EngineLowLoad.wav", "EngineMidLoad.wav", "EngineHighLoad.wav", "HornLow.wav", "HornHigh.wav"):
    if not (AUDIO_DIR / name).is_file():
        missing.append("Config/Audio/" + name)
if missing:
    raise SystemExit("Amarok third in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Lowered the Amarok body 1.5 cm from the previous too-high visual position.")
print("Moved all four player/NPC wheel mounts 1.0 cm farther outward per side.")
print("Lowered the seated player another 10 cm and increased recline from 15 to 17 degrees.")
print("Adjusted private-driver body fitment to +4 cm front / +1 cm rear relative to its previous stance.")
print("Raised automatic downshift threshold to 2800 rpm.")
print("Reparented the authored light GLB under normalized AmarokVisual to remove transform-space offsets.")
print("Added persistent dark-red rear lens covers underneath the emissive rear-light layers.")
print("Reduced engine volume and regenerated load layers with a substantially quieter turbo whistle.")
print("Regenerated a two-tone diaphragm-style automotive horn at 410/510 Hz.")
print("Raised deformation strength to 0.18 and damage scaling slightly to 0.31 / 0.76.")
print("Volkswagen Amarok third in-game feedback patch preflight passed.")
