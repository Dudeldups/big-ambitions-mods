from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MATERIALS = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokMaterials.cs"

if not MATERIALS.is_file():
    raise SystemExit(f"Generated Amarok materials source is missing: {MATERIALS}")

text = MATERIALS.read_text(encoding="utf-8")

# Both remaining black parts live inside mixed source meshes:
# - aventuramodular_phong5_0 contains the body-color cab bar AND low side-step covers
# - the splash/mud guards are disconnected islands inside a body-paint renderer
#
# A renderer-wide property block can therefore never solve this correctly. Split
# disconnected mesh islands first, then let the normal VehicleColor path color only
# the remainder while the extracted low islands receive dedicated black materials.
method_pattern = re.compile(
    r'''    private void PrepareExplicitFactoryBlackGeometry\(\)\s*
    \{.*?
    \}\n\n
    private void ApplyFactoryBlackExteriorParts''',
    re.S,
)

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
            context?.Logger.Warn("VolkswagenAmarok paint: AmarokVisual is missing for black-part splitting.");
            return;
        }

        var sideStepSplits = 0;
        var mudGuardSplits = 0;
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null ||
                renderer.name.StartsWith("VolkswagenAmarok_FactoryBlack_", StringComparison.Ordinal))
                continue;

            var isAventura = string.Equals(
                renderer.name,
                "vw_amorak_2018:aventuramodular_phong5_0",
                StringComparison.OrdinalIgnoreCase);

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
                if (SplitFactoryBlackIslands(
                        renderer,
                        visual,
                        "SideSteps",
                        bounds =>
                            bounds.center.y < 0.78f &&
                            Mathf.Abs(bounds.center.x) > 0.42f &&
                            bounds.size.y < 0.85f))
                {
                    sideStepSplits++;
                }
                continue;
            }

            if (!hasBodyPaint ||
                string.Equals(
                    renderer.name,
                    "vw_amorak_2018:bump_rear_ok_phong5_0",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            // The mud guards are separate mesh islands even though Blender groups
            // them inside a body-paint object. Select only small, low, outer islands
            // around either axle; the large body/fender components fail the size test.
            if (SplitFactoryBlackIslands(
                    renderer,
                    visual,
                    "Mudguards",
                    bounds =>
                    {
                        var center = bounds.center;
                        var size = bounds.size;
                        var nearAxle =
                            (center.z > 1.00f && center.z < 2.15f) ||
                            (center.z < -0.65f && center.z > -1.95f);
                        return nearAxle &&
                               Mathf.Abs(center.x) > 0.68f &&
                               center.y < 0.68f &&
                               size.x < 0.80f &&
                               size.y < 0.95f &&
                               size.z < 0.80f;
                    }))
            {
                mudGuardSplits++;
            }
        }

        context?.Logger.Info(
            $"VolkswagenAmarok paint geometry split: sideStepRenderers={sideStepSplits}, " +
            $"mudGuardRenderers={mudGuardSplits}.");
    }

    private bool SplitFactoryBlackIslands(
        MeshRenderer sourceRenderer,
        Transform visual,
        string suffix,
        Func<Bounds, bool> shouldExtract)
    {
        var filter = sourceRenderer.GetComponent<MeshFilter>();
        var sourceMesh = filter?.sharedMesh;
        if (filter == null || sourceMesh == null || sourceMesh.vertexCount == 0)
            return false;

        var vertices = sourceMesh.vertices;
        var parent = new int[vertices.Length];
        for (var index = 0; index < parent.Length; index++)
            parent[index] = index;

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                Union(triangles[index], triangles[index + 1]);
                Union(triangles[index], triangles[index + 2]);
            }
        }

        var boundsByRoot = new Dictionary<int, Bounds>();
        var rootInitialized = new HashSet<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertexIndex = triangles[index + corner];
                    var root = Find(vertexIndex);
                    var point = visual.InverseTransformPoint(
                        sourceRenderer.transform.TransformPoint(vertices[vertexIndex]));
                    if (!rootInitialized.Add(root))
                    {
                        var bounds = boundsByRoot[root];
                        bounds.Encapsulate(point);
                        boundsByRoot[root] = bounds;
                    }
                    else
                    {
                        boundsByRoot[root] = new Bounds(point, Vector3.zero);
                    }
                }
            }
        }

        var extractedRoots = new HashSet<int>();
        foreach (var pair in boundsByRoot)
        {
            if (shouldExtract(pair.Value))
            {
                extractedRoots.Add(pair.Key);
                context?.Logger.Info(
                    $"VolkswagenAmarok paint extract source='{sourceRenderer.name}' " +
                    $"part='{suffix}' center={pair.Value.center} size={pair.Value.size}.");
            }
        }
        if (extractedRoots.Count == 0)
            return false;

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
                var target = extractedRoots.Contains(Find(triangles[index]))
                    ? blackBySubMesh[subMesh]
                    : keepBySubMesh[subMesh];
                target.Add(triangles[index]);
                target.Add(triangles[index + 1]);
                target.Add(triangles[index + 2]);
                if (ReferenceEquals(target, blackBySubMesh[subMesh]))
                    blackTriangles++;
                else
                    keptTriangles++;
            }
        }

        if (blackTriangles == 0 || keptTriangles == 0)
            return false;

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

        var host = new GameObject("VolkswagenAmarok_FactoryBlack_" + suffix + "_" + sourceRenderer.name);
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
        blackRenderer.sharedMaterials = blackMaterials;

        return true;
    }

    private void ApplyFactoryBlackExteriorParts'''

text, count = method_pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit(
        "Could not replace Amarok mixed-mesh black-part splitter with island-based implementation."
    )

MATERIALS.write_text(text, encoding="utf-8", newline="\n")

check = MATERIALS.read_text(encoding="utf-8")
required = [
    "SplitFactoryBlackIslands(",
    '"vw_amorak_2018:aventuramodular_phong5_0"',
    '"SideSteps"',
    '"Mudguards"',
    "var boundsByRoot = new Dictionary<int, Bounds>();",
    "Mathf.Abs(center.x) > 0.68f",
    '"VolkswagenAmarok_FactoryBlack_" + suffix',
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit("Amarok eighth black-geometry patch failed:\n- " + "\n- ".join(missing))

print("Replaced renderer-wide black heuristics with disconnected-island mesh splitting.")
print("Side-step covers are extracted from aventuramodular while the upper cab bar stays VehicleColor.")
print("Mudguards are extracted as small low outer islands from their shared body-paint mesh.")
print("Volkswagen Amarok eighth black-geometry preflight passed.")
