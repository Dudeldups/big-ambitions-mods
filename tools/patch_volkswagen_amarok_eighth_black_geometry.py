from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MATERIALS = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokMaterials.cs"

if not MATERIALS.is_file():
    raise SystemExit(f"Generated Amarok materials source is missing: {MATERIALS}")

text = MATERIALS.read_text(encoding="utf-8")

# The previous disconnected-island approach cannot work when the black plastic is
# topologically connected to a painted source object. The in-game log confirmed
# that no side-step or mudguard component was extracted:
#   sideStepRenderers=0, mudGuardRenderers=0
#
# Split by triangle position/normal instead. This deliberately removes selected
# triangles from the VehicleColor mesh and recreates those exact triangles in a
# separate black renderer, so sharing one Blender object/material is no longer a
# blocker.
replacement = r'''    private void PrepareExplicitFactoryBlackGeometry()
    {
        if (explicitFactoryBlackGeometryPrepared)
            return;
        explicitFactoryBlackGeometryPrepared = true;

        Transform? visual = null;
        foreach (var current in GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null)
        {
            context?.Logger.Warn(
                "VolkswagenAmarok paint: AmarokVisual is missing for black-part splitting.");
            return;
        }

        var sideStepTriangles = 0;
        var mudGuardTriangles = 0;
        var candidates = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var renderer in candidates)
        {
            if (renderer == null ||
                renderer.name.StartsWith(
                    "VolkswagenAmarok_FactoryBlack_",
                    StringComparison.Ordinal))
                continue;

            var isAventura = HasNameFragmentInHierarchy(renderer, "aventuramodular");
            var isRearBumper = HasNameFragmentInHierarchy(renderer, "bump_rear_ok");
            var hasBodyPaint = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && IsRawAmarokBodyPaintMaterial(material))
                {
                    hasBodyPaint = true;
                    break;
                }
            }

            if (isAventura)
            {
                sideStepTriangles += SplitFactoryBlackTriangles(
                    renderer,
                    visual,
                    "SideSteps",
                    (center, normal) =>
                        Mathf.Abs(center.x) > 0.52f &&
                        center.y > -0.20f &&
                        center.y < 0.82f &&
                        center.z > -1.55f &&
                        center.z < 1.55f);
                continue;
            }

            if (!hasBodyPaint || isRearBumper)
                continue;

            mudGuardTriangles += SplitFactoryBlackTriangles(
                renderer,
                visual,
                "Mudguards",
                (center, normal) =>
                {
                    var frontBehindWheel = center.z > 1.02f && center.z < 1.58f;
                    var rearBehindWheel = center.z > -2.02f && center.z < -1.42f;
                    return (frontBehindWheel || rearBehindWheel) &&
                           Mathf.Abs(center.x) > 0.70f &&
                           center.y > -0.20f &&
                           center.y < 0.72f &&
                           Mathf.Abs(normal.z) > 0.28f;
                });
        }

        context?.Logger.Info(
            $"VolkswagenAmarok paint triangle split: sideStepTriangles={sideStepTriangles}, " +
            $"mudGuardTriangles={mudGuardTriangles}.");
    }

    private static bool HasNameFragmentInHierarchy(
        Renderer renderer,
        string fragment)
    {
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private int SplitFactoryBlackTriangles(
        MeshRenderer sourceRenderer,
        Transform visual,
        string suffix,
        Func<Vector3, Vector3, bool> shouldExtract)
    {
        var filter = sourceRenderer.GetComponent<MeshFilter>();
        var sourceMesh = filter?.sharedMesh;
        if (filter == null || sourceMesh == null || sourceMesh.vertexCount == 0)
            return 0;

        var vertices = sourceMesh.vertices;
        var keepBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var blackBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var keptTriangles = 0;
        var blackTriangles = 0;

        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            keepBySubMesh[subMesh] = new List<int>();
            blackBySubMesh[subMesh] = new List<int>();
            var triangles = sourceMesh.GetTriangles(subMesh);

            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var ia = triangles[index];
                var ib = triangles[index + 1];
                var ic = triangles[index + 2];
                var aWorld = sourceRenderer.transform.TransformPoint(vertices[ia]);
                var bWorld = sourceRenderer.transform.TransformPoint(vertices[ib]);
                var cWorld = sourceRenderer.transform.TransformPoint(vertices[ic]);
                var a = visual.InverseTransformPoint(aWorld);
                var b = visual.InverseTransformPoint(bWorld);
                var c = visual.InverseTransformPoint(cWorld);
                var center = (a + b + c) / 3f;
                var normal = Vector3.Cross(b - a, c - a).normalized;

                var extracted = shouldExtract(center, normal);
                var target = extracted
                    ? blackBySubMesh[subMesh]
                    : keepBySubMesh[subMesh];
                target.Add(ia);
                target.Add(ib);
                target.Add(ic);

                if (extracted)
                    blackTriangles++;
                else
                    keptTriangles++;
            }
        }

        if (blackTriangles == 0 || keptTriangles == 0)
        {
            context?.Logger.Info(
                $"VolkswagenAmarok paint triangle candidate source='{sourceRenderer.name}' " +
                $"part='{suffix}' extracted={blackTriangles} kept={keptTriangles}.");
            return 0;
        }

        var remainder = Instantiate(sourceMesh);
        remainder.name = sourceMesh.name + "_VehicleColorRemainder";
        var blackMesh = Instantiate(sourceMesh);
        blackMesh.name = sourceMesh.name + "_FactoryBlack_" + suffix;
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            remainder.SetTriangles(keepBySubMesh[subMesh], subMesh, true);
            blackMesh.SetTriangles(blackBySubMesh[subMesh], subMesh, true);
        }
        remainder.RecalculateBounds();
        blackMesh.RecalculateBounds();

        filter.sharedMesh = remainder;
        ownedFactoryBlackMeshes.Add(remainder);
        ownedFactoryBlackMeshes.Add(blackMesh);

        var host = new GameObject(
            "VolkswagenAmarok_FactoryBlack_" + suffix + "_" + sourceRenderer.name);
        host.layer = sourceRenderer.gameObject.layer;
        host.transform.SetParent(sourceRenderer.transform, false);
        host.AddComponent<MeshFilter>().sharedMesh = blackMesh;

        var blackRenderer = host.AddComponent<MeshRenderer>();
        blackRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        blackRenderer.receiveShadows = sourceRenderer.receiveShadows;
        blackRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        blackRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        blackRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;

        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        var sourceMaterials = sourceRenderer.sharedMaterials;
        var blackMaterials = new Material[sourceMaterials.Length];
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            var source = sourceMaterials[index];
            if (source == null)
                continue;

            var material = Instantiate(source);
            material.name = source.name + "_FactoryBlack_" + suffix;
            if (material.HasProperty(BaseColor))
                material.SetColor(BaseColor, black);
            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, black);
            if (material.HasProperty(BaseColorFactor))
                material.SetColor(BaseColorFactor, black);
            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("baseColorTexture"))
                material.SetTexture("baseColorTexture", null);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", null);

            blackMaterials[index] = material;
            ownedFactoryBlackMaterials.Add(material);
        }
        blackRenderer.sharedMaterials = blackMaterials;

        context?.Logger.Info(
            $"VolkswagenAmarok paint extracted source='{sourceRenderer.name}' " +
            $"part='{suffix}' triangles={blackTriangles}.");
        return blackTriangles;
    }

'''

start_marker = "    private void PrepareExplicitFactoryBlackGeometry()"
end_marker = "    private void ApplyFactoryBlackExteriorParts()"
start = text.find(start_marker)
end = text.find(end_marker, start + len(start_marker)) if start >= 0 else -1
if start < 0 or end < 0 or end <= start:
    raise SystemExit(
        "Could not locate Amarok factory-black geometry method boundaries. "
        f"start={start} end={end}"
    )

text = text[:start] + replacement + text[end:]
MATERIALS.write_text(text, encoding="utf-8", newline="\n")

check = MATERIALS.read_text(encoding="utf-8")
required = [
    "SplitFactoryBlackTriangles(",
    'HasNameFragmentInHierarchy(renderer, "aventuramodular")',
    '"SideSteps"',
    '"Mudguards"',
    "frontBehindWheel",
    "rearBehindWheel",
    "Mathf.Abs(normal.z) > 0.28f",
    "paint triangle split: sideStepTriangles=",
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit(
        "Amarok eighth black-geometry patch failed:\n- " + "\n- ".join(missing)
    )

print("Replaced disconnected-island black-part detection with direct triangle extraction.")
print("Side-step covers are cut from the low aventuramodular geometry while the upper cab bar stays VehicleColor.")
print("Mudguards are cut by wheel-local position and front/back-facing triangle normals even when connected to the body mesh.")
print("Volkswagen Amarok eighth black-geometry preflight passed.")
