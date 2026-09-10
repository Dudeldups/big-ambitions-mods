#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BMWM4G82LightingController : MonoBehaviour
{
    private const string DaylightName = "BMW_DRL_Source";
    private const string LampName = "BMW_Lamp_Source";
    private const string RearStripName = "BMW_RearLamp_Source";
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private object? transmission;
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? leftDaylightOverlay;
    private MeshRenderer? rightDaylightOverlay;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearLeftTailOverlay;
    private MeshRenderer? rearRightTailOverlay;
    private MeshRenderer? rearLeftBrakeOverlay;
    private MeshRenderer? rearRightBrakeOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller)
            return;
        vehicle = controller;
        context = modContext;
        LocateStateSources();

        var renderers = controller.GetComponentsInChildren<MeshRenderer>(true);
        var daylight = FindRenderer(renderers, DaylightName);
        var lamp = FindRenderer(renderers, LampName);
        var rearStrip = FindRenderer(renderers, RearStripName);

        var white = new Color(0.90f, 0.95f, 1f, 1f);
        leftDaylightOverlay = CreateComponentOverlay(daylight,
            component => IsFront(component) && component.Bounds.center.x < 0f,
            "LeftDaytimeRunningLights", white, 4.8f, 1.001f);
        rightDaylightOverlay = CreateComponentOverlay(daylight,
            component => IsFront(component) && component.Bounds.center.x >= 0f,
            "RightDaytimeRunningLights", white, 4.8f, 1.001f);
        headlampOverlay = CreateComponentOverlay(lamp,
            IsFrontProjector,
            "HeadlampProjectors", white, 6.2f, 1.004f);
        rearLeftTailOverlay = CreateComponentOverlay(lamp,
            component => (IsRearOuterSignature(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x < 0f,
            "RearLeftTailSignature", new Color(0.78f, 0.006f, 0.002f, 1f), 2.5f, 1.003f);
        rearRightTailOverlay = CreateComponentOverlay(lamp,
            component => (IsRearOuterSignature(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x >= 0f,
            "RearRightTailSignature", new Color(0.78f, 0.006f, 0.002f, 1f), 2.5f, 1.003f);
        rearLeftBrakeOverlay = CreateComponentOverlay(lamp,
            component => (IsRearBrakePanel(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x < 0f,
            "RearLeftBrakeSignature", new Color(1f, 0.008f, 0.001f, 1f), 4.2f, 1.0035f);
        rearRightBrakeOverlay = CreateComponentOverlay(lamp,
            component => (IsRearBrakePanel(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x >= 0f,
            "RearRightBrakeSignature", new Color(1f, 0.008f, 0.001f, 1f), 4.2f, 1.0035f);
        reverseOverlay = CreateComponentOverlay(lamp,
            IsRearReverseStrip,
            "ReverseLight", white, 4.6f, 1.004f);
        var amber = new Color(1f, 0.42f, 0.005f, 1f);
        leftBlinkerOverlay = CreateComponentOverlay(daylight,
            component => IsFront(component) && component.Bounds.center.x < 0f,
            "LeftIndicator", amber, 5.4f, 1.002f);
        rightBlinkerOverlay = CreateComponentOverlay(daylight,
            component => IsFront(component) && component.Bounds.center.x >= 0f,
            "RightIndicator", amber, 5.4f, 1.002f);
        rearLeftBlinkerOverlay = CreateComponentOverlay(lamp,
            component => (IsRearOuterSignature(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x < 0f,
            "RearLeftIndicator", amber, 6.0f, 1.006f);
        rearRightBlinkerOverlay = CreateComponentOverlay(lamp,
            component => (IsRearOuterSignature(component) || IsRearInnerSignature(component)) &&
                         component.Bounds.center.x >= 0f,
            "RearRightIndicator", amber, 6.0f, 1.006f);
        var beamCount = ConfigureHeadlightBeams();

        initialized = true;
        LogInfo($"initialized front='{daylight?.name}/{lamp?.name}' " +
                $"rearLens='{rearStrip?.name}' beams={beamCount}/2 " +
                $"lampOverlays={CountLampOverlays()}/8 blinkerOverlays={CountBlinkerOverlays()}/4.");
        if (CountLampOverlays() != 8 || beamCount != 2 || CountBlinkerOverlays() != 4)
            LogWarning("lighting setup is incomplete; inspect renderer-name diagnostics.");
        if (blinkers == null)
            LogWarning("VehicleBlinker state source is missing; indicator input cannot be read.");
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
            if (updateFailureReported)
                return;
            updateFailureReported = true;
            LogWarning($"update failed: {exception.GetType().Name}: {exception.Message}");
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
                brakes = GetMember(component, "brakes");
            if (component.GetType().FullName == "Vehicles.Components.VehicleBlinker")
                blinkers = component;
            if (component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController")
                transmission = GetMember(GetMember(component, "powertrain"), "transmission");
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
            LogWarning("the inherited Spotlights road-light template is missing.");
            return 0;
        }
        templateBeam.enabled = false;
        leftBeam = CloneBeam(-0.62f, "LeftHeadlightBeam");
        rightBeam = CloneBeam(0.62f, "RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string suffix)
    {
        if (templateBeam == null || vehicle == null)
            return null;
        var clone = Instantiate(templateBeam.gameObject, vehicle.transform, false);
        clone.name = "BMWM4G82_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.62f, 2.12f);
        clone.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
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
        light.colorTemperature = 5600f;
        light.useColorTemperature = true;
        generatedObjects.Add(clone);
        return light;
    }

    private MeshRenderer? CreateOverlay(MeshRenderer? source, string suffix, Color color,
        float intensity, float scale = 1.002f)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"overlay '{suffix}' source is missing.");
            return null;
        }
        return CreateOverlayObject(source, source.GetComponent<MeshFilter>().sharedMesh,
            suffix, color, intensity, scale);
    }

    private MeshRenderer? CreateComponentOverlay(MeshRenderer? source,
        Func<LampComponent, bool> includeComponent, string suffix, Color color,
        float intensity, float scale)
    {
        if (source == null || vehicle == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"component overlay '{suffix}' source is missing.");
            return null;
        }
        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sourceMesh.vertices;
        var rootVertices = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
            rootVertices[index] = vehicle.transform.InverseTransformPoint(
                source.transform.TransformPoint(vertices[index]));

        var allTriangles = new List<MeshTriangle>();
        var trianglesByVertex = new Dictionary<int, List<int>>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var triangleIndex = allTriangles.Count;
                allTriangles.Add(new MeshTriangle(a, b, c));
                AddTriangleForVertex(trianglesByVertex, a, triangleIndex);
                AddTriangleForVertex(trianglesByVertex, b, triangleIndex);
                AddTriangleForVertex(trianglesByVertex, c, triangleIndex);
            }
        }

        var selectedTriangles = new List<int>();
        var visited = new bool[allTriangles.Count];
        var queue = new Queue<int>();
        var componentTriangles = new List<int>();
        var componentCount = 0;
        var selectedComponentCount = 0;
        for (var seed = 0; seed < allTriangles.Count; seed++)
        {
            if (visited[seed])
                continue;

            componentCount++;
            componentTriangles.Clear();
            queue.Enqueue(seed);
            visited[seed] = true;
            var hasBounds = false;
            var bounds = default(Bounds);
            while (queue.Count > 0)
            {
                var triangleIndex = queue.Dequeue();
                componentTriangles.Add(triangleIndex);
                var triangle = allTriangles[triangleIndex];
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.A]);
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.B]);
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.C]);
                EnqueueNeighbors(trianglesByVertex, triangle.A, visited, queue);
                EnqueueNeighbors(trianglesByVertex, triangle.B, visited, queue);
                EnqueueNeighbors(trianglesByVertex, triangle.C, visited, queue);
            }

            var component = new LampComponent(bounds, componentTriangles.Count);
            if (!includeComponent(component))
                continue;

            selectedComponentCount++;
            foreach (var triangleIndex in componentTriangles)
            {
                var triangle = allTriangles[triangleIndex];
                selectedTriangles.Add(triangle.A);
                selectedTriangles.Add(triangle.B);
                selectedTriangles.Add(triangle.C);
            }
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"component overlay '{suffix}' selected no geometry from " +
                       $"{componentCount} components.");
            return null;
        }
        var mesh = new Mesh
        {
            name = sourceMesh.name + "_" + suffix,
            indexFormat = sourceMesh.indexFormat,
            vertices = vertices,
            normals = sourceMesh.normals,
            tangents = sourceMesh.tangents,
            colors32 = sourceMesh.colors32,
            uv = sourceMesh.uv,
            uv2 = sourceMesh.uv2
        };
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        LogInfo($"overlay '{suffix}' selected components={selectedComponentCount}/" +
                $"{componentCount} triangles={selectedTriangles.Count / 3}.");
        return CreateOverlayObject(source, mesh, suffix, color, intensity, scale);
    }

    private static bool IsFront(LampComponent component) => component.Bounds.center.z > 1.60f;

    private static bool IsRear(LampComponent component) => component.Bounds.center.z < -1.60f;

    private static bool IsFrontProjector(LampComponent component) =>
        IsFront(component) && component.TriangleCount >= 200 &&
        component.TriangleCount <= 260 && component.Bounds.size.y >= 0.055f &&
        component.Bounds.size.z >= 0.055f;

    private static bool IsRearOuterSignature(LampComponent component) =>
        IsRear(component) && component.TriangleCount >= 112 &&
        component.TriangleCount <= 120 && Mathf.Abs(component.Bounds.center.x) >= 0.50f;

    private static bool IsRearInnerSignature(LampComponent component) =>
        IsRear(component) && component.TriangleCount >= 145 &&
        component.TriangleCount <= 155 && Mathf.Abs(component.Bounds.center.x) >= 0.25f &&
        Mathf.Abs(component.Bounds.center.x) <= 0.58f;

    private static bool IsRearBrakePanel(LampComponent component) =>
        IsRear(component) && component.TriangleCount >= 190 &&
        component.TriangleCount <= 235 && Mathf.Abs(component.Bounds.center.x) >= 0.50f;

    private static bool IsRearReverseStrip(LampComponent component) =>
        IsRear(component) && component.TriangleCount >= 76 &&
        component.TriangleCount <= 82 && Mathf.Abs(component.Bounds.center.x) >= 0.25f &&
        Mathf.Abs(component.Bounds.center.x) <= 0.58f && component.Bounds.size.x >= 0.18f &&
        component.Bounds.size.y <= 0.035f;

    private static void AddTriangleForVertex(
        IDictionary<int, List<int>> trianglesByVertex,
        int vertex,
        int triangle)
    {
        if (!trianglesByVertex.TryGetValue(vertex, out var triangles))
        {
            triangles = new List<int>();
            trianglesByVertex.Add(vertex, triangles);
        }
        triangles.Add(triangle);
    }

    private static void EnqueueNeighbors(
        IReadOnlyDictionary<int, List<int>> trianglesByVertex,
        int vertex,
        bool[] visited,
        Queue<int> queue)
    {
        if (!trianglesByVertex.TryGetValue(vertex, out var neighbors))
            return;
        foreach (var neighbor in neighbors)
        {
            if (visited[neighbor])
                continue;
            visited[neighbor] = true;
            queue.Enqueue(neighbor);
        }
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Vector3 point)
    {
        if (!hasBounds)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
            return;
        }
        bounds.Encapsulate(point);
    }

    private MeshRenderer CreateOverlayObject(MeshRenderer source, Mesh mesh, string suffix,
        Color color, float intensity, float scale)
    {
        var host = new GameObject("BMWM4G82_" + suffix);
        host.transform.SetParent(source.transform, false);
        host.transform.localScale = Vector3.one * scale;
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateUnlitMaterial(host.name + " Material", color, intensity);
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        generatedObjects.Add(host);
        return renderer;
    }

    private Material CreateUnlitMaterial(string name, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color") ??
                     throw new InvalidOperationException("No compatible unlit shader is available.");
        var material = new Material(shader) { name = name };
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
        var leftBlinker = controlled && GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = controlled && GetBoolField(blinkers, "_isRightBlinkerOn");
        var reversing = controlled && GetIntMember(transmission, "Gear") < 0;
        var blinking = leftBlinker || rightBlinker;
        if (blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = blinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
            BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        // The amber indicator geometry is not the low-beam emitter. Keeping a
        // white copy enabled with the headlights made the indicator strips
        // look like the headlamps while the actual round projectors stayed dark.
        SetEnabled(leftDaylightOverlay, false);
        SetEnabled(rightDaylightOverlay, false);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearLeftTailOverlay, lightsOn && !leftBlinker);
        SetEnabled(rearRightTailOverlay, lightsOn && !rightBlinker);
        SetEnabled(rearLeftBrakeOverlay, braking && !leftBlinker);
        SetEnabled(rearRightBrakeOverlay, braking && !rightBlinker);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, lightsOn);
        SetEnabled(rightBeam, lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

    }

    private int CountLampOverlays() =>
        (leftDaylightOverlay != null ? 1 : 0) + (rightDaylightOverlay != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) +
        (rearLeftTailOverlay != null ? 1 : 0) + (rearRightTailOverlay != null ? 1 : 0) +
        (rearLeftBrakeOverlay != null ? 1 : 0) + (rearRightBrakeOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null && string.Equals(renderer.name, name, StringComparison.Ordinal))
                return renderer;
        return null;
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;
        return target.GetType().GetField(name, InstanceMembers)?.GetValue(target) ??
               target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target);
    }

    private static bool GetBoolProperty(object? target, string name) =>
        GetMember(target, name) is bool value && value;

    private static bool GetBoolMethod(object? target, string name) =>
        target != null && target.GetType().GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null)
            ?.Invoke(target, null) is bool value && value;

    private static bool GetBoolField(object? target, string name) =>
        target != null && target.GetType().GetField(name, InstanceMembers)?.GetValue(target) is bool value && value;

    private static int GetIntMember(object? target, string name)
    {
        var value = GetMember(target, name);
        return value == null ? 0 : Convert.ToInt32(value);
    }

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
        context?.Logger.Info($"BMWM4G82 lighting vehicle='{vehicle?.name}' " +
                             $"instance={vehicle?.GetInstanceID()}: {message}");

    private void LogWarning(string message) =>
        context?.Logger.Warn($"BMWM4G82 lighting vehicle='{vehicle?.name}' " +
                             $"instance={vehicle?.GetInstanceID()}: {message}");

    private readonly struct MeshTriangle
    {
        internal MeshTriangle(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        internal readonly int A;
        internal readonly int B;
        internal readonly int C;
    }

    private readonly struct LampComponent
    {
        internal LampComponent(Bounds bounds, int triangleCount)
        {
            Bounds = bounds;
            TriangleCount = triangleCount;
        }

        internal readonly Bounds Bounds;
        internal readonly int TriangleCount;
    }

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Destroy(generatedMaterial);
        foreach (var generatedMesh in generatedMeshes)
            if (generatedMesh != null) Destroy(generatedMesh);
    }
}
