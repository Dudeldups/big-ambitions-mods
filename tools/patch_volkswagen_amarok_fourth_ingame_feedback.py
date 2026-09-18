from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
DRIVER = MOD / "Scripts/VolkswagenAmarokDriverController.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"
PRIVATE_DRIVER = MOD / "Scripts/VolkswagenAmarokPrivateDriverSupport.cs"

for path in (SETUP, RUNTIME, DRIVER, MATERIALS, PRIVATE_DRIVER):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


def set_vector3_constant(text: str, name: str, value: str) -> str:
    pattern = re.compile(
        rf"private static readonly Vector3 {re.escape(name)} =\s*new Vector3\([^;]+;",
        re.S,
    )
    updated, count = pattern.subn(
        f"private static readonly Vector3 {name} =\n        new Vector3({value});",
        text,
        count=1,
    )
    if count != 1:
        raise SystemExit(f"Could not set {name}.")
    return updated


# ---------------------------------------------------------------------------
# Player prefab: body height, gearbox, hitboxes and authored-light axis.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

# The last screenshot still leaves too much arch gap. Move only the visible body
# (and its child light sources) down another 3 cm; wheel/contact geometry stays put.
setup, count = re.subn(
    r"private const float BodyVisualBottomY = [^;]+;",
    "private const float BodyVisualBottomY = -0.070f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not lower Amarok BodyVisualBottomY to -0.070 m.")

# 2800 rpm caused immediate kick-down after an upshift. 2400 keeps the diesel in
# its usable band without fighting the automatic gearbox after each shift.
setup = re.sub(
    r'SetRelativeNumber\(serialized, "powertrain\.transmission\._downshiftRPM", [^;]+;',
    'SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 2400f);',
    setup,
)
setup = setup.replace("downshiftRPM=2800.", "downshiftRPM=2400.")

# The Amarok is 5.254 m long. The inherited Porsche front/rear contact boxes stop
# noticeably short of the bumpers, allowing the player capsule to walk into both
# ends. Extend only the end caps to the measured vehicle length.
setup = set_vector3_constant(setup, "FrontContactColliderCenter", "0f, 0.46f, 2.08f")
setup = set_vector3_constant(setup, "FrontContactColliderSize", "1.86f, 0.72f, 1.14f")
setup = set_vector3_constant(setup, "RearContactColliderCenter", "0f, 0.46f, -2.08f")
setup = set_vector3_constant(setup, "RearContactColliderSize", "1.86f, 0.72f, 1.14f")

# Existing-prefab feedback previously refreshed deformation/powertrain only.
# Re-author the collider values too so the bundle itself contains the corrected
# front/rear end caps before runtime performs its matching safety pass.
if "ConfigureBodyColliders(root);" not in setup.split("ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle", 1)[-1]:
    marker = "            ConfigurePickupDamageHandler(root);\n            ConfigurePowertrain(root);"
    if marker not in setup:
        raise SystemExit("Could not locate Amarok existing-prefab collider insertion point.")
    setup = setup.replace(
        marker,
        "            ConfigurePickupDamageHandler(root);\n"
        "            ConfigureBodyColliders(root);\n"
        "            ConfigurePowertrain(root);",
        1,
    )

# The separate overlay GLB still has a glTF axis conversion relative to the main
# model. Parenting under AmarokVisual fixed translation/scale but identity local
# rotation leaves the lamp set lying on its side. Resolve the remaining 90-degree
# conversion from the authored geometry itself instead of adding centimetre offsets.
old_light_block = '''            var lightSources = FindTransform(root.transform, "AmarokLightSources");
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
new_light_block = '''            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.SetParent(visual, false);
                lightSources.localPosition = Vector3.zero;
                lightSources.localScale = Vector3.one;

                // The overlay GLB was exported separately from Blender. Select the
                // local axis conversion that makes the complete lamp set long in
                // vehicle Z, compact in Y, and keeps BHeadlights ahead of the rear
                // running-light group. This operates in AmarokVisual local space,
                // so the main model's non-uniform scale cannot skew the decision.
                var candidates = new[]
                {
                    Quaternion.Euler(90f, 0f, 0f),
                    Quaternion.Euler(-90f, 0f, 0f),
                    Quaternion.Euler(90f, 180f, 0f),
                    Quaternion.Euler(-90f, 180f, 0f),
                    Quaternion.identity,
                    Quaternion.Euler(0f, 180f, 0f),
                };
                var bestRotation = candidates[0];
                var bestScore = float.NegativeInfinity;
                var headTransform = FindTransformWithNameFragment(lightSources, "BHeadlights");
                var rearTransform = FindTransformWithNameFragment(lightSources, "1RearDrivingLights");

                foreach (var candidate in candidates)
                {
                    lightSources.localRotation = candidate;
                    var renderers = lightSources.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                        continue;

                    var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null)
                            continue;
                        var center = visual.InverseTransformPoint(renderer.bounds.center);
                        minimum = Vector3.Min(minimum, center);
                        maximum = Vector3.Max(maximum, center);
                    }

                    var span = maximum - minimum;
                    var score = span.z * 8f - span.y * 10f - span.x;
                    if (headTransform != null && rearTransform != null &&
                        TryGetRendererBounds(headTransform, out var headBounds) &&
                        TryGetRendererBounds(rearTransform, out var rearBounds))
                    {
                        var headCenter = visual.InverseTransformPoint(headBounds.center);
                        var rearCenter = visual.InverseTransformPoint(rearBounds.center);
                        score += headCenter.z > rearCenter.z ? 100f : -100f;
                    }

                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestRotation = candidate;
                }

                lightSources.localRotation = bestRotation;
                Debug.Log(
                    $"VolkswagenAmarok authored-light axis resolved localEuler={lightSources.localEulerAngles}, " +
                    $"score={bestScore:F2}.");
            }
'''
if old_light_block in setup:
    setup = setup.replace(old_light_block, new_light_block, 1)
elif "authored-light axis resolved" not in setup:
    raise SystemExit("Could not replace Amarok third-feedback light alignment block.")

# ---------------------------------------------------------------------------
# Keep factory-black mud flaps out of vanilla CarFeatures.bodyMeshes.
# ---------------------------------------------------------------------------
setup_paint_helper_marker = "    private static bool IsBodyPaintMaterial(Material material) =>\n"
if "IsFactoryBlackExteriorPart(Renderer renderer)" not in setup:
    helper = '''    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
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
                string.Equals(name, "extra1", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var visual = FindTransform(renderer.transform.root, "AmarokVisual");
        if (visual == null)
            return false;
        var center = visual.InverseTransformPoint(renderer.bounds.center);
        var size = renderer.bounds.size;
        return Mathf.Abs(center.x) > 0.65f && Mathf.Abs(center.z) > 0.85f && center.y < 0.70f &&
               size.x < 0.55f && size.y < 0.80f && size.z < 0.55f;
    }

'''
    if setup_paint_helper_marker not in setup:
        raise SystemExit("Could not locate Amarok setup body-paint helper insertion point.")
    setup = setup.replace(setup_paint_helper_marker, helper + setup_paint_helper_marker, 1)

setup = setup.replace(
    "(IsBodyPaintMaterial(material) || IsCaliperMaterial(material) ||\n                     IsInteriorAccentPaintMaterial(material))",
    "((IsBodyPaintMaterial(material) && !IsFactoryBlackExteriorPart(renderer)) ||\n"
    "                     IsCaliperMaterial(material) || IsInteriorAccentPaintMaterial(material))",
)

save(SETUP, setup)


# ---------------------------------------------------------------------------
# Runtime mirrors editor constants because it re-applies drivetrain/colliders on
# each spawned player vehicle.
# ---------------------------------------------------------------------------
runtime = RUNTIME.read_text(encoding="utf-8")
runtime = re.sub(
    r'SetFloat\(transmission, "_downshiftRPM", [^;]+;',
    'SetFloat(transmission, "_downshiftRPM", 2400f);',
    runtime,
)
runtime = set_vector3_constant(runtime, "FrontContactColliderCenter", "0f, 0.46f, 2.08f")
runtime = set_vector3_constant(runtime, "FrontContactColliderSize", "1.86f, 0.72f, 1.14f")
runtime = set_vector3_constant(runtime, "RearContactColliderCenter", "0f, 0.46f, -2.08f")
runtime = set_vector3_constant(runtime, "RearContactColliderSize", "1.86f, 0.72f, 1.14f")

# Expand the world-space visual deformation allowlist to ordinary detachable
# exterior trim too. License/registration plates were previously omitted entirely.
deform_pattern = re.compile(
    r"    private static bool IsDeformableExterior\(MeshFilter filter\)\s*\{.*?\n    \}\n\n    private static bool HasAncestor",
    re.S,
)
deform_method = '''    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (HasAncestor(filter.transform, "AmarokWheel") ||
            HasAncestor(filter.transform, "AmarokFixedCaliper") ||
            HasAncestor(filter.transform, "AmarokLightSources") ||
            HasAncestor(filter.transform, "interior") ||
            HasAncestor(filter.transform, "steering_ok") ||
            HasAncestor(filter.transform, "v6tdi") ||
            HasAncestor(filter.transform, "tyre") ||
            HasMaterial(filter, "carpet", "fabric", "leather", "seatbelt", "gauges"))
            return false;

        if (filter.name.StartsWith("AmarokDamageBody", StringComparison.Ordinal))
            return true;

        var exteriorMarkers = new[]
        {
            "vw_amorak_2018:body", "door_rr_ok", "door_rf_ok", "door_lr_ok", "door_lf_ok",
            "bump_rear_ok", "bump_front_ok", "boot_ok", "bonnet_ok", "far", "led", "cam",
            "motionstock", "aventuramodular", "extra1", "plate", "numberplate", "number_plate",
            "license", "registration", "kennzeichen", "mirror", "grille", "badge", "step",
            "runningboard", "running_board", "mudflap", "mud_flap", "splash"
        };
        foreach (var marker in exteriorMarkers)
            if (HasAncestor(filter.transform, marker) ||
                filter.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return HasMaterial(filter, "phong5", "dorr_R", "chrome", "plastic", "rubber", "metal");
    }

    private static bool HasAncestor'''
runtime, deform_count = deform_pattern.subn(deform_method, runtime, count=1)
if deform_count != 1:
    raise SystemExit("Could not expand Amarok runtime deformable-exterior allowlist.")
save(RUNTIME, runtime)


# ---------------------------------------------------------------------------
# Player seating: another 10 cm lower; retain the current 17-degree recline.
# ---------------------------------------------------------------------------
driver = DRIVER.read_text(encoding="utf-8")
driver, count = re.subn(
    r"private static readonly Vector3 SeatOffset = new\([^;]+;",
    "private static readonly Vector3 SeatOffset = new(0f, -0.33f, -0.45f);",
    driver,
    count=1,
)
if count != 1:
    raise SystemExit("Could not lower Amarok seated player another 10 cm.")
save(DRIVER, driver)


# ---------------------------------------------------------------------------
# NPC/private-driver visual: preserve the currently good body-to-wheel stance,
# then lift the complete Amarok presentation 3 cm above the Gley template.
# Because the player-source body moved down 3 cm in this pass, the body receives
# an additional 3 cm compensation on top of that global 3 cm lift.
# ---------------------------------------------------------------------------
private_driver = PRIVATE_DRIVER.read_text(encoding="utf-8")
npc_pattern = re.compile(
    r"    private static void ApplyNpcBodyFitment\(GameObject clone\)\s*\{.*?\n    \}\n\n",
    re.S,
)
npc_method = '''    private static void ApplyNpcBodyFitment(GameObject clone)
    {
        // Raise every Amarok-owned visual root together, so traffic wheels and
        // body retain their current relationship while clearing the road surface.
        foreach (var name in new[]
        {
            "AmarokVisual", "AmarokDamageBody",
            "AmarokWheelFrontLeft", "AmarokWheelFrontRight",
            "AmarokWheelRearLeft", "AmarokWheelRearRight",
            "AmarokFixedCaliperFrontLeft", "AmarokFixedCaliperFrontRight",
            "AmarokFixedCaliperRearLeft", "AmarokFixedCaliperRearRight",
        })
        {
            var visual = FindTransform(clone.transform, name);
            if (visual != null)
                visual.localPosition += new Vector3(0f, 0.030f, 0f);
        }

        // Source AmarokVisual moved down 3 cm in this feedback pass. Add another
        // 7 cm to the body roots: combined with the global +3 cm above this is
        // +10 cm, which preserves the prior body/wheel stance and moves the full
        // NPC vehicle net +3 cm versus the previous in-game build.
        foreach (var name in new[] { "AmarokVisual", "AmarokDamageBody" })
        {
            var body = FindTransform(clone.transform, name);
            if (body == null)
                continue;
            body.localPosition += new Vector3(0f, 0.070f, 0f);
            body.localRotation = Quaternion.Euler(-0.56f, 0f, 0f) * body.localRotation;
        }
    }

'''
private_driver, npc_count = npc_pattern.subn(npc_method, private_driver, count=1)
if npc_count != 1:
    raise SystemExit("Could not replace Amarok NPC/private-driver stance helper.")
save(PRIVATE_DRIVER, private_driver)


# ---------------------------------------------------------------------------
# Paint controller: skip mud flaps and explicitly keep them factory black.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")
body_helper_pattern = re.compile(
    r"    private static bool IsAmarokBodyPaintMaterial\(Material material\) =>\s*.*?;\n",
    re.S,
)
body_helper = '''    private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)
    {
        if (IsFactoryBlackExteriorPart(renderer))
            return false;
        return material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRawAmarokBodyPaintMaterial(Material material) =>
        material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
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
                string.Equals(name, "extra1", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
            if (string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
            {
                visual = current;
                break;
            }
        if (visual == null)
            return false;

        var center = visual.InverseTransformPoint(renderer.bounds.center);
        var size = renderer.bounds.size;
        return Mathf.Abs(center.x) > 0.65f && Mathf.Abs(center.z) > 0.85f && center.y < 0.70f &&
               size.x < 0.55f && size.y < 0.80f && size.z < 0.55f;
    }
'''
materials, helper_count = body_helper_pattern.subn(body_helper, materials, count=1)
if helper_count != 1 and "IsFactoryBlackExteriorPart(Renderer renderer)" not in materials:
    raise SystemExit("Could not make Amarok paint mapping renderer-aware.")
materials = materials.replace(
    "IsAmarokBodyPaintMaterial(material)",
    "IsAmarokBodyPaintMaterial(renderer, material)",
)

# Force black after every paint application as a final guard against any other
# vehicle-color path that touched the shared source material/property block.
apply_marker = "        appliedColorName = colorName;\n"
if "ApplyFactoryBlackExteriorParts();" not in materials:
    if apply_marker not in materials:
        raise SystemExit("Could not locate Amarok paint apply completion marker.")
    materials = materials.replace(
        apply_marker,
        "        ApplyFactoryBlackExteriorParts();\n\n" + apply_marker,
        1,
    )

method_marker = "    private void FindPaintSlots()\n"
black_method = '''    private void ApplyFactoryBlackExteriorParts()
    {
        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!IsFactoryBlackExteriorPart(renderer))
                continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || !IsRawAmarokBodyPaintMaterial(material))
                    continue;
                properties.Clear();
                renderer.GetPropertyBlock(properties, index);
                if (material.HasProperty(BaseColor)) properties.SetColor(BaseColor, black);
                if (material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, black);
                if (material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, black);
                renderer.SetPropertyBlock(properties, index);
            }
        }
    }

'''
if "private void ApplyFactoryBlackExteriorParts()" not in materials:
    if method_marker not in materials:
        raise SystemExit("Could not locate Amarok paint-slot method insertion point.")
    materials = materials.replace(method_marker, black_method + method_marker, 1)
save(MATERIALS, materials)


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------
checks = {
    SETUP: [
        "private const float BodyVisualBottomY = -0.070f;",
        '"powertrain.transmission._downshiftRPM", 2400f',
        "new Vector3(0f, 0.46f, 2.08f);",
        "new Vector3(1.86f, 0.72f, 1.14f);",
        "ConfigureBodyColliders(root);",
        "authored-light axis resolved",
        "Quaternion.Euler(90f, 0f, 0f)",
        "IsFactoryBlackExteriorPart(Renderer renderer)",
        "!IsFactoryBlackExteriorPart(renderer)",
    ],
    RUNTIME: [
        'SetFloat(transmission, "_downshiftRPM", 2400f);',
        "new Vector3(0f, 0.46f, 2.08f);",
        "new Vector3(0f, 0.60f, -2.08f);",
        '"kennzeichen"',
        '"numberplate"',
    ],
    DRIVER: [
        "private static readonly Vector3 SeatOffset = new(0f, -0.33f, -0.45f);",
        "private const float SeatBackLeanDegrees = 17f;",
    ],
    PRIVATE_DRIVER: [
        'visual.localPosition += new Vector3(0f, 0.030f, 0f);',
        'body.localPosition += new Vector3(0f, 0.070f, 0f);',
        "Quaternion.Euler(-0.56f, 0f, 0f)",
    ],
    MATERIALS: [
        "IsAmarokBodyPaintMaterial(Renderer renderer, Material material)",
        "IsFactoryBlackExteriorPart(renderer)",
        "ApplyFactoryBlackExteriorParts();",
        "new Color(0.018f, 0.018f, 0.018f, 1f)",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok fourth in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Lowered the player Amarok body another 3 cm to BodyVisualBottomY=-0.070 m.")
print("Raised the complete NPC/private-driver Amarok visual presentation a net 3 cm.")
print("Lowered the seated player another 10 cm while keeping the 17-degree recline.")
print("Lowered automatic downshift threshold from 2800 to 2400 rpm.")
print("Extended the Amarok end caps and lowered both front and rear contact walls so flat car noses cannot wedge underneath in either direction.")
print("Expanded visual deformation to registration plates and additional exterior trim.")
print("Excluded mud flaps/splash guards from vehicle paint and forced them factory black.")
print("Resolved the authored lamp GLB's remaining 90-degree axis conversion in AmarokVisual local space.")
print("Volkswagen Amarok fourth in-game feedback preflight passed.")
