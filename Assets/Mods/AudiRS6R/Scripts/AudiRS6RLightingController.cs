#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
    private object? brakes;
    private object? blinkers;
    private Light? headlightBeam;
    private Light? leftHeadlightBeam;
    private Light? rightHeadlightBeam;
    private MeshRenderer? leftHeadlightOverlay;
    private MeshRenderer? rightHeadlightOverlay;
    private MeshRenderer? leftHeadlightLensOverlay;
    private MeshRenderer? rightHeadlightLensOverlay;
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

    public void Initialize(VehicleController controller)
    {
        if (initialized && vehicleController == controller)
            return;

        vehicleController = controller;
        LocateVehicleStateSources(controller);

        var renderers = controller.GetComponentsInChildren<MeshRenderer>(true);
        var frontLampRenderer = FindRenderer(renderers, FrontLampRendererName);
        var rearLampRenderer = FindRenderer(renderers, RearLampRendererName);
        var outerWindowRenderer = FindRenderer(renderers, OuterWindowRendererName);
        var innerWindowRenderer = FindRenderer(renderers, InnerWindowRendererName);
        ConfigureGlass(outerWindowRenderer, innerWindowRenderer);
        ConfigureLampSurface(frontLampRenderer, "AudiRS6R Front Lamp Housing", Color.white, preserveSourceShader: false);
        ConfigureLampSurface(
            rearLampRenderer,
            "AudiRS6R Rear Lamp Housing",
            new Color(0.45f, 0.025f, 0.015f, 1f),
            preserveSourceShader: true);
        ConfigureHeadlightBeams();

        leftHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x <= 0f,
            "LeftHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            copyBaseTexture: false);
        rightHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x > 0f,
            "RightHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            copyBaseTexture: false);
        leftHeadlightLensOverlay = CreateFunctionalOverlay(
            outerWindowRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.y <= 0.80f && position.x <= 0f,
            "LeftHeadlightLens",
            new Color(0.27f, 0.34f, 0.47f, 1f),
            copyBaseTexture: false);
        rightHeadlightLensOverlay = CreateFunctionalOverlay(
            outerWindowRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.y <= 0.80f && position.x > 0f,
            "RightHeadlightLens",
            new Color(0.27f, 0.34f, 0.47f, 1f),
            copyBaseTexture: false);
        leftTailLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x <= 0f,
            "LeftTailLight", new Color(0.20f, 0.0035f, 0.001f, 1f));
        rightTailLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x > 0f,
            "RightTailLight", new Color(0.20f, 0.0035f, 0.001f, 1f));
        leftBrakeLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x <= 0f,
            "LeftBrakeLight", new Color(0.78f, 0.012f, 0.0025f, 1f));
        rightBrakeLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 0.70f && position.y < 1.10f && position.x > 0f,
            "RightBrakeLight", new Color(0.78f, 0.012f, 0.0025f, 1f));
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
                Debug.LogWarning($"AudiRS6R lighting update failed: {ex.GetType().Name}: {ex.Message}");
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
        if (outerWindowRenderer != null)
        {
            outerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(outerWindowRenderer), "AudiRS6R Corrected Outer Glass",
                new Color(0.55f, 0.61f, 0.66f, 0.32f));
            outerWindowRenderer.enabled = true;
            configuredCount++;
        }

        if (innerWindowRenderer != null)
        {
            innerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(innerWindowRenderer) ?? FirstMaterial(outerWindowRenderer),
                "AudiRS6R Corrected Inner Glass", new Color(0.55f, 0.61f, 0.66f, 0.12f));
            innerWindowRenderer.enabled = true;
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
        SetFloatIfPresent(material, "transmissionFactor", 0.65f);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_TransparentCullMode", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_DOUBLESIDED_ON");
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
            Debug.LogWarning($"AudiRS6R could not clone headlight beam '{objectName}': {ex.GetType().Name}: {ex.Message}");
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
        renderer.sharedMaterial = material;
    }

    private MeshRenderer? CreateFunctionalOverlay(
        MeshRenderer? sourceRenderer,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix,
        Color activeColor,
        bool copyBaseTexture = true,
        float overlayScale = 1.0015f)
    {
        if (sourceRenderer == null)
            return null;

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
            return null;

        try
        {
            var overlayMesh = CreateFilteredMesh(
                sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix);
            if (overlayMesh == null)
                return null;

            var overlayObject = new GameObject("AudiRS6R_" + suffix);
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.transform.localScale = Vector3.one * overlayScale;
            overlayObject.layer = sourceRenderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = CreateUnlitMaterial(
                FirstMaterial(sourceRenderer), "AudiRS6R " + suffix, activeColor, copyBaseTexture);
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.enabled = false;

            generatedObjects.Add(overlayObject);
            generatedMeshes.Add(overlayMesh);
            return overlayRenderer;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"AudiRS6R could not create light overlay '{suffix}': {ex.GetType().Name}: {ex.Message}");
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

    private Material CreateUnlitMaterial(Material? source, string materialName, Color color, bool copyBaseTexture)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Texture") ?? source?.shader;
        if (shader == null)
            throw new InvalidOperationException("No compatible unlit shader is available.");

        var material = new Material(shader) { name = materialName };
        generatedMaterials.Add(material);
        if (copyBaseTexture)
            CopyBaseTexture(source, material);
        var hdrColor = color * 3.5f;
        hdrColor.a = 1f;
        SetColorIfPresent(material, "_UnlitColor", hdrColor);
        SetColorIfPresent(material, "_BaseColor", hdrColor);
        SetColorIfPresent(material, "_Color", hdrColor);
        SetColorIfPresent(material, "baseColorFactor", hdrColor);
        SetColorIfPresent(material, "_EmissiveColor", hdrColor);
        SetColorIfPresent(material, "_EmissionColor", hdrColor);
        SetFloatIfPresent(material, "_SurfaceType", 0f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 2450;
        return material;
    }

    private static void CopyBaseTexture(Material? source, Material destination)
    {
        if (source == null)
            return;

        Texture? baseTexture = null;
        if (source.HasProperty("_BaseColorMap"))
            baseTexture = source.GetTexture("_BaseColorMap");
        if (baseTexture == null && source.HasProperty("_BaseMap"))
            baseTexture = source.GetTexture("_BaseMap");
        if (baseTexture == null && source.HasProperty("_MainTex"))
            baseTexture = source.GetTexture("_MainTex");
        if (baseTexture == null && source.HasProperty("baseColorTexture"))
            baseTexture = source.GetTexture("baseColorTexture");

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
        var headlights = controlledByPlayer;
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

        SetRendererState(leftHeadlightOverlay, headlights && !(leftBlinker && blinkerFlash));
        SetRendererState(rightHeadlightOverlay, headlights && !(rightBlinker && blinkerFlash));
        SetRendererState(leftHeadlightLensOverlay, headlights);
        SetRendererState(rightHeadlightLensOverlay, headlights);
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

    }

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
