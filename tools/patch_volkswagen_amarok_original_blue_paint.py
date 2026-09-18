from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"

for path in (SETUP, RUNTIME, MATERIALS):
    if not path.is_file():
        raise SystemExit(f"Required Amarok source is missing: {path}")

# ---------------------------------------------------------------------------
# Editor/prefab build:
# VehiclePaint_Blue is derived automatically by Blender from the ORIGINAL blue
# source texture. Make it the only renderer exposed to BA's body-paint renderer
# arrays. The original model remains untouched, so black grille/plastic/chrome
# areas keep the creator's authored appearance.
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

paint_call = "            ConfigureOriginalBluePaintSurface(root, visual);\n"
paint_call_marker = (
    '            var oldDamageBody = FindTransform(root.transform, "AmarokDamageBody");\n'
)
if paint_call not in setup:
    if paint_call_marker not in setup:
        raise SystemExit(
            "Could not locate Amarok existing-prefab paint-surface insertion point."
        )
    setup = setup.replace(paint_call_marker, paint_call + "\n" + paint_call_marker, 1)

helper_marker = "    private static void ConfigureRendererReferences(GameObject root)\n"
paint_helper = r'''    private static void ConfigureOriginalBluePaintSurface(
        GameObject root,
        Transform visual)
    {
        // Paint panels are reparented out of AmarokLightSources. Remove panels
        // persisted by the previous existing-prefab build before adopting the
        // freshly imported GLB panels, otherwise every build stacks another
        // paint shell on the vehicle.
        foreach (var current in root.GetComponentsInChildren<Transform>(true))
        {
            if (!current.name.StartsWith(
                    "VolkswagenAmarok_VehiclePaint_Blue",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            UnityEngine.Object.DestroyImmediate(current.gameObject);
        }

        var paintRenderers = new List<MeshRenderer>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name.IndexOf(
                    "VehiclePaint_Blue",
                    StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            paintRenderers.Add(renderer);
        }

        if (paintRenderers.Count == 0)
            throw new InvalidOperationException(
                "VehiclePaint_Blue panels are missing from AmarokLightOverlays.glb.");

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(ModRoot, "Materials");

        var paintPath = MaterialFolder + "/VolkswagenAmarok_BA_VehiclePaint.mat";
        var paintMaterial = AssetDatabase.LoadAssetAtPath<Material>(paintPath);
        if (paintMaterial == null)
        {
            var shader = Shader.Find("HDRP/Lit") ??
                         Shader.Find("High Definition Render Pipeline/Lit") ??
                         throw new InvalidOperationException(
                             "HDRP/Lit is unavailable for Amarok body paint.");
            paintMaterial = new Material(shader)
            {
                name = "VolkswagenAmarok_BA_VehiclePaint",
            };
            AssetDatabase.CreateAsset(paintMaterial, paintPath);
        }

        var white = Color.white;
        if (paintMaterial.HasProperty("_BaseColor"))
            paintMaterial.SetColor("_BaseColor", white);
        if (paintMaterial.HasProperty("_Color"))
            paintMaterial.SetColor("_Color", white);
        if (paintMaterial.HasProperty("baseColorFactor"))
            paintMaterial.SetColor("baseColorFactor", white);
        if (paintMaterial.HasProperty("_BaseColorMap"))
            paintMaterial.SetTexture("_BaseColorMap", null);
        if (paintMaterial.HasProperty("baseColorTexture"))
            paintMaterial.SetTexture("baseColorTexture", null);
        if (paintMaterial.HasProperty("_MainTex"))
            paintMaterial.SetTexture("_MainTex", null);
        if (paintMaterial.HasProperty("_Metallic"))
            paintMaterial.SetFloat("_Metallic", 0.08f);
        if (paintMaterial.HasProperty("_Smoothness"))
            paintMaterial.SetFloat("_Smoothness", 0.86f);
        if (paintMaterial.HasProperty("_CoatMask"))
            paintMaterial.SetFloat("_CoatMask", 0.18f);
        if (paintMaterial.HasProperty("_SurfaceType"))
            paintMaterial.SetFloat("_SurfaceType", 0f);
        if (paintMaterial.HasProperty("_ZWrite"))
            paintMaterial.SetFloat("_ZWrite", 1f);

        foreach (var paintRenderer in paintRenderers)
        {
            // Keep each paint panel separate. This mirrors the finished vehicle
            // mods' panel-based deformation path and lets crash handling cull
            // distant doors/body sections before reading their vertex buffers.
            paintRenderer.transform.SetParent(visual, true);
            paintRenderer.gameObject.name =
                "VolkswagenAmarok_" + paintRenderer.gameObject.name;
            paintRenderer.sharedMaterial = paintMaterial;
            paintRenderer.enabled = true;
        }
        EditorUtility.SetDirty(paintMaterial);

        Debug.Log(
            $"VolkswagenAmarok original-blue paint surfaces ready panels=" +
            $"{paintRenderers.Count}.");
    }

'''
if "private static void ConfigureOriginalBluePaintSurface(" not in setup:
    if helper_marker not in setup:
        raise SystemExit(
            "Could not locate ConfigureRendererReferences() for Amarok paint helper."
        )
    setup = setup.replace(helper_marker, paint_helper + helper_marker, 1)

setup, setup_paint_count = re.subn(
    r'''    private static bool IsBodyPaintMaterial\(Material material\) =>\s*.*?;\n''',
    '''    private static bool IsBodyPaintMaterial(Material material) =>\n        material.name.IndexOf("_BA_VehiclePaint", StringComparison.OrdinalIgnoreCase) >= 0;\n''',
    setup,
    count=1,
    flags=re.S,
)
if setup_paint_count != 1:
    raise SystemExit("Could not narrow Amarok setup body-paint material classifier.")

SETUP.write_text(setup, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Runtime paint controller:
# Stop treating every phong5/dorr_R slot as paint. Those source materials now stay
# exactly as authored. Only the automatically derived VehiclePaint_Blue shell gets
# BA VehicleColor property blocks.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")

neutralizer = '''    private static void NeutralizeAmarokBodyPaintTexture(Material material)
    {
        if (material.name.IndexOf(
                "_BA_VehiclePaint",
                StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", null);
        if (material.HasProperty("baseColorTexture"))
            material.SetTexture("baseColorTexture", null);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", null);
    }

'''

# Earlier feedback passes have reformatted this helper multiple times. Replace
# by declaration boundaries instead of depending on braces/newlines produced by
# a specific generated-source version.
neutralizer_start_marker = (
    "    private static void NeutralizeAmarokBodyPaintTexture(Material material)"
)
neutralizer_end_markers = [
    "    internal static bool FixSolidHdrpMaterial(Material material)",
    "    private static bool IsTransparentMaterial(Material material)",
]
neutralizer_start = materials.find(neutralizer_start_marker)
neutralizer_end = -1
for marker in neutralizer_end_markers:
    candidate = materials.find(
        marker,
        neutralizer_start + len(neutralizer_start_marker)
        if neutralizer_start >= 0 else 0,
    )
    if candidate >= 0 and (neutralizer_end < 0 or candidate < neutralizer_end):
        neutralizer_end = candidate

if neutralizer_start < 0 or neutralizer_end < 0 or neutralizer_end <= neutralizer_start:
    raise SystemExit(
        "Could not locate Amarok source-texture neutralizer boundaries "
        f"start={neutralizer_start} end={neutralizer_end}."
    )
materials = (
    materials[:neutralizer_start]
    + neutralizer
    + materials[neutralizer_end:]
)

body_helper = '''    private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)
    {
        return renderer.name.IndexOf(
                   "VehiclePaint_Blue",
                   StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf(
                   "_BA_VehiclePaint",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

'''

body_start_marker = (
    "    private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)"
)
body_end_marker = "    private static bool IsRawAmarokBodyPaintMaterial"
body_start = materials.find(body_start_marker)
body_end = materials.find(
    body_end_marker,
    body_start + len(body_start_marker) if body_start >= 0 else 0,
)
if body_start < 0 or body_end < 0 or body_end <= body_start:
    raise SystemExit(
        "Could not locate Amarok runtime paint-classifier boundaries "
        f"start={body_start} end={body_end}."
    )
materials = materials[:body_start] + body_helper + materials[body_end:]

MATERIALS.write_text(materials, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Custom visible deformation:
# The paint shell is no longer under AmarokLightSources after the editor helper
# reparents it. Explicitly include it as deformable bodywork so the paint follows
# the same crash deformation instead of hovering over dents.
# ---------------------------------------------------------------------------
runtime = RUNTIME.read_text(encoding="utf-8")
deform_signature = '''    private static bool IsDeformableExterior(MeshFilter filter)
    {
'''
if deform_signature not in runtime:
    raise SystemExit("Could not locate Amarok runtime deformable-exterior helper.")

deform_guard = '''    private static bool IsDeformableExterior(MeshFilter filter)
    {
        // These front surfaces are authored under AmarokLightSources, which is
        // normally excluded from crash deformation. Allow the shared white DRL
        // meshes plus the amber runtime copies before that broad exclusion so
        // DRL and indicator geometry follows the damaged front corner.
        var deformFrontDrlIndicator =
            filter.name.IndexOf(
                "BDRL_Indicator_FL",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "BDRL_Indicator_FR",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "VolkswagenAmarok_IndicatorLeft",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "VolkswagenAmarok_IndicatorRight",
                StringComparison.OrdinalIgnoreCase) >= 0;
        if (deformFrontDrlIndicator)
            return true;

        if (filter.name.IndexOf(
                "VehiclePaint_Blue",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "VolkswagenAmarok_VehiclePaint",
                StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
'''
if "VolkswagenAmarok_VehiclePaint" not in runtime:
    runtime = runtime.replace(deform_signature, deform_guard, 1)
elif "deformFrontDrlIndicator" not in runtime:
    # Existing worktrees may already have the VehiclePaint_Blue deformation
    # guard from an earlier build. Retrofit only the new front-light exception
    # at the very start of IsDeformableExterior(), before AmarokLightSources can
    # reject those filters.
    front_light_guard = '''    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var deformFrontDrlIndicator =
            filter.name.IndexOf(
                "BDRL_Indicator_FL",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "BDRL_Indicator_FR",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "VolkswagenAmarok_IndicatorLeft",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.IndexOf(
                "VolkswagenAmarok_IndicatorRight",
                StringComparison.OrdinalIgnoreCase) >= 0;
        if (deformFrontDrlIndicator)
            return true;
'''
    runtime = runtime.replace(deform_signature, front_light_guard, 1)

RUNTIME.write_text(runtime, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Preflight.
# ---------------------------------------------------------------------------
checks = {
    SETUP: [
        "ConfigureOriginalBluePaintSurface(root, visual);",
        '"VehiclePaint_Blue"',
        '"VolkswagenAmarok_BA_VehiclePaint"',
        'material.name.IndexOf("_BA_VehiclePaint"',
        "paintRenderer.transform.SetParent(visual, true);",
        "var paintRenderers = new List<MeshRenderer>();",
        '"VolkswagenAmarok_VehiclePaint_Blue"',
        "DestroyImmediate(current.gameObject);",
        "paintRenderers.Count",
    ],
    MATERIALS: [
        '"_BA_VehiclePaint"',
        '"VehiclePaint_Blue"',
        "private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)",
        "private static bool IsRawAmarokBodyPaintMaterial",
    ],
    RUNTIME: [
        '"VolkswagenAmarok_VehiclePaint"',
        '"VehiclePaint_Blue"',
        '"BDRL_Indicator_FL"',
        '"BDRL_Indicator_FR"',
        '"VolkswagenAmarok_IndicatorLeft"',
        '"VolkswagenAmarok_IndicatorRight"',
        "if (deformFrontDrlIndicator)",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")

# The broad paint classifier must be gone from the final runtime helper. Raw
# material matching is intentionally retained only for factory-black fallback
# code, never for VehicleColor slots.
materials_check = MATERIALS.read_text(encoding="utf-8")
paint_helper_start_marker = (
    "    private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)"
)
paint_helper_end_marker = "    private static bool IsRawAmarokBodyPaintMaterial"
paint_helper_start = materials_check.find(paint_helper_start_marker)
paint_helper_end = materials_check.find(
    paint_helper_end_marker,
    paint_helper_start + len(paint_helper_start_marker)
    if paint_helper_start >= 0 else 0,
)
if (
    paint_helper_start < 0
    or paint_helper_end < 0
    or paint_helper_end <= paint_helper_start
):
    missing.append(
        "VolkswagenAmarokMaterials.cs: final paint helper boundaries "
        f"start={paint_helper_start} end={paint_helper_end}"
    )
else:
    helper_text = materials_check[paint_helper_start:paint_helper_end]
    if "_BA_VehiclePaint" not in helper_text or "VehiclePaint_Blue" not in helper_text:
        missing.append(
            "VolkswagenAmarokMaterials.cs: final paint helper does not target "
            "VehiclePaint_Blue/_BA_VehiclePaint"
        )
    if "phong5" in helper_text or "dorr_R" in helper_text:
        missing.append(
            "VolkswagenAmarokMaterials.cs: broad phong5/dorr_R paint classifier remained"
        )

if missing:
    raise SystemExit(
        "Amarok original-blue paint patch failed:\n- " + "\n- ".join(missing)
    )

print("Switched Amarok VehicleColor mapping from broad phong5/dorr_R materials to the automatically derived original-blue paint surface.")
print("Original black grille/plastic/chrome geometry now keeps the source model appearance unless explicitly overridden.")
print("VehiclePaint_Blue is reparented into AmarokVisual and included in visible crash deformation.")
print("Front DRL/indicator meshes are explicitly included in crash deformation before the AmarokLightSources exclusion.")
print("Volkswagen Amarok original-blue paint preflight passed.")
