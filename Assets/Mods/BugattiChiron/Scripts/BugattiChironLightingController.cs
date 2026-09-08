#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BugattiChironLightingController : MonoBehaviour
{
    private const string HeadlampName = "Headlight_Headlights-Lights_0";
    private const string RearStripName = "Tail-light_Tail-light-LIGHT_0";
    private const string ThirdBrakeLightName = "Tail-light_Brake-lights_0";
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private VehicleController? vehicle;
    private ModContext? context;
    private object? brakes;
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? thirdBrakeOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private int lastState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller)
            return;

        vehicle = controller;
        context = modContext;
        LocateStateSources();

        var renderers = controller.GetComponentsInChildren<MeshRenderer>(true);
        var headlamp = FindRenderer(renderers, HeadlampName);
        var rearStrip = FindRenderer(renderers, RearStripName);
        var thirdBrake = FindRenderer(renderers, ThirdBrakeLightName);

        headlampOverlay = CreateOverlay(
            headlamp, "BugattiChiron_HeadlampRectangles", new Color(0.78f, 0.87f, 1f, 1f));
        rearTailOverlay = CreateOverlay(
            rearStrip, "BugattiChiron_RearTailStrip", new Color(0.26f, 0.004f, 0.001f, 1f), 2.4f);
        rearBrakeOverlay = CreateOverlay(
            rearStrip, "BugattiChiron_RearBrakeStrip", new Color(1f, 0.008f, 0.001f, 1f), 4.2f, 1.003f);
        thirdBrakeOverlay = CreateOverlay(
            thirdBrake, "BugattiChiron_ThirdBrakeLight", new Color(1f, 0.008f, 0.001f, 1f), 4.2f);
        var beamCount = ConfigureHeadlightBeams();

        initialized = true;
        LogInfo($"initialized headlamp='{headlamp?.name ?? "missing"}' " +
                $"rearStrip='{rearStrip?.name ?? "missing"}' " +
                $"thirdBrake='{thirdBrake?.name ?? "missing"}' beams={beamCount}/2 overlays=" +
                $"{(headlampOverlay != null ? 1 : 0) + (rearTailOverlay != null ? 1 : 0) + (rearBrakeOverlay != null ? 1 : 0) + (thirdBrakeOverlay != null ? 1 : 0)}/4.");
        if (headlampOverlay == null || rearTailOverlay == null || rearBrakeOverlay == null ||
            thirdBrakeOverlay == null || beamCount != 2)
        {
            LogWarning("lighting setup is incomplete; inspect the named renderer and beam diagnostics.");
        }
        ApplyState();
    }

    private void Update()
    {
        if (!initialized)
            return;
        try
        {
            ApplyState();
        }
        catch (Exception ex)
        {
            if (updateFailureReported)
                return;
            updateFailureReported = true;
            LogWarning($"update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LocateStateSources()
    {
        if (vehicle == null)
            return;
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            if (brakes == null)
                brakes = component.GetType().GetField("brakes", InstanceMembers)?.GetValue(component);
        }
        foreach (var light in vehicle.GetComponentsInChildren<Light>(true))
        {
            if (light != null && string.Equals(light.name, "Spotlights", StringComparison.Ordinal))
            {
                templateBeam = light;
                break;
            }
        }
    }

    private int ConfigureHeadlightBeams()
    {
        if (templateBeam == null)
        {
            LogWarning("the inherited 'Spotlights' road-light template is missing.");
            return 0;
        }
        templateBeam.enabled = false;
        leftBeam = CloneBeam(-0.68f, "BugattiChiron_LeftHeadlightBeam");
        rightBeam = CloneBeam(0.68f, "BugattiChiron_RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string objectName)
    {
        if (templateBeam == null || vehicle == null)
            return null;
        try
        {
            var clone = Instantiate(templateBeam.gameObject, vehicle.transform, false);
            clone.name = objectName;
            clone.transform.localPosition = new Vector3(localX, 0.58f, 2.18f);
            clone.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
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
            LogWarning($"could not create road beam '{objectName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private MeshRenderer? CreateOverlay(
        MeshRenderer? source,
        string objectName,
        Color color,
        float intensity = 3.5f,
        float scale = 1.0015f)
    {
        if (source == null)
        {
            LogWarning($"overlay '{objectName}' has no source renderer.");
            return null;
        }
        var filter = source.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
        {
            LogWarning($"overlay '{objectName}' source '{source.name}' has no mesh.");
            return null;
        }

        var overlayObject = new GameObject(objectName);
        overlayObject.transform.SetParent(source.transform, false);
        overlayObject.transform.localScale = Vector3.one * scale;
        overlayObject.layer = source.gameObject.layer;
        overlayObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var overlay = overlayObject.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = CreateUnlitMaterial(objectName + " Material", color, intensity);
        overlay.renderingLayerMask = source.renderingLayerMask;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.enabled = false;
        generatedObjects.Add(overlayObject);
        return overlay;
    }

    private Material CreateUnlitMaterial(string materialName, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color");
        if (shader == null)
            throw new InvalidOperationException("No compatible unlit shader is available.");

        var material = new Material(shader) { name = materialName };
        var hdrColor = color * intensity;
        hdrColor.a = 1f;
        SetColor(material, "_UnlitColor", hdrColor);
        SetColor(material, "_BaseColor", hdrColor);
        SetColor(material, "_Color", hdrColor);
        SetColor(material, "baseColorFactor", hdrColor);
        SetColor(material, "_EmissiveColor", hdrColor);
        SetColor(material, "_EmissionColor", hdrColor);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_Cull", 0f);
        SetFloat(material, "_CullMode", 0f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 2450;
        generatedMaterials.Add(material);
        return material;
    }

    private void ApplyState()
    {
        var controlled = vehicle != null && vehicle.controlledByPlayer;
        var lightsOn = controlled && GetBoolProperty(vehicle, "ShouldLightsBeOn");
        var braking = controlled &&
                      (GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking"));

        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailOverlay, lightsOn && !braking);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(thirdBrakeOverlay, braking);
        SetEnabled(leftBeam, lightsOn);
        SetEnabled(rightBeam, lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0);
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state playerControlled={controlled} nightLights={lightsOn} braking={braking} " +
                $"headlampRectangles={lightsOn} roadBeams={lightsOn} rearStripBrake={braking} " +
                $"thirdBrake={braking}.");
    }

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null && string.Equals(renderer.name, name, StringComparison.Ordinal))
                return renderer;
        return null;
    }

    private static bool GetBoolProperty(object? target, string name) =>
        target != null && target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target) is bool value && value;

    private static bool GetBoolMethod(object? target, string name) =>
        target != null && target.GetType().GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null)
            ?.Invoke(target, null) is bool value && value;

    private static void SetEnabled(Renderer? renderer, bool enabled)
    {
        if (renderer != null && renderer.enabled != enabled)
            renderer.enabled = enabled;
    }

    private static void SetEnabled(Light? light, bool enabled)
    {
        if (light != null && light.enabled != enabled)
            light.enabled = enabled;
    }

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name)) material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name)) material.SetFloat(name, value);
    }

    private void LogInfo(string message) =>
        context?.Logger.Info($"BugattiChiron lighting vehicle='{vehicle?.name}' " +
                             $"instance={vehicle?.GetInstanceID()}: {message}");

    private void LogWarning(string message) =>
        context?.Logger.Warn($"BugattiChiron lighting vehicle='{vehicle?.name}' " +
                             $"instance={vehicle?.GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Destroy(generatedMaterial);
    }
}
