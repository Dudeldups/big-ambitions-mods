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
# source texture. Replace those exact blue source triangles with BA paint panels
# while leaving black grille/plastic/chrome source geometry untouched.
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

# The old existing-prefab path deliberately copied the previous AmarokDamageBody
# materials onto the newly baked body. That predates the source-blue replacement
# architecture and can preserve stale blue/paint material state. Rebuild the damage
# body from the freshly stripped source renderer and keep its current materials.
old_damage_material_block = '''            var oldDamageBody = FindTransform(root.transform, "AmarokDamageBody");
            var oldDamageMaterials = oldDamageBody?.GetComponent<MeshRenderer>()?.sharedMaterials ??
                                     Array.Empty<Material>();
            if (oldDamageBody != null)
                UnityEngine.Object.DestroyImmediate(oldDamageBody.gameObject);

            var damageBody = CreateDeformableBody(root, visual.gameObject);
            var damageRenderer = damageBody.GetComponent<MeshRenderer>();
            if (damageRenderer != null && oldDamageMaterials.Length > 0)
                damageRenderer.sharedMaterials = oldDamageMaterials;
'''
new_damage_material_block = '''            var oldDamageBody = FindTransform(root.transform, "AmarokDamageBody");
            if (oldDamageBody != null)
                UnityEngine.Object.DestroyImmediate(oldDamageBody.gameObject);

            // CreateDeformableBody now consumes the source-blue-stripped model.
            // Keep the current source materials instead of restoring stale damage
            // body material references from the previous prefab build.
            var damageBody = CreateDeformableBody(root, visual.gameObject);
'''
if old_damage_material_block in setup:
    setup = setup.replace(old_damage_material_block, new_damage_material_block, 1)
elif "oldDamageMaterials" in setup:
    raise SystemExit(
        "Could not replace stale AmarokDamageBody material preservation block."
    )

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
                    "VehiclePaint_Blue_",
                    StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            paintRenderers.Add(renderer);
        }

        if (paintRenderers.Count == 0)
            throw new InvalidOperationException(
                "VehiclePaint_Blue panels are missing from AmarokLightOverlays.glb.");

        var generatedMeshFolder = ModRoot + "/Models/GeneratedMeshes";
        if (!AssetDatabase.IsValidFolder(generatedMeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        // The old implementation placed VehiclePaint_Blue slightly above the
        // creator's original blue geometry. That only hid the blue. NPCs and
        // deformed player cars could expose the untouched source shell again.
        //
        // Replace it for real: use the auto-detected paint panels as triangle
        // masks, remove those exact world-space triangles from the corresponding
        // original source renderers, and keep the BA paint panels in their place.
        var strippedPanels = 0;
        var strippedTriangles = 0;
        foreach (var paintRenderer in paintRenderers)
        {
            var marker = "VehiclePaint_Blue_";
            var markerAt = paintRenderer.name.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);
            if (markerAt < 0)
                continue;
            var sourceKey = paintRenderer.name.Substring(markerAt + marker.Length);
            if (sourceKey.StartsWith(
                    "VolkswagenAmarok_",
                    StringComparison.OrdinalIgnoreCase))
            {
                sourceKey = sourceKey.Substring("VolkswagenAmarok_".Length);
            }

            MeshRenderer? sourceRenderer = null;
            foreach (var candidate in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (candidate == null ||
                    candidate.name.IndexOf(
                        "VehiclePaint_Blue",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (!string.Equals(
                        SanitizeAmarokSourceName(candidate.name),
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                sourceRenderer = candidate;
                break;
            }

            if (sourceRenderer == null)
                throw new InvalidOperationException(
                    $"Could not map Amarok paint panel '{paintRenderer.name}' " +
                    $"back to source renderer key '{sourceKey}'.");

            var panelFilter = paintRenderer.GetComponent<MeshFilter>();
            var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (panelFilter?.sharedMesh == null || sourceFilter?.sharedMesh == null)
                throw new InvalidOperationException(
                    $"Amarok paint/source mesh is missing for '{paintRenderer.name}'.");

            var safeKey = SanitizeAmarokAssetName(sourceKey);
            var originalPath =
                generatedMeshFolder + "/VolkswagenAmarok_SourceOriginal_" +
                safeKey + ".asset";
            var remainderPath =
                generatedMeshFolder + "/VolkswagenAmarok_SourceRemainder_" +
                safeKey + ".asset";

            var originalMesh = AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
            if (originalMesh == null)
            {
                // Capture the pristine imported source mesh once. Subsequent
                // builds always rebuild the remainder from this backup, so paint
                // mask changes never accumulate or permanently lose triangles.
                var originalClone = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
                originalClone.name =
                    sourceFilter.sharedMesh.name + "_OriginalBeforeBAPaintCutout";
                AssetDatabase.CreateAsset(originalClone, originalPath);
                originalMesh = originalClone;
            }

            var panelKeys = BuildAmarokWorldTriangleKeys(
                panelFilter,
                panelFilter.sharedMesh);
            if (panelKeys.Count == 0)
                throw new InvalidOperationException(
                    $"Amarok paint panel '{paintRenderer.name}' has no triangles.");

            var remainder = UnityEngine.Object.Instantiate(originalMesh);
            remainder.name = originalMesh.name + "_NoOriginalBlue";
            var originalVertices = originalMesh.vertices;
            var removedForPanel = 0;

            for (var subMesh = 0; subMesh < originalMesh.subMeshCount; subMesh++)
            {
                var triangles = originalMesh.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var a = triangles[index];
                    var b = triangles[index + 1];
                    var c = triangles[index + 2];
                    var key = BuildAmarokTriangleKey(
                        sourceRenderer.transform.TransformPoint(originalVertices[a]),
                        sourceRenderer.transform.TransformPoint(originalVertices[b]),
                        sourceRenderer.transform.TransformPoint(originalVertices[c]));

                    if (panelKeys.TryGetValue(key, out var remainingMatches) &&
                        remainingMatches > 0)
                    {
                        if (remainingMatches == 1)
                            panelKeys.Remove(key);
                        else
                            panelKeys[key] = remainingMatches - 1;
                        removedForPanel++;
                        continue;
                    }

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                }

                remainder.SetTriangles(kept, subMesh, true);
            }
            remainder.RecalculateBounds();

            var persistentRemainder =
                AssetDatabase.LoadAssetAtPath<Mesh>(remainderPath);
            if (persistentRemainder == null)
            {
                AssetDatabase.CreateAsset(remainder, remainderPath);
                persistentRemainder = remainder;
            }
            else
            {
                EditorUtility.CopySerialized(remainder, persistentRemainder);
                UnityEngine.Object.DestroyImmediate(remainder);
                EditorUtility.SetDirty(persistentRemainder);
            }

            sourceFilter.sharedMesh = persistentRemainder;
            EditorUtility.SetDirty(sourceFilter);
            strippedPanels++;
            strippedTriangles += removedForPanel;

            var unmatchedPanelTriangles = 0;
            foreach (var remaining in panelKeys.Values)
                unmatchedPanelTriangles += remaining;
            if (unmatchedPanelTriangles != 0)
                throw new InvalidOperationException(
                    $"Amarok paint panel '{paintRenderer.name}' still has " +
                    $"{unmatchedPanelTriangles} unmatched authored triangles after source cutout.");

            if (removedForPanel == 0)
            {
                Debug.LogWarning(
                    $"VolkswagenAmarok original-blue replacement panel=" +
                    $"'{paintRenderer.name}' matched zero source triangles; " +
                    $"source='{sourceRenderer.name}'.");
            }
            else
            {
                Debug.Log(
                    $"VolkswagenAmarok original-blue replacement panel=" +
                    $"'{paintRenderer.name}' source='{sourceRenderer.name}' " +
                    $"removedTriangles={removedForPanel}.");
            }
        }

        if (strippedPanels != paintRenderers.Count || strippedTriangles == 0)
            throw new InvalidOperationException(
                $"Amarok original-blue geometry replacement incomplete: " +
                $"panels={strippedPanels}/{paintRenderers.Count}, " +
                $"removedTriangles={strippedTriangles}.");

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
            $"VolkswagenAmarok original-blue paint replacement ready " +
            $"panels={paintRenderers.Count}, removedSourceTriangles={strippedTriangles}.");
    }

    private static string SanitizeAmarokSourceName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (char.IsLetterOrDigit(chars[index]) || chars[index] == '_')
                continue;
            chars[index] = '_';
        }
        return new string(chars).Trim('_');
    }

    private static string SanitizeAmarokAssetName(string value)
    {
        var sanitized = SanitizeAmarokSourceName(value);
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    private static Dictionary<string, int> BuildAmarokWorldTriangleKeys(
        MeshFilter filter,
        Mesh mesh)
    {
        // Preserve multiplicity. The source model contains some coincident
        // triangles/layers. A HashSet collapsed those duplicates and the cutout
        // pass then removed every source triangle with the same three positions,
        // which made unrelated inner/back-side geometry disappear.
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var vertices = mesh.vertices;
        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var triangles = mesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var key = BuildAmarokTriangleKey(
                    filter.transform.TransformPoint(vertices[triangles[index]]),
                    filter.transform.TransformPoint(vertices[triangles[index + 1]]),
                    filter.transform.TransformPoint(vertices[triangles[index + 2]]));
                keys.TryGetValue(key, out var count);
                keys[key] = count + 1;
            }
        }
        return keys;
    }

    private static string BuildAmarokTriangleKey(
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var points = new[]
        {
            QuantizeAmarokPoint(a),
            QuantizeAmarokPoint(b),
            QuantizeAmarokPoint(c),
        };
        Array.Sort(points, StringComparer.Ordinal);
        return points[0] + "|" + points[1] + "|" + points[2];
    }

    private static string QuantizeAmarokPoint(Vector3 point)
    {
        // 0.5 mm buckets tolerate tiny import/transform rounding while still
        // identifying the exact authored triangles from the source GLB.
        const float scale = 2000f;
        return
            Mathf.RoundToInt(point.x * scale) + ":" +
            Mathf.RoundToInt(point.y * scale) + ":" +
            Mathf.RoundToInt(point.z * scale);
    }

'''
paint_helper_start_marker = (
    "    private static void ConfigureOriginalBluePaintSurface("
)
paint_helper_start = setup.find(paint_helper_start_marker)
paint_helper_end = setup.find(
    helper_marker,
    paint_helper_start + len(paint_helper_start_marker)
    if paint_helper_start >= 0 else 0,
)
if paint_helper_start >= 0:
    if paint_helper_end < 0 or paint_helper_end <= paint_helper_start:
        raise SystemExit(
            "Could not locate end of existing Amarok original-blue paint helper."
        )
    setup = setup[:paint_helper_start] + paint_helper + setup[paint_helper_end:]
else:
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
        '"VehiclePaint_Blue_"',
        '"VolkswagenAmarok_BA_VehiclePaint"',
        'material.name.IndexOf("_BA_VehiclePaint"',
        "paintRenderer.transform.SetParent(visual, true);",
        "var paintRenderers = new List<MeshRenderer>();",
        '"VolkswagenAmarok_VehiclePaint_Blue"',
        "DestroyImmediate(current.gameObject);",
        "BuildAmarokWorldTriangleKeys(",
        "BuildAmarokTriangleKey(",
        "VolkswagenAmarok_SourceOriginal_",
        "VolkswagenAmarok_SourceRemainder_",
        "removedSourceTriangles",
        "CreateDeformableBody now consumes the source-blue-stripped model.",
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
print("Original blue source triangles are now physically removed and replaced by VehiclePaint_Blue panels instead of being hidden underneath them.")
print("Original black grille/plastic/chrome geometry keeps the source model appearance unless explicitly overridden.")
print("VehiclePaint_Blue is reparented into AmarokVisual and included in visible crash deformation.")
print("Front DRL/indicator meshes are explicitly included in crash deformation before the AmarokLightSources exclusion.")
print("Volkswagen Amarok original-blue paint preflight passed.")
