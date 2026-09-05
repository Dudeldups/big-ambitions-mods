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

    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();

    private VehicleController? vehicleController;
    private object? brakes;
    private object? blinkers;
    private Light? headlightBeam;
    private Material? frontLampMaterial;
    private Material? headlightOverlayMaterial;
    private Material? rearLampMaterial;
    private Material? leftFrontBlinkerMaterial;
    private Material? rightFrontBlinkerMaterial;
    private Material? leftRearBlinkerMaterial;
    private Material? rightRearBlinkerMaterial;
    private bool initialized;
    private bool lastHeadlights;
    private bool lastBrakes;
    private bool lastLeftBlinker;
    private bool lastRightBlinker;
    private bool lastBlinkerFlash;

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

        var glassConfigured = ConfigureGlass(outerWindowRenderer, innerWindowRenderer);
        frontLampMaterial = ConfigureLampSurface(
            frontLampRenderer,
            "AudiRS6R Front Lamps",
            Color.white,
            new Color(0.95f, 0.95f, 0.95f, 1f),
            transparent: false);
        headlightOverlayMaterial = CreateHeadlightOverlay(frontLampRenderer);
        rearLampMaterial = ConfigureLampSurface(
            rearLampRenderer,
            "AudiRS6R Rear Lamps",
            new Color(1f, 0.025f, 0.01f),
            new Color(0.42f, 0.005f, 0.005f, 0.52f),
            transparent: true);

        leftFrontBlinkerMaterial = CreateBlinkerOverlay(frontLampRenderer, true, "FrontLeft");
        rightFrontBlinkerMaterial = CreateBlinkerOverlay(frontLampRenderer, false, "FrontRight");
        leftRearBlinkerMaterial = CreateBlinkerOverlay(rearLampRenderer, true, "RearLeft");
        rightRearBlinkerMaterial = CreateBlinkerOverlay(rearLampRenderer, false, "RearRight");

        initialized = true;
        ApplyLightState(forceLog: true);

        AudiRS6RDiagnostics.Vehicle(
            "LIGHTING_CONFIG",
            $"vehicleId=\"{controller.vehicleInstance?.id ?? "<none>"}\" " +
            $"frontLampRenderer={frontLampRenderer != null} rearLampRenderer={rearLampRenderer != null} " +
            $"outerGlassRenderer={outerWindowRenderer != null} innerGlassDisabled={innerWindowRenderer != null && !innerWindowRenderer.enabled} " +
            $"glassConfigured={glassConfigured} headlightBeam={headlightBeam != null} brakesSource={brakes != null} " +
            $"blinkerSource={blinkers != null} blinkerOverlays={CountBlinkerOverlays()} " +
            $"shader=\"{frontLampMaterial?.shader?.name ?? "<none>"}\"");
    }

    private void Update()
    {
        if (!initialized)
            return;

        try
        {
            ApplyLightState(forceLog: false);
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error(nameof(AudiRS6RLightingController) + ".Update", ex);
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
        if (innerWindowRenderer != null)
        {
            innerWindowRenderer.enabled = false;
            configuredCount++;
        }

        if (outerWindowRenderer == null)
            return configuredCount;

        var original = FirstMaterial(outerWindowRenderer);
        var glass = CreateCompatibleMaterial(
            original,
            "AudiRS6R Corrected Glass",
            new Color(0.11f, 0.15f, 0.18f, 0.3f),
            transparent: true,
            copyBaseTexture: false);
        outerWindowRenderer.sharedMaterial = glass;
        configuredCount++;
        return configuredCount;
    }

    private Material? ConfigureLampSurface(
        MeshRenderer? renderer,
        string materialName,
        Color emissionColor,
        Color baseColor,
        bool transparent)
    {
        if (renderer == null)
            return null;

        var original = FirstMaterial(renderer);
        var material = CreateCompatibleMaterial(
            original,
            materialName,
            baseColor,
            transparent,
            copyBaseTexture: true);
        renderer.sharedMaterial = material;
        SetEmission(material, emissionColor, 0.08f);
        return material;
    }

    private Material? CreateBlinkerOverlay(MeshRenderer? sourceRenderer, bool leftSide, string suffix)
    {
        if (sourceRenderer == null)
            return null;

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
            return null;

        try
        {
            var overlayMesh = CreateFilteredMesh(
                sourceRenderer,
                sourceFilter.sharedMesh,
                vehiclePosition => leftSide ? vehiclePosition.x <= 0f : vehiclePosition.x > 0f,
                suffix);
            if (overlayMesh == null)
                return null;

            var overlayObject = new GameObject("AudiRS6R_Blinker_" + suffix);
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.layer = sourceRenderer.gameObject.layer;
            var overlayFilter = overlayObject.AddComponent<MeshFilter>();
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayFilter.sharedMesh = overlayMesh;

            var material = CreateCompatibleMaterial(
                FirstMaterial(sourceRenderer),
                "AudiRS6R Blinker " + suffix,
                new Color(1f, 0.22f, 0.005f, 0f),
                transparent: true,
                copyBaseTexture: true);
            material.renderQueue = Math.Max(material.renderQueue, 3100);
            overlayRenderer.sharedMaterial = material;
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            SetOverlayState(material, false);

            generatedObjects.Add(overlayObject);
            generatedMeshes.Add(overlayMesh);
            return material;
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error("CreateBlinkerOverlay." + suffix, ex);
            return null;
        }
    }

    private Material? CreateHeadlightOverlay(MeshRenderer? sourceRenderer)
    {
        if (sourceRenderer == null || vehicleController == null)
            return null;

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
            return null;

        try
        {
            var overlayMesh = CreateFilteredMesh(
                sourceRenderer,
                sourceFilter.sharedMesh,
                vehiclePosition => vehiclePosition.z >= 0f,
                "Headlights");
            if (overlayMesh == null)
                return null;

            var overlayObject = new GameObject("AudiRS6R_HeadlightOverlay");
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.layer = sourceRenderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            var material = CreateCompatibleMaterial(
                FirstMaterial(sourceRenderer),
                "AudiRS6R Headlight Emission",
                new Color(1f, 1f, 1f, 0f),
                transparent: true,
                copyBaseTexture: true);
            material.renderQueue = Math.Max(material.renderQueue, 3050);
            overlayRenderer.sharedMaterial = material;
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;

            generatedObjects.Add(overlayObject);
            generatedMeshes.Add(overlayMesh);
            SetHeadlightOverlayState(material, false);
            return material;
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error("CreateHeadlightOverlay", ex);
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
        if (vertices.Length == 0)
            return null;

        if (vehicleController == null)
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
                if (includeTriangleCenter(center))
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
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

    private Material CreateCompatibleMaterial(
        Material? source,
        string materialName,
        Color baseColor,
        bool transparent,
        bool copyBaseTexture)
    {
        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit") ??
                     source?.shader ??
                     Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("No compatible lit shader is available.");

        var material = new Material(shader) { name = materialName };
        generatedMaterials.Add(material);

        Texture? baseTexture = null;
        if (copyBaseTexture && source != null)
        {
            if (source.HasProperty("_BaseColorMap"))
                baseTexture = source.GetTexture("_BaseColorMap");
            if (baseTexture == null && source.HasProperty("_BaseMap"))
                baseTexture = source.GetTexture("_BaseMap");
            if (baseTexture == null && source.HasProperty("_MainTex"))
                baseTexture = source.GetTexture("_MainTex");
        }

        SetTextureIfPresent(material, "_BaseColorMap", baseTexture);
        SetTextureIfPresent(material, "_BaseMap", baseTexture);
        SetTextureIfPresent(material, "_MainTex", baseTexture);
        SetTextureIfPresent(material, "_EmissiveColorMap", baseTexture);
        SetTextureIfPresent(material, "_EmissionMap", baseTexture);
        SetColorIfPresent(material, "_BaseColor", baseColor);
        SetColorIfPresent(material, "_Color", baseColor);

        if (transparent)
        {
            SetFloatIfPresent(material, "_SurfaceType", 1f);
            SetFloatIfPresent(material, "_BlendMode", 0f);
            SetFloatIfPresent(material, "_SrcBlend", 1f);
            SetFloatIfPresent(material, "_DstBlend", 10f);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
        }

        return material;
    }

    private void ApplyLightState(bool forceLog)
    {
        var headlights = headlightBeam != null && headlightBeam.enabled;
        var braking = GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking");
        var leftBlinker = GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = GetBoolField(blinkers, "_isRightBlinkerOn");
        var blinkerFlash = GetBoolField(blinkers, "_isBlinkerOn");

        SetEmission(frontLampMaterial, Color.white, 0.12f);
        SetHeadlightOverlayState(headlightOverlayMaterial, headlights);
        SetEmission(
            rearLampMaterial,
            new Color(1f, 0.025f, 0.01f),
            braking ? 7f : headlights ? 0.9f : 0.08f);
        SetOverlayState(leftFrontBlinkerMaterial, leftBlinker && blinkerFlash);
        SetOverlayState(leftRearBlinkerMaterial, leftBlinker && blinkerFlash);
        SetOverlayState(rightFrontBlinkerMaterial, rightBlinker && blinkerFlash);
        SetOverlayState(rightRearBlinkerMaterial, rightBlinker && blinkerFlash);

        if (forceLog ||
            headlights != lastHeadlights ||
            braking != lastBrakes ||
            leftBlinker != lastLeftBlinker ||
            rightBlinker != lastRightBlinker ||
            blinkerFlash != lastBlinkerFlash)
        {
            AudiRS6RDiagnostics.Vehicle(
                "LIGHT_STATE",
                $"vehicleId=\"{vehicleController?.vehicleInstance?.id ?? "<none>"}\" " +
                $"headlights={headlights} brakes={braking} leftBlinker={leftBlinker} " +
                $"rightBlinker={rightBlinker} blinkerFlash={blinkerFlash}");
        }

        lastHeadlights = headlights;
        lastBrakes = braking;
        lastLeftBlinker = leftBlinker;
        lastRightBlinker = rightBlinker;
        lastBlinkerFlash = blinkerFlash;
    }

    private static void SetEmission(Material? material, Color color, float intensity)
    {
        if (material == null)
            return;

        var emission = color * intensity;
        emission.a = 1f;
        SetColorIfPresent(material, "_EmissiveColor", emission);
        SetColorIfPresent(material, "_EmissionColor", emission);
        SetFloatIfPresent(material, "_EmissiveIntensity", 1f);
        SetFloatIfPresent(material, "_UseEmissiveIntensity", 0f);
        material.EnableKeyword("_EMISSION");
        material.EnableKeyword("_EMISSIVE_COLOR_MAP");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    private static void SetOverlayState(Material? material, bool active)
    {
        if (material == null)
            return;

        var amber = new Color(1f, 0.22f, 0.005f, active ? 0.42f : 0f);
        SetColorIfPresent(material, "_BaseColor", amber);
        SetColorIfPresent(material, "_Color", amber);
        SetEmission(material, new Color(1f, 0.12f, 0.002f), active ? 9f : 0f);
    }

    private static void SetHeadlightOverlayState(Material? material, bool active)
    {
        if (material == null)
            return;

        var white = new Color(1f, 1f, 1f, active ? 0.32f : 0f);
        SetColorIfPresent(material, "_BaseColor", white);
        SetColorIfPresent(material, "_Color", white);
        SetEmission(material, Color.white, active ? 7f : 0f);
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

    private static Material? FirstMaterial(Renderer renderer)
    {
        var materials = renderer.sharedMaterials;
        return materials.Length > 0 ? materials[0] : null;
    }

    private static bool GetBoolField(object? target, string fieldName)
    {
        return target != null &&
               target.GetType().GetField(fieldName, InstanceFields)?.GetValue(target) is bool value &&
               value;
    }

    private static bool GetBoolProperty(object? target, string propertyName)
    {
        return target != null &&
               target.GetType().GetProperty(propertyName, InstanceFields)?.GetValue(target) is bool value &&
               value;
    }

    private static bool GetBoolMethod(object? target, string methodName)
    {
        return target != null &&
               target.GetType().GetMethod(methodName, InstanceFields, null, Type.EmptyTypes, null)?.Invoke(target, null) is bool value &&
               value;
    }

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
            material.SetColor(propertyName, value);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static void SetTextureIfPresent(Material material, string propertyName, Texture? value)
    {
        if (value != null && material.HasProperty(propertyName))
            material.SetTexture(propertyName, value);
    }

    private int CountBlinkerOverlays()
    {
        var count = 0;
        if (leftFrontBlinkerMaterial != null) count++;
        if (rightFrontBlinkerMaterial != null) count++;
        if (leftRearBlinkerMaterial != null) count++;
        if (rightRearBlinkerMaterial != null) count++;
        return count;
    }

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
        {
            if (generatedObject != null)
                Destroy(generatedObject);
        }

        foreach (var material in generatedMaterials)
        {
            if (material != null)
                Destroy(material);
        }

        foreach (var mesh in generatedMeshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
