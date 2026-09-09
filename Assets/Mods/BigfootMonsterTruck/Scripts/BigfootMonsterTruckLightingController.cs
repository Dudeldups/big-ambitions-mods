#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BigfootMonsterTruckLightingController : MonoBehaviour
{
    private const string BodyRendererName = "Object_5";
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();
    private VehicleController? vehicle;
    private ModContext? context;
    private Light? originalBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? headlightOverlay;
    private bool initialized;
    private bool failureReported;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        foreach (var light in controller.GetComponentsInChildren<Light>(true))
            if (string.Equals(light.name, "Spotlights", StringComparison.Ordinal))
            {
                originalBeam = light;
                break;
            }

        MeshRenderer? bodyRenderer = null;
        foreach (var renderer in controller.GetComponentsInChildren<MeshRenderer>(true))
            if (string.Equals(renderer.name, BodyRendererName, StringComparison.Ordinal))
            {
                bodyRenderer = renderer;
                break;
            }

        headlightOverlay = CreatePaintedHeadlightOverlay(bodyRenderer);
        ConfigureBeams();
        initialized = true;
        if (headlightOverlay == null || leftBeam == null || rightBeam == null)
            Warn("headlight setup is incomplete; painted lamp glow or a beam is unavailable.");
        if (controller.GetType().GetProperty("ShouldLightsBeOn", InstanceMembers) == null)
            Warn("automatic headlight state is unavailable.");
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
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            Warn($"headlight update failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private MeshRenderer? CreatePaintedHeadlightOverlay(MeshRenderer? sourceRenderer)
    {
        if (sourceRenderer == null)
        {
            Warn($"body renderer '{BodyRendererName}' was not found.");
            return null;
        }
        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        var source = sourceFilter?.sharedMesh;
        if (source == null || source.subMeshCount == 0)
        {
            Warn("painted-headlight source mesh is unavailable.");
            return null;
        }

        try
        {
            var uv = source.uv;
            var vertices = source.vertices;
            if (uv.Length != vertices.Length)
                throw new InvalidOperationException("Headlight source mesh has no usable UV channel.");
            var vehiclePositions = new Vector3[vertices.Length];
            for (var index = 0; index < vertices.Length; index++)
                vehiclePositions[index] = vehicle!.transform.InverseTransformPoint(
                    sourceRenderer.transform.TransformPoint(vertices[index]));

            var selected = new List<int>();
            var triangles = source.GetTriangles(0);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                var center = (vehiclePositions[a] + vehiclePositions[b] + vehiclePositions[c]) / 3f;
                var touchesHeadlightAtlas = IsPaintedHeadlightUv(uv[a]) ||
                                            IsPaintedHeadlightUv(uv[b]) ||
                                            IsPaintedHeadlightUv(uv[c]);
                if (!touchesHeadlightAtlas || Mathf.Abs(center.x) < 0.85f ||
                    center.z < 1.9f || center.y < 1.35f || center.y > 1.95f)
                    continue;
                selected.Add(a);
                selected.Add(b);
                selected.Add(c);
            }
            if (selected.Count == 0)
                throw new InvalidOperationException("No painted headlight triangles matched the model atlas.");

            var overlayMesh = new Mesh
            {
                name = source.name + "_BigfootHeadlights",
                indexFormat = source.indexFormat,
                vertices = vertices,
                normals = source.normals,
                tangents = source.tangents,
                colors32 = source.colors32,
                uv = uv,
                uv2 = source.uv2,
            };
            overlayMesh.SetTriangles(selected, 0, true);
            overlayMesh.RecalculateBounds();
            generatedMeshes.Add(overlayMesh);

            var overlayObject = new GameObject("BigfootMonsterTruck_PaintedHeadlights");
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.transform.localScale = Vector3.one * 1.0015f;
            overlayObject.layer = sourceRenderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = CreateEmissiveMaterial(
                sourceRenderer.sharedMaterials.Length > 0 ? sourceRenderer.sharedMaterials[0] : null);
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.enabled = false;
            generatedObjects.Add(overlayObject);
            return overlayRenderer;
        }
        catch (Exception exception)
        {
            Warn($"painted headlight overlay failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static bool IsPaintedHeadlightUv(Vector2 uv) =>
        uv.y >= 0.55f && uv.y <= 0.64f &&
        ((uv.x >= 0.06f && uv.x <= 0.15f) ||
         (uv.x >= 0.40f && uv.x <= 0.51f));

    private Material CreateEmissiveMaterial(Material? source)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Texture") ?? source?.shader;
        if (shader == null)
            throw new InvalidOperationException("No compatible emissive shader is available.");
        var material = new Material(shader) { name = "Bigfoot Painted Headlight Glow" };
        generatedMaterials.Add(material);
        var texture = GetBaseTexture(source);
        SetTexture(material, "_UnlitColorMap", texture);
        SetTexture(material, "_BaseColorMap", texture);
        SetTexture(material, "_BaseMap", texture);
        SetTexture(material, "_MainTex", texture);
        SetTexture(material, "baseColorTexture", texture);
        SetTexture(material, "_EmissiveColorMap", texture);
        SetTexture(material, "_EmissionMap", texture);
        var glow = new Color(4.2f, 4.35f, 4.6f, 1f);
        SetColor(material, "_UnlitColor", glow);
        SetColor(material, "_BaseColor", glow);
        SetColor(material, "_Color", glow);
        SetColor(material, "baseColorFactor", glow);
        SetColor(material, "_EmissiveColor", glow);
        SetColor(material, "_EmissionColor", glow);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_Cull", 0f);
        SetFloat(material, "_CullMode", 0f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 2450;
        return material;
    }

    private void ConfigureBeams()
    {
        if (originalBeam == null)
        {
            Warn("standard headlight beam was not found.");
            return;
        }
        originalBeam.enabled = false;
        leftBeam = CloneBeam(-0.78f, "BigfootMonsterTruck_LeftHeadlightBeam");
        rightBeam = CloneBeam(0.78f, "BigfootMonsterTruck_RightHeadlightBeam");
    }

    private Light? CloneBeam(float localX, string objectName)
    {
        var clone = Instantiate(originalBeam!.gameObject, originalBeam.transform.parent, false);
        clone.name = objectName;
        clone.transform.localPosition = new Vector3(localX, 1.62f, 2.28f);
        clone.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        var light = clone.GetComponent<Light>();
        if (light == null)
        {
            Destroy(clone);
            return null;
        }
        light.enabled = false;
        light.cookie = null;
        light.range = 50f;
        light.spotAngle = 62f;
        light.innerSpotAngle = 38f;
        light.useColorTemperature = true;
        light.colorTemperature = 4900f;
        generatedObjects.Add(clone);
        return light;
    }

    private void ApplyState()
    {
        var active = vehicle != null && vehicle.controlledByPlayer &&
                     vehicle.GetType().GetProperty("ShouldLightsBeOn", InstanceMembers)
                         ?.GetValue(vehicle) is true;
        if (originalBeam != null)
            originalBeam.enabled = false;
        SetEnabled(leftBeam, active);
        SetEnabled(rightBeam, active);
        if (headlightOverlay != null && headlightOverlay.enabled != active)
            headlightOverlay.enabled = active;
    }

    private static Texture? GetBaseTexture(Material? material)
    {
        if (material == null)
            return null;
        foreach (var property in new[] { "_BaseColorMap", "_BaseMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property) && material.GetTexture(property) is Texture texture)
                return texture;
        return null;
    }

    private static void SetEnabled(Light? light, bool enabled)
    {
        if (light != null && light.enabled != enabled)
            light.enabled = enabled;
    }

    private static void SetTexture(Material material, string name, Texture? value)
    {
        if (value != null && material.HasProperty(name))
            material.SetTexture(name, value);
    }

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name))
            material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name))
            material.SetFloat(name, value);
    }

    private void Warn(string message) =>
        context?.Logger.Warn(
            $"BigfootMonsterTruck lighting vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null)
                Destroy(generatedObject);
        foreach (var material in generatedMaterials)
            if (material != null)
                Destroy(material);
        foreach (var mesh in generatedMeshes)
            if (mesh != null)
                Destroy(mesh);
    }
}
