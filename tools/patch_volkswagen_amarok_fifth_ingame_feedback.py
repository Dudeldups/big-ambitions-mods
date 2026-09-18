from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"
PRIVATE_DRIVER = MOD / "Scripts/VolkswagenAmarokPrivateDriverSupport.cs"

for path in (SETUP, RUNTIME, MATERIALS, PRIVATE_DRIVER):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Gearbox + VehicleType.
# 2400 rpm still causes gear hunting after some part-throttle upshifts. Return
# to the previously stable 1900-rpm threshold while retaining the 4200-rpm
# upshift point and the real eight-speed ratios.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")
setup = re.sub(
    r'SetRelativeNumber\(serialized, "powertrain\.transmission\._downshiftRPM", [^;]+;',
    'SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1900f);',
    setup,
)
setup = setup.replace("downshiftRPM=2400.", "downshiftRPM=1900.")

# Amarok should not expose the vanilla automatic-parking action. Fix both full
# regeneration and the existing VehicleType asset used by the feedback build.
setup = re.sub(
    r'SetBool\(serialized, "autoParkSupported", (?:true|false)\);',
    'SetBool(serialized, "autoParkSupported", false);',
    setup,
)
feedback_damage = '        SetNumber(feedbackVehicleSerialized, "damageIntensity", 0.31f);\n'
feedback_autopark = feedback_damage + '        SetBool(feedbackVehicleSerialized, "autoParkSupported", false);\n'
if 'SetBool(feedbackVehicleSerialized, "autoParkSupported", false);' not in setup:
    if feedback_damage not in setup:
        raise SystemExit("Could not locate Amarok existing VehicleType feedback block for auto-parking.")
    setup = setup.replace(feedback_damage, feedback_autopark, 1)


# ---------------------------------------------------------------------------
# Lights.
# The user has now confirmed the remaining failure mode: every authored lamp is
# oriented upward and needs one fixed 90-degree rotation toward vehicle forward.
# Do not re-score/guess the axis again. Keep the corrected parenting/scale from
# the previous passes and author exactly +90 degrees around local X.
# ---------------------------------------------------------------------------
light_pattern = re.compile(
    r'''            var lightSources = FindTransform\(root\.transform, "AmarokLightSources"\);\n'''
    r'''            if \(lightSources != null\)\n'''
    r'''            \{.*?\n'''
    r'''            \}\n\n'''
    r'''            // The visible outer shell''',
    re.S,
)
light_block = '''            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.SetParent(visual, false);
                lightSources.localPosition = Vector3.zero;
                lightSources.localScale = Vector3.one;
                // Confirmed in-game correction: the exported lamp set points
                // upward in AmarokVisual local space. Rotate the complete set
                // exactly 90 degrees toward vehicle forward.
                lightSources.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Debug.Log(
                    $"VolkswagenAmarok authored-light fixed forward rotation=" +
                    $"{lightSources.localEulerAngles}.");
            }

            // The visible outer shell'''
setup, light_count = light_pattern.subn(light_block, setup, count=1)
if light_count != 1:
    raise SystemExit("Could not replace Amarok authored-light orientation with fixed +90-degree rotation.")


# ---------------------------------------------------------------------------
# Factory-black exterior accessories.
# The Amarok source shares phong5/dorr_R paint materials with several accessories,
# so material-name checks alone are insufficient. Explicitly classify lower
# running boards/tubes, the rear bumper step surface, and wheel splash guards by
# hierarchy names plus conservative geometry tests.
# ---------------------------------------------------------------------------
factory_helper = '''    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
    {
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("mudflap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mud_flap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("splash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("spritz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("schmutz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("bryzg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("runningboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("running_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("running board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sidestep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side_step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("footboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foot_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("nerf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("step_tube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side_tube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(name, "extra1", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var visual = FindTransform(renderer.transform.root, "AmarokVisual");
        if (visual == null)
            return false;

        var center = visual.InverseTransformPoint(renderer.bounds.center);
        var size = renderer.bounds.size;

        // Narrow vertical guards immediately behind the four wheels.
        var splashGuard =
            Mathf.Abs(center.x) > 0.68f &&
            Mathf.Abs(center.z) > 0.85f &&
            center.y < 0.72f &&
            size.x < 0.58f && size.y < 0.90f && size.z < 0.65f;

        // Long low tubes / tread boards below the doors.
        var sideStep =
            Mathf.Abs(center.x) > 0.62f &&
            Mathf.Abs(center.z) < 1.55f &&
            center.y < 0.62f &&
            size.z > 1.05f &&
            size.y < 0.42f;

        // Thin tread surface across/around the rear bumper. Only body-paint
        // material slots on this renderer are recolored later, so chrome/silver
        // bumper material remains untouched.
        var rearStep =
            center.z < -1.95f &&
            center.y < 0.72f &&
            size.y < 0.48f;

        return splashGuard || sideStep || rearStep;
    }'''

setup_factory_pattern = re.compile(
    r'''    private static bool IsFactoryBlackExteriorPart\(Renderer renderer\)\s*\{.*?\n    \}\n\n'''
    r'''    private static bool IsBodyPaintMaterial''',
    re.S,
)
setup, setup_factory_count = setup_factory_pattern.subn(
    factory_helper + "\n\n    private static bool IsBodyPaintMaterial",
    setup,
    count=1,
)
if setup_factory_count != 1:
    raise SystemExit("Could not replace Amarok setup factory-black accessory classifier.")

save(SETUP, setup)


# Runtime gearbox must match the prefab.
runtime = RUNTIME.read_text(encoding="utf-8")
runtime = re.sub(
    r'SetFloat\(transmission, "_downshiftRPM", [^;]+;',
    'SetFloat(transmission, "_downshiftRPM", 1900f);',
    runtime,
)
save(RUNTIME, runtime)


# ---------------------------------------------------------------------------
# NPC / private-driver presentation.
# The complete NPC Amarok still sits ~3 cm below the road. The fourth pass
# already moves every Amarok-owned visual root +3 cm; make that +6 cm while
# retaining its body-vs-wheel pitch correction.
# ---------------------------------------------------------------------------
private_driver = PRIVATE_DRIVER.read_text(encoding="utf-8")
private_driver, npc_raise_count = re.subn(
    r'visual\.localPosition \+= new Vector3\(0f, 0\.030f, 0f\);',
    'visual.localPosition += new Vector3(0f, 0.060f, 0f);',
    private_driver,
    count=1,
)
if npc_raise_count != 1:
    raise SystemExit("Could not raise complete Amarok NPC/private-driver visuals by another 3 cm.")
save(PRIVATE_DRIVER, private_driver)


# ---------------------------------------------------------------------------
# Paint controller factory-black classifier. Keep the same classification in the
# runtime paint path and the editor/CarFeatures body-mesh path.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")

materials_factory_helper = factory_helper.replace(
    'var visual = FindTransform(renderer.transform.root, "AmarokVisual");\n'
    '        if (visual == null)\n'
    '            return false;',
    '''Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null)
            return false;'''
)

materials_factory_pattern = re.compile(
    r'''    private static bool IsFactoryBlackExteriorPart\(Renderer renderer\)\s*\{.*?\n    \}\n\n'''
    r'''    private void ApplyFactoryBlackExteriorParts''',
    re.S,
)
materials, materials_factory_count = materials_factory_pattern.subn(
    materials_factory_helper + "\n\n    private void ApplyFactoryBlackExteriorParts",
    materials,
    count=1,
)
if materials_factory_count != 1:
    raise SystemExit("Could not replace Amarok runtime factory-black accessory classifier.")

save(MATERIALS, materials)


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------
checks = {
    SETUP: [
        '"powertrain.transmission._downshiftRPM", 1900f',
        'SetBool(feedbackVehicleSerialized, "autoParkSupported", false);',
        'SetBool(serialized, "autoParkSupported", false);',
        'lightSources.localRotation = Quaternion.Euler(90f, 0f, 0f);',
        'name.IndexOf("runningboard"',
        'name.IndexOf("sidestep"',
        'var rearStep =',
        'var sideStep =',
        'var splashGuard =',
    ],
    RUNTIME: [
        'SetFloat(transmission, "_downshiftRPM", 1900f);',
    ],
    PRIVATE_DRIVER: [
        'visual.localPosition += new Vector3(0f, 0.060f, 0f);',
    ],
    MATERIALS: [
        'name.IndexOf("runningboard"',
        'name.IndexOf("sidestep"',
        'var rearStep =',
        'var sideStep =',
        'var splashGuard =',
        'ApplyFactoryBlackExteriorParts();',
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok fifth in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Returned Amarok automatic downshift threshold to the stable 1900 rpm value.")
print("Disabled VehicleType auto-parking for both current feedback builds and future regeneration.")
print("Forced the complete authored lamp set to a fixed +90-degree forward rotation.")
print("Expanded factory-black paint exclusions to side steps/tubes, rear tread step and splash guards.")
print("Raised the complete NPC/private-driver Amarok presentation another 3 cm.")
print("Volkswagen Amarok fifth in-game feedback preflight passed.")
