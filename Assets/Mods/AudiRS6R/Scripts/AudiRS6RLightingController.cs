#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class AudiRS6RLightingController : MonoBehaviour
{
    private const string FrontLampRendererName = "B:Light_Geo_lodA_B:Light_Geo_lodASG1_0";
    private const string InnerWindowRendererName = "B:WindowInside_Geo_lodA_B:Window_Geo_lodASG1_0";
    private const string OuterWindowRendererName = "B:Window_Geo_lodA_B:Window_Geo_lodASG1_0";
    private const string RearLampRendererName = "B:Window_Geo_lodA_red_glass_0";
    private const float BlinkerHalfPeriod = 0.42f;

    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();

    private VehicleController? vehicleController;
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private Light? headlightBeam;
    private Light? leftHeadlightBeam;
    private Light? rightHeadlightBeam;
    private MeshRenderer? leftHeadlightOverlay;
    private MeshRenderer? rightHeadlightOverlay;
    private MeshRenderer? leftTailLightOverlay;
    private MeshRenderer? rightTailLightOverlay;
    private MeshRenderer? leftBrakeLightOverlay;
    private MeshRenderer? rightBrakeLightOverlay;
    private MeshRenderer? centerBrakeLightOverlay;
    private MeshRenderer? leftFrontBlinkerOverlay;
    private MeshRenderer? rightFrontBlinkerOverlay;
    private MeshRenderer? leftRearBlinkerOverlay;
    private MeshRenderer? rightRearBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    private int lastHeadlightState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicleController == controller)
            return;

        vehicleController = controller;
        context = modContext;
        LocateVehicleStateSources(controller);

        var renderers = controller.GetComponentsInChildren<MeshRenderer>(true);
        var frontLampRenderer = FindRenderer(renderers, FrontLampRendererName);
        var rearLampRenderer = FindRenderer(renderers, RearLampRendererName);
        var outerWindowRenderer = FindRenderer(renderers, OuterWindowRendererName);
        var innerWindowRenderer = FindRenderer(renderers, InnerWindowRendererName);
        var glassCount = ConfigureGlass(outerWindowRenderer, innerWindowRenderer);
        // Keep the imported housing shader, textures and roughness visible in daylight.
        LogSurface("front-lamp-preserved", frontLampRenderer);
        ConfigureLampSurface(
            rearLampRenderer,
            "AudiRS6R Rear Lamp Housing",
            new Color(0.45f, 0.025f, 0.015f, 1f),
            preserveSourceShader: true);
        var beamCount = ConfigureHeadlightBeams();

        leftHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x <= 0f,
            "LeftHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            copyBaseTexture: false,
            overlayScale: 1.006f,
            additive: true,
            selectHeadlightSignatureComponents: true);
        rightHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x > 0f,
            "RightHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            copyBaseTexture: false,
            overlayScale: 1.006f,
            additive: true,
            selectHeadlightSignatureComponents: true);
        leftTailLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x <= 0f,
            "LeftTailLight", new Color(0.20f, 0.0035f, 0.001f, 1f),
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        rightTailLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x > 0f,
            "RightTailLight", new Color(0.20f, 0.0035f, 0.001f, 1f),
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        leftBrakeLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x <= 0f,
            "LeftBrakeLight", new Color(0.78f, 0.012f, 0.0025f, 1f),
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        rightBrakeLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x > 0f,
            "RightBrakeLight", new Color(0.78f, 0.012f, 0.0025f, 1f),
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        centerBrakeLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 1.10f,
            "CenterBrakeLight", new Color(0.78f, 0.012f, 0.0025f, 1f));
        leftFrontBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.z >= 0f && position.x <= 0f,
            "FrontLeftBlinker", new Color(1f, 0.14f, 0.002f, 1f), overlayScale: 1.004f);
        rightFrontBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.z >= 0f && position.x > 0f,
            "FrontRightBlinker", new Color(1f, 0.14f, 0.002f, 1f), overlayScale: 1.004f);
        leftRearBlinkerOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x <= 0f,
            "RearLeftBlinker", new Color(1f, 0.12f, 0.001f, 1f), overlayScale: 1.004f);
        rightRearBlinkerOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x > 0f,
            "RearRightBlinker", new Color(1f, 0.12f, 0.001f, 1f), overlayScale: 1.004f);

        initialized = true;
        LogInfo($"initialized glassRenderers={glassCount}/2 headlightBeams={beamCount}/2 " +
                $"overlays={generatedMeshes.Count}/11 headlightLensOverlays=0.");
        if (glassCount != 2 || beamCount != 2 || generatedMeshes.Count != 11)
            LogWarning("Lighting setup is incomplete; inspect the preceding material/overlay diagnostics.");
        if (controller.GetType().GetProperty("ShouldLightsBeOn", InstanceFields) == null)
            LogWarning("ShouldLightsBeOn is unavailable; automatic headlights cannot be read.");
        ApplyLightState();
    }

    private void Update()
    {
        if (!initialized)
            return;

        try
        {
            ApplyLightState();
        }
        catch (Exception ex)
        {
            if (!updateFailureReported)
            {
                updateFailureReported = true;
                LogWarning($"update failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void LocateVehicleStateSources(VehicleController controller)
    {
        foreach (var component in controller.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            var componentType = component.GetType();
            if (brakes == null)
                brakes = componentType.GetField("brakes", InstanceFields)?.GetValue(component);
            if (componentType.FullName == "Vehicles.Components.VehicleBlinker")
                blinkers = component;
        }

        foreach (var light in controller.GetComponentsInChildren<Light>(true))
        {
            if (light != null && string.Equals(light.name, "Spotlights", StringComparison.Ordinal))
            {
                headlightBeam = light;
                break;
            }
        }
    }

    private int ConfigureGlass(MeshRenderer? outerWindowRenderer, MeshRenderer? innerWindowRenderer)
    {
        var configuredCount = 0;
        LogSurface("outer-glass-source", outerWindowRenderer);
        LogSurface("inner-glass-source", innerWindowRenderer);
        if (outerWindowRenderer != null)
        {
            outerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(outerWindowRenderer), "AudiRS6R Corrected Outer Glass",
                new Color(0f, 0f, 0f, 0.25f));
            outerWindowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outerWindowRenderer.enabled = true;
            LogSurface("outer-glass-configured", outerWindowRenderer);
            configuredCount++;
        }

        if (innerWindowRenderer != null)
        {
            innerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(innerWindowRenderer) ?? FirstMaterial(outerWindowRenderer),
                "AudiRS6R Corrected Inner Glass", new Color(0f, 0f, 0f, 0.12f));
            innerWindowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            innerWindowRenderer.enabled = true;
            LogSurface("inner-glass-configured", innerWindowRenderer);
            configuredCount++;
        }

        return configuredCount;
    }

    private Material CloneAndConfigureGlass(Material? source, string materialName, Color tint)
    {
        if (source == null)
            throw new InvalidOperationException("Audi glass source material is missing.");

        var material = new Material(source) { name = materialName };
        generatedMaterials.Add(material);
        SetColorIfPresent(material, "_BaseColor", tint);
        SetColorIfPresent(material, "_Color", tint);
        SetColorIfPresent(material, "baseColorFactor", tint);
        // HDRP requires blend/depth state as well as alpha. Use ordinary alpha
        // transparency so glass does not depend on screen-space refraction.
        SetFloatIfPresent(material, "transmissionFactor", 0f);
        SetFloatIfPresent(material, "_SurfaceType", 1f);
        SetFloatIfPresent(material, "_BlendMode", 0f);
        // HDRP applies the source alpha inside the shader.
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_TransparentZWrite", 0f);
        SetFloatIfPresent(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        SetFloatIfPresent(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloatIfPresent(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloatIfPresent(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloatIfPresent(material, "_TransparentBackfaceEnable", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_TransparentCullMode", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        if (!material.HasProperty("_SurfaceType") || !material.HasProperty("_SrcBlend") ||
            !material.HasProperty("_DstBlend") || !material.HasProperty("_ZWrite"))
            LogWarning($"Glass material '{materialName}' uses unexpected shader '{material.shader.name}'; " +
                       "transparent rendering controls are missing.");
        return material;
    }

    private int ConfigureHeadlightBeams()
    {
        if (headlightBeam == null)
            return 0;

        headlightBeam.enabled = false;
        leftHeadlightBeam = CloneHeadlightBeam(headlightBeam, -0.82f, "AudiRS6R_LeftHeadlightBeam");
        rightHeadlightBeam = CloneHeadlightBeam(headlightBeam, 0.82f, "AudiRS6R_RightHeadlightBeam");
        return (leftHeadlightBeam != null ? 1 : 0) + (rightHeadlightBeam != null ? 1 : 0);
    }

    private Light? CloneHeadlightBeam(Light source, float localX, string objectName)
    {
        try
        {
            var clone = Instantiate(source.gameObject, source.transform.parent, false);
            clone.name = objectName;
            clone.transform.localPosition = new Vector3(localX, 0.88f, 2.38f);
            clone.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            clone.layer = source.gameObject.layer;

            var light = clone.GetComponent<Light>();
            if (light == null)
            {
                Destroy(clone);
                return null;
            }

            light.enabled = false;
            light.cookie = null;
            light.range = 45f;
            light.spotAngle = 68f;
            light.innerSpotAngle = 42f;
            light.colorTemperature = 5000f;
            light.useColorTemperature = true;
            generatedObjects.Add(clone);
            return light;
        }
        catch (Exception ex)
        {
            LogWarning($"could not clone headlight beam '{objectName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void ConfigureLampSurface(
        MeshRenderer? renderer,
        string materialName,
        Color baseColor,
        bool preserveSourceShader)
    {
        if (renderer == null)
            return;

        var source = FirstMaterial(renderer);
        if (source == null)
            return;

        var shader = preserveSourceShader
            ? source.shader
            : Shader.Find("HDRP/Lit") ?? Shader.Find("High Definition Render Pipeline/Lit") ?? source.shader;
        var material = new Material(shader) { name = materialName };
        generatedMaterials.Add(material);
        if (preserveSourceShader)
            CopyBaseTexture(source, material);
        SetColorIfPresent(material, "baseColorFactor", baseColor);
        SetColorIfPresent(material, "_BaseColor", baseColor);
        SetColorIfPresent(material, "_Color", baseColor);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        material.EnableKeyword("_DISABLE_DECALS");
        renderer.sharedMaterial = material;
    }

    private MeshRenderer? CreateFunctionalOverlay(
        MeshRenderer? sourceRenderer,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix,
        Color activeColor,
        bool copyBaseTexture = true,
        float overlayScale = 1.0015f,
        bool additive = false,
        bool selectHeadlightSignatureComponents = false,
        bool selectRearLampSignatureComponents = false)
    {
        if (sourceRenderer == null)
        {
            LogWarning($"overlay '{suffix}' has no source renderer.");
            return null;
        }

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
        {
            LogWarning($"overlay '{suffix}' has no source mesh on '{sourceRenderer.name}'.");
            return null;
        }

        try
        {
            var overlayMesh = selectHeadlightSignatureComponents
                ? CreateHeadlightSignatureMesh(
                    sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix)
                : selectRearLampSignatureComponents
                    ? CreateRearLampSignatureMesh(
                        sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix)
                    : CreateFilteredMesh(
                        sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix);
            if (overlayMesh == null)
            {
                LogWarning($"overlay '{suffix}' selected no triangles on '{sourceRenderer.name}'.");
                return null;
            }

            var overlayObject = new GameObject("AudiRS6R_" + suffix);
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.transform.localScale = Vector3.one * overlayScale;
            overlayObject.layer = sourceRenderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = CreateUnlitMaterial(
                FirstMaterial(sourceRenderer), "AudiRS6R " + suffix, activeColor, copyBaseTexture,
                additive);
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.enabled = false;

            generatedObjects.Add(overlayObject);
            generatedMeshes.Add(overlayMesh);
            return overlayRenderer;
        }
        catch (Exception ex)
        {
            LogWarning($"could not create light overlay '{suffix}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private Mesh? CreateFilteredMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix)
    {
        var vertices = source.vertices;
        if (vertices.Length == 0 || vehicleController == null)
            return null;

        var vehicleTransform = vehicleController.transform;
        var vehiclePositions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            vehiclePositions[index] = vehicleTransform.InverseTransformPoint(
                sourceRenderer.transform.TransformPoint(vertices[index]));
        }

        var triangles = new List<int>();
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var sourceTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var center = (vehiclePositions[a] + vehiclePositions[b] + vehiclePositions[c]) / 3f;
                if (!includeTriangleCenter(center))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        if (triangles.Count == 0)
            return null;

        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = source.uv,
            uv2 = source.uv2
        };
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh? CreateHeadlightSignatureMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix)
    {
        var vertices = source.vertices;
        var uvs = source.uv;
        if (vertices.Length == 0 || uvs.Length != vertices.Length || vehicleController == null)
            return null;

        var vehicleTransform = vehicleController.transform;
        var bounds = source.bounds;
        var selectedTriangles = new List<int>();
        var atlasBandTriangles = 0;
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var subMeshTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < subMeshTriangles.Length; index += 3)
            {
                var a = subMeshTriangles[index];
                var b = subMeshTriangles[index + 1];
                var c = subMeshTriangles[index + 2];
                var uvCenter = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                if (uvCenter.x < 0.50f || uvCenter.x > 0.60f)
                    continue;

                atlasBandTriangles++;
                var localCenter = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                var normalizedHeight = Mathf.InverseLerp(bounds.min.y, bounds.max.y, localCenter.y);
                if (normalizedHeight < 0.25f)
                    continue;

                var vehicleCenter = vehicleTransform.InverseTransformPoint(
                    sourceRenderer.transform.TransformPoint(localCenter));
                if (!includeTriangleCenter(vehicleCenter))
                    continue;

                selectedTriangles.Add(a);
                selectedTriangles.Add(b);
                selectedTriangles.Add(c);
            }
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"headlight-signature suffix='{suffix}' selected=0 atlasBand={atlasBandTriangles} " +
                       $"vertices={vertices.Length} bounds={bounds}.");
            return null;
        }

        LogInfo($"headlight-signature suffix='{suffix}' selectedTriangles={selectedTriangles.Count / 3} " +
                $"atlasBandTriangles={atlasBandTriangles} bounds={bounds}.");
        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = uvs,
            uv2 = source.uv2
        };
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh? CreateRearLampSignatureMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeComponentCenter,
        string suffix)
    {
        var vertices = source.vertices;
        if (vertices.Length == 0 || vehicleController == null)
            return null;

        var vehicleTransform = vehicleController.transform;
        var sourceTransform = sourceRenderer.transform;
        var vehiclePositions = new Vector3[vertices.Length];
        var vehicleNormals = new Vector3[vertices.Length];
        var sourceNormals = source.normals;
        for (var index = 0; index < vertices.Length; index++)
        {
            vehiclePositions[index] = vehicleTransform.InverseTransformPoint(
                sourceTransform.TransformPoint(vertices[index]));
            if (sourceNormals.Length == vertices.Length)
            {
                vehicleNormals[index] = vehicleTransform.InverseTransformDirection(
                    sourceTransform.TransformDirection(sourceNormals[index])).normalized;
            }
        }

        var parents = new int[vertices.Length];
        for (var index = 0; index < parents.Length; index++)
            parents[index] = index;

        var allTriangles = new List<int>();
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var subMeshTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < subMeshTriangles.Length; index += 3)
            {
                var a = subMeshTriangles[index];
                var b = subMeshTriangles[index + 1];
                var c = subMeshTriangles[index + 2];
                UnionVertices(parents, a, b);
                UnionVertices(parents, a, c);
                allTriangles.Add(a);
                allTriangles.Add(b);
                allTriangles.Add(c);
            }
        }

        var components = new Dictionary<int, List<int>>();
        for (var index = 0; index + 2 < allTriangles.Count; index += 3)
        {
            var root = FindVertexRoot(parents, allTriangles[index]);
            if (!components.TryGetValue(root, out var componentTriangles))
            {
                componentTriangles = new List<int>();
                components.Add(root, componentTriangles);
            }

            componentTriangles.Add(allTriangles[index]);
            componentTriangles.Add(allTriangles[index + 1]);
            componentTriangles.Add(allTriangles[index + 2]);
        }

        var selectedTriangles = new List<int>();
        var selectedBars = 0;
        var selectedTeeth = 0;
        foreach (var component in components.Values)
        {
            var componentVertices = new HashSet<int>(component);
            using var vertexEnumerator = componentVertices.GetEnumerator();
            if (!vertexEnumerator.MoveNext())
                continue;

            var componentBounds = new Bounds(vehiclePositions[vertexEnumerator.Current], Vector3.zero);
            var normalSum = Vector3.zero;
            foreach (var vertexIndex in componentVertices)
            {
                componentBounds.Encapsulate(vehiclePositions[vertexIndex]);
                normalSum += vehicleNormals[vertexIndex];
            }

            var center = componentBounds.center;
            var size = componentBounds.size;
            var absMinX = Mathf.Min(Mathf.Abs(componentBounds.min.x), Mathf.Abs(componentBounds.max.x));
            var absMaxX = Mathf.Max(Mathf.Abs(componentBounds.min.x), Mathf.Abs(componentBounds.max.x));
            var averageNormal = normalSum.sqrMagnitude > 0f ? normalSum.normalized : Vector3.zero;
            var isRearward = averageNormal.z <= -0.60f;
            var isStraightBar =
                absMinX >= 0.30f && absMaxX <= 0.77f &&
                componentBounds.min.z >= -2.40f && componentBounds.max.z <= -2.15f &&
                componentBounds.min.y >= 0.848f && componentBounds.max.y <= 0.890f &&
                size.y >= 0.015f && size.y <= 0.035f && size.x >= 0.10f;
            var isTooth =
                absMinX >= 0.40f && absMaxX <= 0.76f && componentBounds.max.z < -2.10f &&
                size.x <= 0.035f && size.y >= 0.018f && size.y <= 0.040f &&
                componentBounds.min.y >= 0.795f &&
                componentBounds.max.y <= 0.838f;

            if (!includeComponentCenter(center) || (!isStraightBar && !(isTooth && isRearward)))
            {
                continue;
            }

            selectedTriangles.AddRange(component);
            if (isStraightBar)
                selectedBars++;
            else
                selectedTeeth++;
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"rear-lamp-signature suffix='{suffix}' selected=0 " +
                       $"components={components.Count} vertices={vertices.Length}.");
            return null;
        }

        LogInfo($"rear-lamp-signature suffix='{suffix}' selectedTriangles={selectedTriangles.Count / 3} " +
                $"barComponents={selectedBars} toothComponents={selectedTeeth} " +
                $"components={components.Count}.");
        if (selectedBars != 2 || selectedTeeth != 50 || selectedTriangles.Count / 3 != 546)
        {
            LogWarning($"rear-lamp-signature suffix='{suffix}' expected bars=2 teeth=50 triangles=546 " +
                       $"but selected bars={selectedBars} teeth={selectedTeeth} " +
                       $"triangles={selectedTriangles.Count / 3}.");
        }
        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = source.uv,
            uv2 = source.uv2
        };
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int FindVertexRoot(int[] parents, int vertex)
    {
        while (parents[vertex] != vertex)
        {
            parents[vertex] = parents[parents[vertex]];
            vertex = parents[vertex];
        }

        return vertex;
    }

    private static void UnionVertices(int[] parents, int first, int second)
    {
        var firstRoot = FindVertexRoot(parents, first);
        var secondRoot = FindVertexRoot(parents, second);
        if (firstRoot != secondRoot)
            parents[secondRoot] = firstRoot;
    }

    private Material CreateUnlitMaterial(
        Material? source,
        string materialName,
        Color color,
        bool copyBaseTexture,
        bool additive)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Texture") ?? source?.shader;
        if (shader == null)
            throw new InvalidOperationException("No compatible unlit shader is available.");

        var material = new Material(shader) { name = materialName };
        generatedMaterials.Add(material);
        if (copyBaseTexture)
        {
            CopyBaseTexture(source, material);
        }

        var hdrColor = color * (additive ? 6f : 3.5f);
        hdrColor.a = 1f;
        SetColorIfPresent(material, "_UnlitColor", hdrColor);
        SetColorIfPresent(material, "_BaseColor", hdrColor);
        SetColorIfPresent(material, "_Color", hdrColor);
        SetColorIfPresent(material, "baseColorFactor", hdrColor);
        SetColorIfPresent(material, "_EmissiveColor", hdrColor);
        SetColorIfPresent(material, "_EmissionColor", hdrColor);
        SetFloatIfPresent(material, "_SurfaceType", additive ? 1f : 0f);
        SetFloatIfPresent(material, "_ZWrite", additive ? 0f : 1f);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        material.EnableKeyword("_EMISSION");
        if (additive)
            ConfigureAdditiveTransparency(material);
        else
            material.renderQueue = 2450;
        return material;
    }

    private void ConfigureAdditiveTransparency(Material material)
    {
        SetFloatIfPresent(material, "_BlendMode", 1f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_AlphaDstBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_TransparentZWrite", 0f);
        // The imported headlamp shell writes opaque depth in front of the modeled light
        // guides. Draw the selected guide faces through that shell, then gate the overlay
        // to front-side cameras in ApplyLightState so it cannot show through the car.
        SetFloatIfPresent(material, "_ZTestTransparent", (float)CompareFunction.Always);
        SetFloatIfPresent(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.Always);
        SetFloatIfPresent(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloatIfPresent(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
        SetFloatIfPresent(material, "_CullMode", (float)CullMode.Off);
        SetFloatIfPresent(material, "_CullModeForward", (float)CullMode.Off);
        SetFloatIfPresent(material, "_TransparentCullMode", (float)CullMode.Off);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.doubleSidedGI = true;
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent + 20;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        LogInfo($"additive-material name='{material.name}' shader='{material.shader.name}' " +
                $"cull={ReadFloat(material, "_CullMode")} " +
                $"forwardCull={ReadFloat(material, "_CullModeForward")} " +
                $"transparentCull={ReadFloat(material, "_TransparentCullMode")} " +
                $"zTest={ReadFloat(material, "_ZTestTransparent")} doubleSided=true.");
    }

    private static void CopyBaseTexture(Material? source, Material destination)
    {
        AssignBaseTexture(destination, FindBaseTexture(source));
    }

    private static Texture? FindBaseTexture(Material? source)
    {
        if (source == null)
            return null;

        Texture? baseTexture = null;
        if (source.HasProperty("_BaseColorMap"))
            baseTexture = source.GetTexture("_BaseColorMap");
        if (baseTexture == null && source.HasProperty("_BaseMap"))
            baseTexture = source.GetTexture("_BaseMap");
        if (baseTexture == null && source.HasProperty("_MainTex"))
            baseTexture = source.GetTexture("_MainTex");
        if (baseTexture == null && source.HasProperty("baseColorTexture"))
            baseTexture = source.GetTexture("baseColorTexture");
        return baseTexture;
    }

    private static void AssignBaseTexture(Material destination, Texture? baseTexture)
    {
        SetTextureIfPresent(destination, "_UnlitColorMap", baseTexture);
        SetTextureIfPresent(destination, "_BaseColorMap", baseTexture);
        SetTextureIfPresent(destination, "_BaseMap", baseTexture);
        SetTextureIfPresent(destination, "_MainTex", baseTexture);
        SetTextureIfPresent(destination, "baseColorTexture", baseTexture);
        SetTextureIfPresent(destination, "_EmissiveColorMap", baseTexture);
        SetTextureIfPresent(destination, "_EmissionMap", baseTexture);
    }

    private void ApplyLightState()
    {
        var controlledByPlayer = vehicleController != null && vehicleController.controlledByPlayer;
        var automaticHeadlights = GetBoolProperty(vehicleController, "ShouldLightsBeOn");
        var headlights = controlledByPlayer && automaticHeadlights;
        var tailLights = controlledByPlayer && automaticHeadlights;
        var rawBraking = GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking");
        var braking = controlledByPlayer && rawBraking;
        var leftBlinker = controlledByPlayer && GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = controlledByPlayer && GetBoolField(blinkers, "_isRightBlinkerOn");
        var isBlinking = leftBlinker || rightBlinker;

        if (isBlinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var blinkerFlash = isBlinking &&
                           Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt, BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = isBlinking;

        var frontSignatureVisible = headlights && IsCameraOnFrontSide();
        SetRendererState(leftHeadlightOverlay, frontSignatureVisible && !(leftBlinker && blinkerFlash));
        SetRendererState(rightHeadlightOverlay, frontSignatureVisible && !(rightBlinker && blinkerFlash));
        if (headlightBeam != null)
            headlightBeam.enabled = false;
        SetLightState(leftHeadlightBeam, controlledByPlayer && automaticHeadlights);
        SetLightState(rightHeadlightBeam, controlledByPlayer && automaticHeadlights);
        SetRendererState(leftTailLightOverlay, tailLights && !braking && !(leftBlinker && blinkerFlash));
        SetRendererState(rightTailLightOverlay, tailLights && !braking && !(rightBlinker && blinkerFlash));
        SetRendererState(leftBrakeLightOverlay, braking && !(leftBlinker && blinkerFlash));
        SetRendererState(rightBrakeLightOverlay, braking && !(rightBlinker && blinkerFlash));
        SetRendererState(centerBrakeLightOverlay, braking);
        SetRendererState(leftFrontBlinkerOverlay, leftBlinker && blinkerFlash);
        SetRendererState(leftRearBlinkerOverlay, leftBlinker && blinkerFlash);
        SetRendererState(rightFrontBlinkerOverlay, rightBlinker && blinkerFlash);
        SetRendererState(rightRearBlinkerOverlay, rightBlinker && blinkerFlash);

        var headlightState = (controlledByPlayer ? 1 : 0) | (automaticHeadlights ? 2 : 0);
        if (headlightState != lastHeadlightState)
        {
            lastHeadlightState = headlightState;
            LogInfo($"headlight-state playerControlled={controlledByPlayer} automaticLights={automaticHeadlights} " +
                    $"signatureOverlays={headlights} frontCameraVisible={frontSignatureVisible} " +
                    $"lensOverlays=false beams={headlights}.");
        }
    }

    private bool IsCameraOnFrontSide()
    {
        if (vehicleController == null)
            return false;

        var activeCamera = Camera.main;
        if (activeCamera == null)
            return true;

        return vehicleController.transform.InverseTransformPoint(activeCamera.transform.position).z >= 0f;
    }

    private void LogSurface(string operation, MeshRenderer? renderer)
    {
        var material = FirstMaterial(renderer);
        if (renderer == null || material == null)
        {
            LogWarning($"surface operation='{operation}' renderer='{renderer?.name ?? "missing"}' material=missing.");
            return;
        }

        var color = material.HasProperty("baseColorFactor") ? material.GetColor("baseColorFactor") :
            material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
        var texture = material.HasProperty("baseColorTexture") ? material.GetTexture("baseColorTexture") :
            material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : null;
        LogInfo($"surface operation='{operation}' renderer='{renderer.name}' material='{material.name}' " +
                $"shader='{material.shader.name}' supported={material.shader.isSupported} color={color} " +
                $"texture='{texture?.name ?? "none"}' queue={material.renderQueue} " +
                $"transparent={material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")} " +
                $"srcBlend={ReadFloat(material, "_SrcBlend")} dstBlend={ReadFloat(material, "_DstBlend")} " +
                $"zWrite={ReadFloat(material, "_ZWrite")}.");
        if (!material.shader.isSupported)
            LogWarning($"surface operation='{operation}' shader '{material.shader.name}' is unsupported.");
        if (operation == "front-lamp-preserved" && texture == null)
            LogWarning("The imported front lamp material has no base texture; model detail may be missing.");
    }

    private static string ReadFloat(Material material, string propertyName) =>
        material.HasProperty(propertyName) ? material.GetFloat(propertyName).ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture) : "missing";

    private void LogInfo(string message) =>
        context?.Logger.Info($"AudiRS6R lighting vehicle='{vehicleController?.name}' " +
                             $"instance={vehicleController?.GetInstanceID()}: {message}");

    private void LogWarning(string message) =>
        context?.Logger.Warn($"AudiRS6R lighting vehicle='{vehicleController?.name}' " +
                             $"instance={vehicleController?.GetInstanceID()}: {message}");

    private static void SetRendererState(Renderer? renderer, bool active)
    {
        if (renderer != null && renderer.enabled != active)
            renderer.enabled = active;
    }

    private static void SetLightState(Light? light, bool active)
    {
        if (light != null && light.enabled != active)
            light.enabled = active;
    }

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string objectName)
    {
        foreach (var renderer in renderers)
        {
            if (renderer != null && string.Equals(renderer.name, objectName, StringComparison.Ordinal))
                return renderer;
        }
        return null;
    }

    private static Material? FirstMaterial(Renderer? renderer)
    {
        if (renderer == null)
            return null;
        var materials = renderer.sharedMaterials;
        return materials.Length > 0 ? materials[0] : null;
    }

    private static bool GetBoolField(object? target, string fieldName) =>
        target != null && target.GetType().GetField(fieldName, InstanceFields)?.GetValue(target) is bool value && value;

    private static bool GetBoolProperty(object? target, string propertyName) =>
        target != null && target.GetType().GetProperty(propertyName, InstanceFields)?.GetValue(target) is bool value && value;

    private static bool GetBoolMethod(object? target, string methodName) =>
        target != null &&
        target.GetType().GetMethod(methodName, InstanceFields, null, Type.EmptyTypes, null)?.Invoke(target, null) is bool value && value;

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName)) material.SetColor(propertyName, value);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
    }

    private static void SetTextureIfPresent(Material material, string propertyName, Texture? value)
    {
        if (value != null && material.HasProperty(propertyName)) material.SetTexture(propertyName, value);
    }


    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var material in generatedMaterials)
            if (material != null) Destroy(material);
        foreach (var mesh in generatedMeshes)
            if (mesh != null) Destroy(mesh);
    }
}
