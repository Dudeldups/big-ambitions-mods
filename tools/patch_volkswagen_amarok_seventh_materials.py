from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MATERIALS = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokMaterials.cs"

if not MATERIALS.is_file():
    raise SystemExit(f"Generated Amarok materials source is missing: {MATERIALS}")

text = MATERIALS.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# Rear lamp cover: use the exact source renderer name reported from the GLB.
# The previous position heuristic could miss this renderer and sourceTransparentTint
# can be nearly neutral. Preserve its source texture but give the lens the deep red
# plastic tint seen on the original Sketchfab model.
# ---------------------------------------------------------------------------
role_signature = '''    internal static VolkswagenAmarokTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
'''
if role_signature not in text:
    raise SystemExit("Could not locate Amarok transparent-role method.")

exact_lens_guard = '''        if (string.Equals(
                renderer.name,
                "vw_amorak_2018:cam_EXT_GLASS_0",
                StringComparison.OrdinalIgnoreCase))
            return VolkswagenAmarokTransparentRole.RearLampLens;

'''
if '"vw_amorak_2018:cam_EXT_GLASS_0"' not in text:
    text = text.replace(role_signature, role_signature + exact_lens_guard, 1)

# Replace the rear-lens branch with an explicit OEM-red transparent plastic tint.
tint_pattern = re.compile(
    r'''        var tint = rearLampLens\s*
            \? sourceTransparentTint\s*
            : cabinGlass.*?
        if \(rearLampLens\)\s*
            tint\.a = Mathf\.Clamp\(tint\.a, 0\.55f, 0\.90f\);''',
    re.S,
)
tint_replacement = '''        var tint = rearLampLens
            ? new Color(0.48f, 0.018f, 0.012f, 0.78f)
            : cabinGlass
                ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
                : role == VolkswagenAmarokTransparentRole.HeadlampLens
                    ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                    : new Color(0.82f, 0.86f, 0.90f, 0.05f);'''
text, tint_count = tint_pattern.subn(tint_replacement, text, count=1)
if tint_count != 1 and "new Color(0.48f, 0.018f, 0.012f, 0.78f)" not in text:
    raise SystemExit("Could not force Amarok rear-lamp lens red tint.")

rear_lens_hook = '''        if (rearLampLens)
        {
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.90f);
            SetFloat(material, "roughnessFactor", 0.10f);
        }
'''
surface_marker = '        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);\n'
if rear_lens_hook not in text:
    if surface_marker not in text:
        raise SystemExit("Could not locate transparent material tuning marker.")
    text = text.replace(surface_marker, rear_lens_hook + surface_marker, 1)

# ---------------------------------------------------------------------------
# Exact black accessory names supplied from the actual GLB.
# - rear step can be blacked as a whole body-paint renderer
# - aventuramodular contains BOTH side steps and the upper cab bar, so it must
#   be split by triangles rather than blacking the complete renderer
# - extra1 remains an explicit splash-guard fallback.
# ---------------------------------------------------------------------------
factory_method_match = re.search(
    r'''    private static bool IsFactoryBlackExteriorPart\(Renderer renderer\)\s*
    \{.*?
    \}\n\n''',
    text,
    re.S,
)
if factory_method_match is None:
    raise SystemExit("Could not locate Amarok factory-black classifier.")

factory_method = factory_method_match.group(0)
exact_factory_guard = '''        if (string.Equals(
                renderer.name,
                "vw_amorak_2018:bump_rear_ok_phong5_0",
                StringComparison.OrdinalIgnoreCase) ||
            renderer.name.StartsWith(
                "VolkswagenAmarok_FactoryBlack_",
                StringComparison.OrdinalIgnoreCase) ||
            renderer.name.IndexOf("extra1", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

'''
if '"vw_amorak_2018:bump_rear_ok_phong5_0"' not in factory_method:
    brace = factory_method.index("{\n") + 2
    factory_method = factory_method[:brace] + exact_factory_guard + factory_method[brace:]
    text = text[:factory_method_match.start()] + factory_method + text[factory_method_match.end():]

# Fields owned by the paint controller for runtime-created lower side-step geometry.
fields_marker = '    private readonly List<Material> ownedPanelMaterials = new List<Material>();\n'
fields_insert = fields_marker + '''    private readonly List<Mesh> ownedFactoryBlackMeshes = new List<Mesh>();
    private readonly List<Material> ownedFactoryBlackMaterials = new List<Material>();
    private bool explicitFactoryBlackGeometryPrepared;
'''
if "ownedFactoryBlackMeshes" not in text:
    if fields_marker not in text:
        raise SystemExit("Could not locate Amarok paint-controller owned material fields.")
    text = text.replace(fields_marker, fields_insert, 1)

# Split the aventuramodular source renderer before paint slots are discovered.
find_slots_signature = '''    private void FindPaintSlots()
    {
'''
if find_slots_signature not in text:
    raise SystemExit("Could not locate Amarok FindPaintSlots().")
if "PrepareExplicitFactoryBlackGeometry();" not in text:
    text = text.replace(
        find_slots_signature,
        find_slots_signature + "        PrepareExplicitFactoryBlackGeometry();\n",
        1,
    )

split_method = r'''    private void PrepareExplicitFactoryBlackGeometry()
    {
        if (explicitFactoryBlackGeometryPrepared)
            return;
        explicitFactoryBlackGeometryPrepared = true;

        Renderer? targetRenderer = null;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (string.Equals(
                    renderer.name,
                    "vw_amorak_2018:aventuramodular_phong5_0",
                    StringComparison.OrdinalIgnoreCase))
            {
                targetRenderer = renderer;
                break;
            }
        }
        if (targetRenderer == null)
        {
            context?.Logger.Warn(
                "VolkswagenAmarok paint: exact aventuramodular renderer was not found.");
            return;
        }

        var sourceFilter = targetRenderer.GetComponent<MeshFilter>();
        var sourceMesh = sourceFilter?.sharedMesh;
        if (sourceFilter == null || sourceMesh == null)
        {
            context?.Logger.Warn(
                "VolkswagenAmarok paint: aventuramodular renderer has no readable mesh.");
            return;
        }

        Transform? visual = null;
        for (var current = targetRenderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null)
            return;

        var sourceVertices = sourceMesh.vertices;
        var lowerBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var upperBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var lowerTriangles = 0;
        var upperTriangles = 0;
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            lowerBySubMesh[subMesh] = new List<int>();
            upperBySubMesh[subMesh] = new List<int>();
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                var centerLocal = (sourceVertices[a] + sourceVertices[b] + sourceVertices[c]) / 3f;
                var centerInVehicle = visual.InverseTransformPoint(
                    targetRenderer.transform.TransformPoint(centerLocal));

                // The side step/tube assembly is below the door sills. The same
                // source object also contains the tall cab/bed bar, which must stay
                // in VehicleColor. A wide vertical gap makes 0.72 m a stable split.
                var lower = centerInVehicle.y < 0.72f;
                var destination = lower ? lowerBySubMesh[subMesh] : upperBySubMesh[subMesh];
                destination.Add(a);
                destination.Add(b);
                destination.Add(c);
                if (lower) lowerTriangles++;
                else upperTriangles++;
            }
        }

        if (lowerTriangles == 0 || upperTriangles == 0)
        {
            context?.Logger.Warn(
                $"VolkswagenAmarok paint: aventuramodular split rejected " +
                $"lower={lowerTriangles} upper={upperTriangles}.");
            return;
        }

        var upperMesh = Instantiate(sourceMesh);
        upperMesh.name = sourceMesh.name + "_VehicleColorCabBar";
        upperMesh.subMeshCount = sourceMesh.subMeshCount;
        var lowerMesh = Instantiate(sourceMesh);
        lowerMesh.name = sourceMesh.name + "_FactoryBlackSideSteps";
        lowerMesh.subMeshCount = sourceMesh.subMeshCount;
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            upperMesh.SetTriangles(upperBySubMesh[subMesh], subMesh, true);
            lowerMesh.SetTriangles(lowerBySubMesh[subMesh], subMesh, true);
        }
        upperMesh.RecalculateBounds();
        lowerMesh.RecalculateBounds();
        sourceFilter.sharedMesh = upperMesh;
        ownedFactoryBlackMeshes.Add(upperMesh);
        ownedFactoryBlackMeshes.Add(lowerMesh);

        var lowerHost = new GameObject("VolkswagenAmarok_FactoryBlack_SideSteps");
        lowerHost.layer = targetRenderer.gameObject.layer;
        lowerHost.transform.SetParent(targetRenderer.transform, false);
        lowerHost.AddComponent<MeshFilter>().sharedMesh = lowerMesh;
        var lowerRenderer = lowerHost.AddComponent<MeshRenderer>();
        lowerRenderer.shadowCastingMode = targetRenderer.shadowCastingMode;
        lowerRenderer.receiveShadows = targetRenderer.receiveShadows;
        lowerRenderer.lightProbeUsage = targetRenderer.lightProbeUsage;
        lowerRenderer.reflectionProbeUsage = targetRenderer.reflectionProbeUsage;
        lowerRenderer.renderingLayerMask = targetRenderer.renderingLayerMask;

        var sourceMaterials = targetRenderer.sharedMaterials;
        var blackMaterials = new Material[sourceMaterials.Length];
        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            var source = sourceMaterials[index];
            if (source == null)
                continue;
            var material = Instantiate(source);
            material.name = source.name + "_FactoryBlackSideStep";
            if (IsRawAmarokBodyPaintMaterial(material))
            {
                if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, black);
                if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, black);
                if (material.HasProperty(BaseColorFactor)) material.SetColor(BaseColorFactor, black);
                if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
                if (material.HasProperty("baseColorTexture")) material.SetTexture("baseColorTexture", null);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
            }
            blackMaterials[index] = material;
            ownedFactoryBlackMaterials.Add(material);
        }
        lowerRenderer.sharedMaterials = blackMaterials;

        context?.Logger.Info(
            $"VolkswagenAmarok paint: split aventuramodular lower side steps " +
            $"lowerTriangles={lowerTriangles} upperCabBarTriangles={upperTriangles}.");
    }

'''
method_marker = "    private void ApplyFactoryBlackExteriorParts()\n"
if "private void PrepareExplicitFactoryBlackGeometry()" not in text:
    if method_marker not in text:
        raise SystemExit("Could not locate Amarok factory-black apply method.")
    text = text.replace(method_marker, split_method + method_marker, 1)

# Cleanup generated runtime meshes/materials with the existing paint-controller lifecycle.
destroy_marker = '''        ownedPanelMaterials.Clear();
    }
'''
destroy_replacement = '''        ownedPanelMaterials.Clear();
        foreach (var material in ownedFactoryBlackMaterials)
            if (material != null) Destroy(material);
        ownedFactoryBlackMaterials.Clear();
        foreach (var mesh in ownedFactoryBlackMeshes)
            if (mesh != null) Destroy(mesh);
        ownedFactoryBlackMeshes.Clear();
    }
'''
if "ownedFactoryBlackMaterials.Clear();" not in text:
    if destroy_marker not in text:
        raise SystemExit("Could not locate Amarok paint-controller OnDestroy cleanup.")
    text = text.replace(destroy_marker, destroy_replacement, 1)

MATERIALS.write_text(text, encoding="utf-8", newline="\n")

check = MATERIALS.read_text(encoding="utf-8")
required = [
    '"vw_amorak_2018:cam_EXT_GLASS_0"',
    "new Color(0.48f, 0.018f, 0.012f, 0.78f)",
    '"vw_amorak_2018:bump_rear_ok_phong5_0"',
    '"vw_amorak_2018:aventuramodular_phong5_0"',
    "PrepareExplicitFactoryBlackGeometry();",
    "centerInVehicle.y < 0.72f",
    '"VolkswagenAmarok_FactoryBlack_SideSteps"',
    'renderer.name.IndexOf("extra1"',
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit("Amarok seventh materials patch failed:\n- " + "\n- ".join(missing))

print("Bound the rear-lamp lens fix to exact renderer vw_amorak_2018:cam_EXT_GLASS_0.")
print("Forced the original rear-lamp lens texture through a deep OEM-red transparent tint.")
print("Forced vw_amorak_2018:bump_rear_ok_phong5_0 and splash-guard body slots factory black.")
print("Split vw_amorak_2018:aventuramodular_phong5_0 by geometry: low side steps black, upper cab bar remains VehicleColor.")
print("Volkswagen Amarok seventh materials preflight passed.")
