from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"
LIGHTING = MOD / "Scripts/VolkswagenAmarokLightingController.cs"

for path in (SETUP, MATERIALS, LIGHTING):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Rear lamp lens.
#
# The source GLB already contains the full rear-lamp cover seen in Sketchfab.
# It disappeared because every BLEND material was normalized to the same nearly
# colorless OtherClear tint at runtime. Preserve the original source color/texture
# for transparent renderers at the rear corners instead of trying to fake the
# complete lens with the smaller emissive Blender vertex groups.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")

# Add a semantic role for the source rear-lamp lens.
enum_pattern = re.compile(
    r"(internal enum VolkswagenAmarokTransparentRole\s*\{)(.*?)(\n\})",
    re.S,
)
match = enum_pattern.search(materials)
if match is None:
    raise SystemExit("Could not locate VolkswagenAmarokTransparentRole enum.")
if "RearLampLens" not in match.group(2):
    body = match.group(2)
    insert_before = "\n    OtherClear,"
    if insert_before in body:
        body = body.replace(insert_before, "\n    RearLampLens," + insert_before, 1)
    else:
        body = body.rstrip() + "\n    RearLampLens,\n"
    materials = materials[:match.start(2)] + body + materials[match.end(2):]

# Capture the source glTF tint before RebindToHdrpLit() changes shader state.
prepare_signature = '''    internal static void PrepareTransparentMaterial(
        Material material,
        VolkswagenAmarokTransparentRole role = VolkswagenAmarokTransparentRole.OtherClear)
    {
'''
if prepare_signature not in materials:
    raise SystemExit("Could not locate Amarok PrepareTransparentMaterial().")

if "var sourceTransparentTint =" not in materials:
    materials = materials.replace(
        prepare_signature,
        prepare_signature
        + '''        var sourceTransparentTint = GetColor(
            material, "baseColorFactor", "_BaseColor", Color.white);
        var rearLampLens = role == VolkswagenAmarokTransparentRole.RearLampLens;
''',
        1,
    )

old_tint = '''        var tint = cabinGlass
            ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
            : role == VolkswagenAmarokTransparentRole.HeadlampLens
                ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                : new Color(0.82f, 0.86f, 0.90f, 0.05f);'''
new_tint = '''        var tint = rearLampLens
            ? sourceTransparentTint
            : cabinGlass
                ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
                : role == VolkswagenAmarokTransparentRole.HeadlampLens
                    ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                    : new Color(0.82f, 0.86f, 0.90f, 0.05f);
        if (rearLampLens)
            tint.a = Mathf.Clamp(tint.a, 0.55f, 0.90f);'''
if old_tint in materials:
    materials = materials.replace(old_tint, new_tint, 1)
elif (
    "rearLampLens\n            ? sourceTransparentTint" not in materials
    and "new Color(0.48f, 0.018f, 0.012f, 0.78f)" not in materials
):
    raise SystemExit("Could not replace Amarok transparent tint selection.")

# Rear-lamp role classification is position based so it does not depend on
# exporter-generated object names. Front lamps are already correct and remain on
# the existing Headlamp/OtherClear path.
role_pattern = re.compile(
    r'''    internal static VolkswagenAmarokTransparentRole GetTransparentRole\(
        Renderer renderer,
        Material material\)
    \{.*?\n    \}\n\n''',
    re.S,
)
role_method = '''    internal static VolkswagenAmarokTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (!IsTransparentMaterial(material))
            return VolkswagenAmarokTransparentRole.Authored;

        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }

        if (visual != null)
        {
            var center = visual.InverseTransformPoint(renderer.bounds.center);
            // Rear light clusters sit at the two outer corners behind the rear
            // axle. The rear cabin window is central and much farther forward.
            if (center.z < -1.70f && Mathf.Abs(center.x) > 0.48f && center.y > 0.35f)
                return VolkswagenAmarokTransparentRole.RearLampLens;
        }

        var rendererName = renderer.name;
        if (rendererName.IndexOf("headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("far", StringComparison.OrdinalIgnoreCase) >= 0)
            return VolkswagenAmarokTransparentRole.HeadlampLens;

        if (rendererName.IndexOf("windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return VolkswagenAmarokTransparentRole.CabinGlass;
        }

        return VolkswagenAmarokTransparentRole.OtherClear;
    }

'''
materials, role_count = role_pattern.subn(role_method, materials, count=1)
if role_count != 1:
    raise SystemExit("Could not replace Amarok transparent-role classification.")

# ---------------------------------------------------------------------------
# Factory-black tread pads / splash guards.
#
# Previous checks used world-axis renderer.bounds dimensions and therefore change
# when the vehicle is rotated in the city. Measure the source mesh in AmarokVisual
# local space instead. Only body-paint material slots are blacked later, so chrome
# tubes / silver bumper materials remain silver.
# ---------------------------------------------------------------------------
factory_helper = '''    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
    {
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("mudflap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mud_flap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("splash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("runningboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("running_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sidestep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side_step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("footboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foot_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("step_pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tread", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null || !TryGetMeshBoundsInSpace(renderer, visual, out var localBounds))
            return false;

        var center = localBounds.center;
        var size = localBounds.size;

        // Never classify the complete baked body shell as a black accessory.
        if (size.x > 1.60f && size.z > 3.0f)
            return false;

        // Separate thin guards behind the wheels.
        var splashGuard =
            Mathf.Abs(center.x) > 0.62f &&
            Mathf.Abs(center.z) > 0.72f &&
            center.y < 0.72f &&
            size.x < 0.80f &&
            size.y < 1.05f &&
            size.z < 0.85f;

        // Rubber tread pads sitting on the long side tubes. The metal tube uses
        // another material slot and is intentionally not recolored here.
        var sideTread =
            Mathf.Abs(center.x) > 0.58f &&
            Mathf.Abs(center.z) < 1.80f &&
            center.y < 0.68f &&
            size.x < 0.85f &&
            size.y < 0.45f &&
            size.z < 2.90f;

        // Black tread surface above the rear chrome bumper.
        var rearTread =
            center.z < -1.72f &&
            center.y < 0.78f &&
            size.x < 2.35f &&
            size.y < 0.50f &&
            size.z < 1.05f;

        return splashGuard || sideTread || rearTread;
    }

    private static bool TryGetMeshBoundsInSpace(
        Renderer renderer,
        Transform space,
        out Bounds bounds)
    {
        bounds = default;
        var filter = renderer.GetComponent<MeshFilter>();
        var mesh = filter?.sharedMesh;
        if (mesh == null)
            return false;

        var source = mesh.bounds;
        var extents = source.extents;
        var initialized = false;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var local = source.center + Vector3.Scale(
                extents, new Vector3(x, y, z));
            var point = space.InverseTransformPoint(
                renderer.transform.TransformPoint(local));
            if (!initialized)
            {
                bounds = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }
        return initialized;
    }
'''

materials_factory_pattern = re.compile(
    r'''    private static bool IsFactoryBlackExteriorPart\(Renderer renderer\)\s*\{.*?\n    \}\n\n'''
    r'''    private void ApplyFactoryBlackExteriorParts''',
    re.S,
)
materials, material_factory_count = materials_factory_pattern.subn(
    factory_helper + "\n    private void ApplyFactoryBlackExteriorParts",
    materials,
    count=1,
)
if material_factory_count != 1:
    raise SystemExit("Could not replace Amarok runtime factory-black classifier.")

# Add one compact diagnostic to the paint application. If any part still slips
# through, the next Player.log will contain exact renderer names/positions.
if "factoryBlack=[{string.Join" not in materials:
    apply_method_pattern = re.compile(
        r'''    private void ApplyFactoryBlackExteriorParts\(\)\s*\{.*?\n    \}\n\n''',
        re.S,
    )
    apply_match = apply_method_pattern.search(materials)
    if apply_match is None:
        raise SystemExit("Could not locate Amarok ApplyFactoryBlackExteriorParts().")
    block = apply_match.group(0)
    block = block.replace(
        '''        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
''',
        '''        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        var applied = new List<string>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
''',
        1,
    )
    block = block.replace(
        '''                renderer.SetPropertyBlock(properties, index);
            }
        }
    }

''',
        '''                renderer.SetPropertyBlock(properties, index);
                if (!applied.Contains(renderer.name))
                    applied.Add(renderer.name);
            }
        }
        context?.Logger.Info(
            $"VolkswagenAmarok paint factoryBlack=[{string.Join(", ", applied)}].");
    }

''',
        1,
    )
    materials = materials[:apply_match.start()] + block + materials[apply_match.end():]

save(MATERIALS, materials)


# Editor/CarFeatures paint list must use the same local-space classifier so the
# vanilla VehicleColor path never receives those black accessory renderers.
setup = SETUP.read_text(encoding="utf-8")
setup_factory_pattern = re.compile(
    r'''    private static bool IsFactoryBlackExteriorPart\(Renderer renderer\)\s*\{.*?\n    \}\n\n'''
    r'''    private static bool IsBodyPaintMaterial''',
    re.S,
)
setup_factory_helper = factory_helper.replace(
    '''        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null || !TryGetMeshBoundsInSpace(renderer, visual, out var localBounds))
            return false;''',
    '''        var visual = FindTransform(renderer.transform.root, "AmarokVisual");
        if (visual == null || !TryGetMeshBoundsInSpace(renderer, visual, out var localBounds))
            return false;'''
)
setup, setup_factory_count = setup_factory_pattern.subn(
    setup_factory_helper + "\n    private static bool IsBodyPaintMaterial",
    setup,
    count=1,
)
if setup_factory_count != 1:
    raise SystemExit("Could not replace Amarok editor factory-black classifier.")
save(SETUP, setup)


# The small dark-red covers generated from the emissive Blender groups are not
# the vehicle's full lens. Keep those helpers available for old preflights, but
# disable them at runtime now that the original source lens is preserved.
lighting = LIGHTING.read_text(encoding="utf-8")
lighting = re.sub(
    r'(?:\s*SetEnabled\(rearTailLensCover, (?:true|false)\);\n)+',
    '        SetEnabled(rearTailLensCover, false);\n',
    lighting,
)
lighting = re.sub(
    r'(?:\s*SetEnabled\(rearBrakeLensCover, (?:true|false)\);\n)+',
    '        SetEnabled(rearBrakeLensCover, false);\n',
    lighting,
)
save(LIGHTING, lighting)


checks = {
    MATERIALS: [
        "VolkswagenAmarokTransparentRole.RearLampLens",
        "sourceTransparentTint",
        "center.z < -1.70f",
        "TryGetMeshBoundsInSpace",
        'name.IndexOf("tread"',
        'VolkswagenAmarok paint factoryBlack=',
    ],
    SETUP: [
        "TryGetMeshBoundsInSpace",
        'name.IndexOf("tread"',
        "var rearTread =",
        "var sideTread =",
        "var splashGuard =",
    ],
    LIGHTING: [
        "SetEnabled(rearTailLensCover, false);",
        "SetEnabled(rearBrakeLensCover, false);",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok sixth in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Restored the original source-material tint/texture on the full Amarok rear-lamp lenses.")
print("Disabled the old partial fake rear-lens covers made from emissive Blender groups.")
print("Reworked black tread/splash-guard detection to use rotation-independent AmarokVisual-local mesh bounds.")
print("Added factory-black renderer diagnostics for the next in-game log.")
print("Volkswagen Amarok sixth in-game feedback preflight passed.")
