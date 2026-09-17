from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SCRIPTS = MOD / "Scripts"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
MATERIALS = SCRIPTS / "VolkswagenAmarokMaterials.cs"
MATERIAL_CONTROLLER = SCRIPTS / "VolkswagenAmarokMaterialController.cs"

for path in (SETUP, MATERIALS, MATERIAL_CONTROLLER):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source file is missing: {path}")


def save(path, text):
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Material pipeline: every opaque runtime material must be normalized to the
# SDK's known-good HDRP/Lit state. The earlier setup test incorrectly treated
# _SupportDecals as proof that an imported glTF shader was already usable.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")
materials = materials.replace(
    "    private static void RebindToHdrpLit(Material material)",
    "    internal static void RebindToHdrpLit(Material material)",
    1,
)
materials = materials.replace(
    "    private static bool FixSolidHdrpMaterial(Material material)",
    "    internal static bool FixSolidHdrpMaterial(Material material)",
    1,
)
if "internal static void RebindToHdrpLit" not in materials or \
   "internal static bool FixSolidHdrpMaterial" not in materials:
    raise SystemExit("Could not expose the Amarok HDRP material normalization helpers.")
save(MATERIALS, materials)

controller = MATERIAL_CONTROLLER.read_text(encoding="utf-8")
if "var opaqueMaterials = 0;" not in controller:
    marker = "        var transparentMaterials = 0;\n"
    if marker not in controller:
        raise SystemExit("Could not locate material-controller counters.")
    controller = controller.replace(
        marker,
        marker + "        var opaqueMaterials = 0;\n",
        1,
    )

old_branch = '''                    if (role != VolkswagenAmarokTransparentRole.Authored)
                    {
                        VolkswagenAmarokMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (VolkswagenAmarokMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        authoredTransparentMaterials++;
                    }'''
new_branch = '''                    if (role != VolkswagenAmarokTransparentRole.Authored)
                    {
                        VolkswagenAmarokMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (VolkswagenAmarokMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        authoredTransparentMaterials++;
                    }
                    else
                    {
                        // Imported glTF opaque shaders can exist as valid Material
                        // objects yet render magenta in the SDK's HDRP scene. Force
                        // every authored opaque instance through the known-good
                        // HDRP/Lit conversion and validation path.
                        VolkswagenAmarokMaterials.RebindToHdrpLit(runtimeMaterial);
                        VolkswagenAmarokMaterials.FixSolidHdrpMaterial(runtimeMaterial);
                        opaqueMaterials++;
                    }'''
if old_branch in controller:
    controller = controller.replace(old_branch, new_branch, 1)
elif "opaqueMaterials++;" not in controller:
    raise SystemExit("Could not patch opaque Amarok material normalization.")

old_result = '''            rendererCount,
            0,
            0,
            transparentMaterials,'''
new_result = '''            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,'''
if old_result in controller:
    controller = controller.replace(old_result, new_result, 1)
elif "            opaqueMaterials,\n            transparentMaterials," not in controller:
    raise SystemExit("Could not wire the opaque-material count into the material-fix result.")
save(MATERIAL_CONTROLLER, controller)


# ---------------------------------------------------------------------------
# Prefab values: author target wheelbase and pickup damage tuning directly in
# the generated prefab instead of leaving donor defaults for runtime to repair.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

powertrain_marker = '''                found = true;
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1000f);'''
powertrain_replacement = '''                found = true;
                SetNumber(serialized, "wheelbase", Wheelbase);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1000f);'''
if 'SetNumber(serialized, "wheelbase", Wheelbase);' not in setup:
    if powertrain_marker not in setup:
        raise SystemExit("Could not locate Amarok NWH powertrain configuration.")
    setup = setup.replace(powertrain_marker, powertrain_replacement, 1)

call_marker = '''            ConfigureVehicleDeformation(root, damageBody);
            var fix = VolkswagenAmarokMaterials.FixSolidMaterials(root);'''
call_replacement = '''            ConfigureVehicleDeformation(root, damageBody);
            ConfigurePickupDamageHandler(root);
            var fix = VolkswagenAmarokMaterials.FixSolidMaterials(root);'''
if "ConfigurePickupDamageHandler(root);" not in setup:
    if call_marker not in setup:
        raise SystemExit("Could not locate Amarok deformation/material setup sequence.")
    setup = setup.replace(call_marker, call_replacement, 1)

helper_marker = "    private static bool IsBodyPaintMaterial(Material material) =>\n"
helper = '''    private static void ConfigurePickupDamageHandler(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(component.GetType().Name, "DamageHandler", StringComparison.Ordinal))
                continue;

            var serialized = new SerializedObject(component);
            SetNumber(serialized, "damageIntensity", 0.72f);
            SetNumber(serialized, "decelerationThreshold", 650f);
            SetNumber(serialized, "deformationRadius", DeformationRadius);
            SetNumber(serialized, "deformationRandomness", DeformationRandomness);
            SetNumber(serialized, "deformationStrength", DeformationStrength);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            found = true;
        }
        if (!found)
            throw new InvalidOperationException("Reference DamageHandler is missing.");
    }

'''
if "private static void ConfigurePickupDamageHandler" not in setup:
    if helper_marker not in setup:
        raise SystemExit("Could not locate Amarok damage-helper insertion point.")
    setup = setup.replace(helper_marker, helper + helper_marker, 1)

# The previous editor-time conversion skipped materials merely because they had
# an HDRP-ish property. Restrict the fast-path to the actual Lit shader; all
# other opaque imported shaders are explicitly rebound.
old_skip = '''                if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal) ||
                    shaderName.StartsWith("High Definition Render Pipeline/", StringComparison.Ordinal) ||
                    material.HasProperty("_SupportDecals"))
                {
                    continue;
                }'''
new_skip = '''                if (string.Equals(shaderName, "HDRP/Lit", StringComparison.Ordinal) ||
                    string.Equals(shaderName, "High Definition Render Pipeline/Lit", StringComparison.Ordinal))
                {
                    // Still normalize the material state below at runtime via
                    // VolkswagenAmarokMaterialController.
                    continue;
                }'''
if old_skip in setup:
    setup = setup.replace(old_skip, new_skip, 1)

save(SETUP, setup)


# Hard source preflight.
checks = {
    MATERIALS: [
        "internal static void RebindToHdrpLit",
        "internal static bool FixSolidHdrpMaterial",
    ],
    MATERIAL_CONTROLLER: [
        "VolkswagenAmarokMaterials.RebindToHdrpLit(runtimeMaterial);",
        "VolkswagenAmarokMaterials.FixSolidHdrpMaterial(runtimeMaterial);",
        "opaqueMaterials++;",
    ],
    SETUP: [
        'SetNumber(serialized, "wheelbase", Wheelbase);',
        "ConfigurePickupDamageHandler(root);",
        'SetNumber(serialized, "damageIntensity", 0.72f);',
        'SetNumber(serialized, "decelerationThreshold", 650f);',
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok visual/prefab-value patch failed:\n- " + "\n- ".join(missing))

print("Patched Amarok opaque materials to use the validated HDRP/Lit path.")
print("Patched NWH wheelbase to 3.097 m in the generated prefab.")
print("Patched DamageHandler to the pickup damage calibration (0.72 / 650 / 0.30 / 0.005 / 0.12).")
print("Volkswagen Amarok visual/prefab-value preflight passed.")
