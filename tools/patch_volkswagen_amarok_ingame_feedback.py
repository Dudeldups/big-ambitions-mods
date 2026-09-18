from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
DRIVER = MOD / "Scripts/VolkswagenAmarokDriverController.cs"
LIGHTING = MOD / "Scripts/VolkswagenAmarokLightingController.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"
AUDIO_MODEL = MOD / "Scripts/VolkswagenAmarokAudioModel.cs"

for path in (SETUP, RUNTIME, DRIVER, LIGHTING, MATERIALS, AUDIO_MODEL):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Visual proportions / ride height / gearbox.
# 1.954 m is the body width without mirrors. The supplied model bounds include
# the mirrors (~2.228 m), so forcing the complete GLB to 1.954 m visibly
# squeezed the vehicle in X while leaving height correct.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

if "private const float VisualTargetWidth" not in setup:
    marker = "    private const float TargetWidth = 1.954f;\n"
    if marker not in setup:
        raise SystemExit("Could not locate TargetWidth in VolkswagenAmarokSetup.cs.")
    setup = setup.replace(
        marker,
        marker
        + "    // Width including mirrors; used only for the imported visual.\n"
        + "    private const float VisualTargetWidth = 2.228f;\n"
        + "    // Lower the body relative to the already-correct wheel centers.\n"
        + "    private const float BodyVisualBottomY = -0.055f;\n",
        1,
    )

setup = setup.replace("TargetWidth / bounds.size.x", "VisualTargetWidth / bounds.size.x")

body_center_line = (
    "        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);\n"
)
body_drop_line = body_center_line + (
    "        model.transform.position += new Vector3(0f, BodyVisualBottomY, 0f);\n"
)
if "BodyVisualBottomY, 0f" not in setup:
    if body_center_line not in setup:
        raise SystemExit("Could not locate Amarok visual ground-normalization line.")
    setup = setup.replace(body_center_line, body_drop_line, 1)

setup = re.sub(
    r'SetRelativeNumber\(serialized, "powertrain\.transmission\._downshiftRPM", [^;]+;',
    'SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1900f);',
    setup,
)

# The isolated builder can regenerate the asset/prefab before creating the bundle.
# AudiRS6R remains available as the generic donor during that isolated run.
if "RegenerateAndBuildStandaloneWindowsAssetBundle" not in setup:
    marker = '    [MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]\n'
    if marker not in setup:
        raise SystemExit("Could not locate Amarok standalone bundle-build menu method.")
    wrapper = '''    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()\n    {\n        Generate();\n        BuildStandaloneWindowsAssetBundle();\n    }\n\n'''
    setup = setup.replace(marker, wrapper + marker, 1)

save(SETUP, setup)


# Runtime must not restore the old 1500-rpm downshift threshold after spawning.
runtime = RUNTIME.read_text(encoding="utf-8")
runtime = re.sub(
    r'SetFloat\(transmission, "_downshiftRPM", [^;]+;',
    'SetFloat(transmission, "_downshiftRPM", 1900f);',
    runtime,
)
save(RUNTIME, runtime)


# ---------------------------------------------------------------------------
# Driver placement.
# The glTF steering object uses a mesh whose visible center is not guaranteed to
# equal the Transform pivot. Using the pivot put the cloned player near the rear
# bench. Anchor seat and hand placement to the steering renderer bounds instead.
# Keep the real seat behind the wheel, while raising the hips by 20 cm.
# ---------------------------------------------------------------------------
driver = DRIVER.read_text(encoding="utf-8")
driver = re.sub(
    r"private static readonly Vector3 SeatOffset = new\([^;]+;",
    "private static readonly Vector3 SeatOffset = new(0f, -0.10f, -0.42f);",
    driver,
    count=1,
)

field_marker = "    private Transform? steeringWheel;\n"
if "steeringWheelCenterLocal" not in driver:
    if field_marker not in driver:
        raise SystemExit("Could not locate Amarok driver steering-wheel field.")
    driver = driver.replace(
        field_marker,
        field_marker + "    private Vector3 steeringWheelCenterLocal;\n",
        1,
    )

missing_check = '''        if (steeringWheel == null)
            throw new InvalidOperationException("Amarok steering-wheel seat reference is missing.");
'''
center_block = missing_check + '''        var steeringRenderer = steeringWheel.GetComponentInChildren<Renderer>(true);
        steeringWheelCenterLocal = steeringRenderer != null
            ? vehicle!.transform.InverseTransformPoint(steeringRenderer.bounds.center)
            : vehicle!.transform.InverseTransformPoint(steeringWheel.position);
'''
if "steeringRenderer = steeringWheel.GetComponentInChildren<Renderer>" not in driver:
    if missing_check not in driver:
        raise SystemExit("Could not locate Amarok driver steering-wheel validation.")
    driver = driver.replace(missing_check, center_block, 1)

driver = driver.replace(
    "        var seatPosition = steeringWheel.position + vehicle.transform.TransformVector(SeatOffset);",
    "        var seatPosition = vehicle.transform.TransformPoint(steeringWheelCenterLocal + SeatOffset);",
)
driver = driver.replace(
    "        var wheelCenter = vehicle.transform.InverseTransformPoint(steeringWheel.position);",
    "        var wheelCenter = steeringWheelCenterLocal;",
)
driver = driver.replace(
    '                $"steeringWheel={VehiclePosition(steeringWheel)}.");',
    '                $"steeringWheelCenter={steeringWheelCenterLocal}.");',
)
save(DRIVER, driver)


# ---------------------------------------------------------------------------
# Repaint.
# phong5/dorr_R carry the source model's blue diffuse texture. A property-block
# tint multiplies that texture, so every requested paint ended up as a light or
# dark blue. For paintable body materials keep normal/metal/roughness data but
# remove only the blue base-color texture before the selected VehicleColor tint
# is applied.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")
fix_signature = "    internal static bool FixSolidHdrpMaterial(Material material)\n    {\n"
paint_neutralizer = '''    private static void NeutralizeAmarokBodyPaintTexture(Material material)
    {
        var name = material.name;
        if (name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) < 0 &&
            name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
        if (material.HasProperty("baseColorTexture")) material.SetTexture("baseColorTexture", null);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
    }

'''
if "NeutralizeAmarokBodyPaintTexture" not in materials:
    if fix_signature not in materials:
        raise SystemExit("Could not locate Amarok HDRP solid-material fixer.")
    materials = materials.replace(
        fix_signature,
        paint_neutralizer
        + fix_signature
        + "        NeutralizeAmarokBodyPaintTexture(material);\n",
        1,
    )
elif "NeutralizeAmarokBodyPaintTexture(material);" not in materials:
    materials = materials.replace(
        fix_signature,
        fix_signature + "        NeutralizeAmarokBodyPaintTexture(material);\n",
        1,
    )
save(MATERIALS, materials)


# ---------------------------------------------------------------------------
# Lights.
# Use the exported Blender overlay meshes themselves for unique lamp surfaces;
# this avoids placing another copied mesh on top of a disabled imported renderer.
# The shared front DRL/indicator surfaces retain a second amber copy so white DRL
# can switch off during the amber flash. Also resolve by mesh name as a fallback
# because glTF importers can alter hierarchy object names.
# ---------------------------------------------------------------------------
lighting = LIGHTING.read_text(encoding="utf-8")

replacements = {
    'headlampOverlay=CreateOverlay(head,"Headlights",white,5.8f,1.006f);':
        'headlampOverlay=PrepareSourceOverlay(head,"Headlights",white,5.8f);',
    'daylightOverlay=CreateOverlay(fl,"DRLLeft",white,3.8f,1.006f);':
        'daylightOverlay=PrepareSourceOverlay(fl,"DRLLeft",white,3.8f);',
    'daylightOverlayRight=CreateOverlay(fr,"DRLRight",white,3.8f,1.006f);':
        'daylightOverlayRight=PrepareSourceOverlay(fr,"DRLRight",white,3.8f);',
    'rearTailOverlay=CreateOverlay(tail,"RearRunning",red,2.6f,1.006f);':
        'rearTailOverlay=PrepareSourceOverlay(tail,"RearRunning",red,2.6f);',
    'rearBrakeOverlay=CreateOverlay(brake,"RearBrake",red,4.6f,1.009f);':
        'rearBrakeOverlay=PrepareSourceOverlay(brake,"RearBrake",red,4.6f);',
    'thirdBrakeOverlay=CreateOverlay(third,"ThirdBrake",red,4.6f,1.009f);':
        'thirdBrakeOverlay=PrepareSourceOverlay(third,"ThirdBrake",red,4.6f);',
    'reverseOverlay=CreateOverlay(reverse,"Reverse",white,4.4f,1.008f);':
        'reverseOverlay=PrepareSourceOverlay(reverse,"Reverse",white,4.4f);',
    'rearLeftBlinkerOverlay=CreateOverlay(rl,"RearIndicatorLeft",amber,5.2f,1.009f);':
        'rearLeftBlinkerOverlay=PrepareSourceOverlay(rl,"RearIndicatorLeft",amber,5.2f);',
    'rearRightBlinkerOverlay=CreateOverlay(rr,"RearIndicatorRight",amber,5.2f,1.009f);':
        'rearRightBlinkerOverlay=PrepareSourceOverlay(rr,"RearIndicatorRight",amber,5.2f);',
    'sideLeftBlinkerOverlay=CreateOverlay(sideL,"SideIndicatorLeft",amber,5.0f,1.008f);':
        'sideLeftBlinkerOverlay=PrepareSourceOverlay(sideL,"SideIndicatorLeft",amber,5.0f);',
    'sideRightBlinkerOverlay=CreateOverlay(sideR,"SideIndicatorRight",amber,5.0f,1.008f);':
        'sideRightBlinkerOverlay=PrepareSourceOverlay(sideR,"SideIndicatorRight",amber,5.0f);',
}
for old, new in replacements.items():
    lighting = lighting.replace(old, new)

update_marker = "    private void Update()\n"
source_helper = '''    private MeshRenderer? PrepareSourceOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"Amarok authored light source '{suffix}' is missing.");
            return null;
        }

        source.sharedMaterial = CreateUnlitMaterial("VolkswagenAmarok_" + suffix + " Material", color, intensity);
        source.shadowCastingMode = ShadowCastingMode.Off;
        source.receiveShadows = false;
        source.enabled = false;
        return source;
    }

'''
if "PrepareSourceOverlay(" not in lighting.split(update_marker)[0]:
    if update_marker not in lighting:
        raise SystemExit("Could not locate Amarok lighting Update method.")
    lighting = lighting.replace(update_marker, source_helper + update_marker, 1)

# Replace hierarchy-only lookup with hierarchy/name/mesh-name lookup.
lookup_pattern = re.compile(
    r"    private static MeshRenderer\? FindRendererByHierarchy\(\s*"
    r"IEnumerable<MeshRenderer> renderers,\s*string hierarchyMarker\)\s*"
    r"\{.*?\n    \}",
    re.S,
)
lookup_replacement = '''    private static MeshRenderer? FindRendererByHierarchy(
        IEnumerable<MeshRenderer> renderers,
        string hierarchyMarker)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (HasAncestor(renderer.transform, hierarchyMarker) ||
                renderer.name.IndexOf(hierarchyMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
            var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? string.Empty;
            if (meshName.IndexOf(hierarchyMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
        }
        return null;
    }'''
lighting, lookup_count = lookup_pattern.subn(lookup_replacement, lighting, count=1)
if lookup_count != 1:
    raise SystemExit("Could not replace Amarok light renderer lookup.")

# DRL is independent of low-beam/night lighting. Match the finished Cadillac
# implementation: white DRL is on whenever the player controls the vehicle and
# stays off for the full duration of the corresponding turn signal.
for old in (
    "        SetEnabled(daylightOverlay, lightsOn);",
    "        SetEnabled(daylightOverlay, lightsOn && !(leftBlinker && flash));",
):
    lighting = lighting.replace(
        old,
        "        SetEnabled(daylightOverlay, controlled && !leftBlinker);",
    )
for old in (
    "        SetEnabled(daylightOverlayRight, lightsOn);",
    "        SetEnabled(daylightOverlayRight, lightsOn && !(rightBlinker && flash));",
):
    lighting = lighting.replace(
        old,
        "        SetEnabled(daylightOverlayRight, controlled && !rightBlinker);",
    )

# Add one useful runtime diagnostic for the next in-game pass.
diag_marker = "        ConfigureHeadlightBeams(); initialized=true; ApplyState();\n"
diag_insert = '''        LogInfo($"overlay sources head={head?.name ?? "<missing>"} fl={fl?.name ?? "<missing>"} fr={fr?.name ?? "<missing>"} " +
                $"tail={tail?.name ?? "<missing>"} brake={brake?.name ?? "<missing>"} third={third?.name ?? "<missing>"} " +
                $"reverse={reverse?.name ?? "<missing>"} rl={rl?.name ?? "<missing>"} rr={rr?.name ?? "<missing>"} " +
                $"sideL={sideL?.name ?? "<missing>"} sideR={sideR?.name ?? "<missing>"}.");
        ConfigureHeadlightBeams(); initialized=true; ApplyState();
'''
if "overlay sources head=" not in lighting:
    if diag_marker not in lighting:
        raise SystemExit("Could not locate Amarok lighting initialization tail.")
    lighting = lighting.replace(diag_marker, diag_insert, 1)

save(LIGHTING, lighting)


# ---------------------------------------------------------------------------
# Engine audio.
# The previous model retained the Porsche 38->335 Hz target curve while merely
# changing layer references, pushing the upper layers to ~2x pitch. Use a diesel
# combustion-frequency range (36->210 Hz) and raise level without changing the
# already-acceptable idle character.
# ---------------------------------------------------------------------------
audio = AUDIO_MODEL.read_text(encoding="utf-8")
audio = re.sub(
    r"internal const float EngineBaseVolume = [^;]+;",
    "internal const float EngineBaseVolume = .58f;",
    audio,
    count=1,
)
audio = re.sub(
    r"internal const float EngineThrottleVolume = [^;]+;",
    "internal const float EngineThrottleVolume = .60f;",
    audio,
    count=1,
)
audio = re.sub(
    r"internal static float IdleVolume\(float drivingBlend\) =>\s*"
    r"[^;]+;",
    "internal static float IdleVolume(float drivingBlend) =>\n        .60f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));",
    audio,
    count=1,
)
audio, target_count = re.subn(
    r"    internal static float TargetHz\(float normalized\)\s*\{.*?\n    \}",
    '''    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        return (float)(36d * Math.Pow(210d / 36d, position));
    }''',
    audio,
    count=1,
    flags=re.S,
)
if target_count != 1:
    raise SystemExit("Could not replace Amarok diesel TargetHz curve.")
save(AUDIO_MODEL, audio)


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------
checks = {
    SETUP: [
        "private const float VisualTargetWidth = 2.228f;",
        "private const float BodyVisualBottomY = ",
        "VisualTargetWidth / bounds.size.x",
        "BodyVisualBottomY, 0f",
        '"powertrain.transmission._downshiftRPM", 1900f',
        "RegenerateAndBuildStandaloneWindowsAssetBundle",
    ],
    RUNTIME: ['"_downshiftRPM", 1900f'],
    DRIVER: [
        "steeringWheelCenterLocal",
        "GetComponentInChildren<Renderer>(true)",
        "TransformPoint(steeringWheelCenterLocal + SeatOffset)",
        "private static readonly Vector3 SeatOffset = new(0f, -0.10f, -0.42f);",
    ],
    MATERIALS: [
        "NeutralizeAmarokBodyPaintTexture",
        'material.SetTexture("_BaseColorMap", null)',
    ],
    LIGHTING: [
        "PrepareSourceOverlay(head",
        "sharedMesh?.name",
        "controlled && !leftBlinker",
        "controlled && !rightBlinker",
        "overlay sources head=",
    ],
    AUDIO_MODEL: [
        "EngineBaseVolume = .58f;",
        "EngineThrottleVolume = .60f;",
        "36d * Math.Pow(210d / 36d, position)",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Patched Amarok visual width to 2.228 m including mirrors while keeping 1.954 m physical body width.")
print("Confirmed Amarok body-height tuning constant is present; later feedback passes may override its value.")
print("Raised downshift threshold from 1500 to 1900 rpm in setup and runtime.")
print("Anchored the seated player to the visible steering-wheel center and raised the seat by 20 cm.")
print("Neutralized the source blue body diffuse map so the selected VehicleColor is applied directly.")
print("Reworked authored light overlays to use their source meshes and added mesh-name fallback diagnostics.")
print("Retuned driving audio to a lower V6 TDI pitch range and raised engine/idle volume.")
print("Volkswagen Amarok first in-game feedback patch preflight passed.")