#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class BMWM4G82CaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "BMWFixedCaliperFrontLeft", "BMWWheelFrontLeft" },
        { "BMWFixedCaliperFrontRight", "BMWWheelFrontRight" },
        { "BMWFixedCaliperRearLeft", "BMWWheelRearLeft" },
        { "BMWFixedCaliperRearRight", "BMWWheelRearRight" },
    };

    private readonly List<CaliperBinding> bindings = new List<CaliperBinding>(4);
    private readonly List<Mesh> ownedMeshes = new List<Mesh>(4);
    private readonly List<Material> ownedMaterials = new List<Material>(4);
    private VehicleController? vehicle;
    private ModContext? context;
    private bool warnedAboutInvalidSteering;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        bindings.Clear();
        warnedAboutInvalidSteering = false;

        for (var index = 0; index < BindingNames.GetLength(0); index++)
        {
            var pivotName = BindingNames[index, 0];
            var wheelName = BindingNames[index, 1];
            var pivot = FindTransform(controller.transform, pivotName) ??
                        throw new InvalidOperationException($"Caliper pivot '{pivotName}' is missing.");
            var wheel = FindTransform(controller.transform, wheelName) ??
                        throw new InvalidOperationException($"Wheel visual '{wheelName}' is missing.");
            ReplaceIncorrectSourceGeometry(
                pivot,
                index < 2,
                index == 0 || index == 2);
            CenterPivotWithoutMovingGeometry(pivot, wheel, controller.transform.rotation);
            bindings.Add(new CaliperBinding(pivot, wheel));
        }

        ApplyBindings();
        context?.Logger.Info(
            $"BMWM4G82 steering calipers ready vehicle={controller.GetInstanceID()}, " +
            $"bindings={bindings.Count}, followsSteeringAndSuspension=true, inheritsWheelSpin=false.");
    }

    private void LateUpdate()
    {
        if (vehicle != null && bindings.Count == 4)
            ApplyBindings();
    }

    private void ReplaceIncorrectSourceGeometry(Transform pivot, bool front, bool left)
    {
        var sourceRenderer = pivot.GetComponentInChildren<MeshRenderer>(true);
        var sourceMaterial = sourceRenderer?.sharedMaterial;
        var sourceLayerMask = sourceRenderer?.renderingLayerMask ?? uint.MaxValue;
        for (var index = pivot.childCount - 1; index >= 0; index--)
        {
            var child = pivot.GetChild(index);
            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            child.gameObject.SetActive(false);
            child.SetParent(null, true);
            Destroy(child.gameObject);
        }

        pivot.localScale = Vector3.one;
        var holder = new GameObject("BMWCaliperGeometry_" + (front ? "Front" : "Rear") +
                                    (left ? "Left" : "Right"));
        holder.transform.SetParent(pivot, false);
        var part = new GameObject("BMW_Caliper_Runtime");
        part.transform.SetParent(holder.transform, false);
        var mesh = BuildCaliperMesh(front, left);
        ownedMeshes.Add(mesh);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rendererComponent = part.AddComponent<MeshRenderer>();
        rendererComponent.sharedMaterial = CreateCaliperMaterial(sourceMaterial);
        rendererComponent.renderingLayerMask = sourceLayerMask;
        rendererComponent.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        rendererComponent.receiveShadows = true;
    }

    private Mesh BuildCaliperMesh(bool front, bool left)
    {
        var height = front ? 0.31f : 0.27f;
        var width = front ? 0.14f : 0.12f;
        var thickness = front ? 0.095f : 0.085f;
        var centerX = left ? 0.045f : -0.045f;
        var centerY = 0.015f;
        var centerZ = front ? -0.165f : 0.155f;
        var halfHeight = height * 0.5f;
        var halfWidth = width * 0.5f;
        var halfThickness = thickness * 0.5f;
        var profile = new[]
        {
            new Vector2(-halfHeight + 0.035f, -halfWidth),
            new Vector2(-halfHeight, -halfWidth * 0.30f),
            new Vector2(-halfHeight + 0.015f, halfWidth * 0.72f),
            new Vector2(-halfHeight + 0.055f, halfWidth),
            new Vector2(halfHeight - 0.045f, halfWidth),
            new Vector2(halfHeight, halfWidth * 0.32f),
            new Vector2(halfHeight - 0.012f, -halfWidth * 0.72f),
            new Vector2(halfHeight - 0.050f, -halfWidth),
        };

        var vertices = new Vector3[profile.Length * 2];
        for (var index = 0; index < profile.Length; index++)
        {
            var point = profile[index];
            vertices[index] = new Vector3(
                centerX - halfThickness,
                centerY + point.x,
                centerZ + point.y);
            vertices[index + profile.Length] = new Vector3(
                centerX + halfThickness,
                centerY + point.x,
                centerZ + point.y);
        }

        var triangles = new List<int>((profile.Length - 2) * 6 + profile.Length * 6);
        for (var index = 1; index < profile.Length - 1; index++)
        {
            triangles.Add(0);
            triangles.Add(index + 1);
            triangles.Add(index);
            triangles.Add(profile.Length);
            triangles.Add(profile.Length + index);
            triangles.Add(profile.Length + index + 1);
        }
        for (var index = 0; index < profile.Length; index++)
        {
            var next = (index + 1) % profile.Length;
            triangles.Add(index);
            triangles.Add(next);
            triangles.Add(profile.Length + next);
            triangles.Add(index);
            triangles.Add(profile.Length + next);
            triangles.Add(profile.Length + index);
        }

        var mesh = new Mesh { name = "BMW_M_Sport_Caliper_Runtime" };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private Material CreateCaliperMaterial(Material? source)
    {
        Material material;
        if (source != null)
        {
            material = new Material(source);
        }
        else
        {
            var shader = Shader.Find("HDRP/Lit") ??
                         Shader.Find("High Definition Render Pipeline/Lit") ??
                         Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No opaque shader is available for BMW calipers.");
            material = new Material(shader);
        }

        material.name = "BMWM4G82 Caliper Runtime";
        foreach (var textureProperty in new[]
                 {
                     "_BaseColorMap", "_MainTex", "baseColorTexture", "_NormalMap",
                     "_MaskMap", "_DetailMap", "_EmissiveColorMap"
                 })
            if (material.HasProperty(textureProperty))
                material.SetTexture(textureProperty, null);
        foreach (var colorProperty in new[] { "_BaseColor", "_Color", "baseColorFactor" })
            if (material.HasProperty(colorProperty))
                material.SetColor(colorProperty, BMWM4G82Materials.CaliperBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0.35f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.46f);
        if (material.HasProperty("_SurfaceType"))
            material.SetFloat("_SurfaceType", 0f);
        material.renderQueue = -1;
        ownedMaterials.Add(material);
        return material;
    }

    private void OnDestroy()
    {
        foreach (var mesh in ownedMeshes)
            if (mesh != null)
                Destroy(mesh);
        foreach (var material in ownedMaterials)
            if (material != null)
                Destroy(material);
        ownedMeshes.Clear();
        ownedMaterials.Clear();
    }

    private void ApplyBindings()
    {
        var vehicleTransform = vehicle!.transform;
        var vehicleUp = vehicleTransform.up;
        foreach (var binding in bindings)
        {
            // Wheel roll happens around the visual's right axis, so that axis
            // retains steering while discarding spin. Rebuild the caliper pose
            // from it after NWH has updated the visual for this frame.
            var axle = Vector3.ProjectOnPlane(binding.Wheel.right, vehicleUp).normalized;
            if (axle.sqrMagnitude < 0.5f)
                continue;
            var steeringAngle = Vector3.SignedAngle(vehicleTransform.right, axle, vehicleUp);
            if (Mathf.Abs(steeringAngle) > 60f)
            {
                if (!warnedAboutInvalidSteering)
                {
                    warnedAboutInvalidSteering = true;
                    context?.Logger.Warn(
                        $"BMWM4G82 steering caliper rejected angle={steeringAngle:0.0}deg " +
                        $"wheel='{binding.Wheel.name}'.");
                }
                continue;
            }

            binding.Pivot.SetPositionAndRotation(
                binding.Wheel.position,
                vehicleTransform.rotation * Quaternion.Euler(0f, steeringAngle, 0f));
        }
    }

    private static void CenterPivotWithoutMovingGeometry(
        Transform pivot,
        Transform wheel,
        Quaternion chassisRotation)
    {
        var childPositions = new Vector3[pivot.childCount];
        var childRotations = new Quaternion[pivot.childCount];
        for (var index = 0; index < pivot.childCount; index++)
        {
            childPositions[index] = pivot.GetChild(index).position;
            childRotations[index] = pivot.GetChild(index).rotation;
        }

        pivot.SetPositionAndRotation(wheel.position, chassisRotation);
        for (var index = 0; index < pivot.childCount; index++)
            pivot.GetChild(index).SetPositionAndRotation(childPositions[index], childRotations[index]);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        }
        return null;
    }

    private sealed class CaliperBinding
    {
        public CaliperBinding(Transform pivot, Transform wheel)
        {
            Pivot = pivot;
            Wheel = wheel;
        }

        public Transform Pivot { get; }
        public Transform Wheel { get; }
    }
}
