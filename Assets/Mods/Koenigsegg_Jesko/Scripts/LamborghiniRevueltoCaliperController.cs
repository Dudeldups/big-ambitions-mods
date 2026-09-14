#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class KoenigseggJeskoCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "KoenigseggFixedCaliperFrontLeft", "KoenigseggWheelFrontLeft" },
        { "KoenigseggFixedCaliperFrontRight", "KoenigseggWheelFrontRight" },
        { "KoenigseggFixedCaliperRearLeft", "KoenigseggWheelRearLeft" },
        { "KoenigseggFixedCaliperRearRight", "KoenigseggWheelRearRight" },
    };

    private readonly List<CaliperBinding> bindings = new List<CaliperBinding>(4);
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
            var positionOffset = CenterPivotWithoutMovingGeometry(
                pivot,
                wheel,
                controller.transform);
            // Front calipers sit slightly inboard of the disc centre; the rear
            // calipers are forward-mounted. These are fixed measured offsets,
            // applied in the controller's upright space rather than inferred
            // from a rolling mesh each frame.
            positionOffset += index switch
            {
                0 or 1 => new Vector3(0f, 0f, -0.08f),
                2 or 3 => new Vector3(0f, 0f, 0.12f),
                _ => Vector3.zero,
            };
            bindings.Add(new CaliperBinding(pivot, wheel, positionOffset));
        }

        ApplyBindings();
        context?.Logger.Info(
            $"KoenigseggJesko steering calipers ready vehicle={controller.GetInstanceID()}, " +
            $"bindings={bindings.Count}, followsSteeringAndSuspension=true, inheritsWheelSpin=false.");
    }

    private void LateUpdate()
    {
        if (vehicle != null && bindings.Count == 4)
            ApplyBindings();
    }

    private void ApplyBindings()
    {
        var vehicleTransform = vehicle!.transform;
        var vehicleUp = vehicleTransform.up;
        foreach (var binding in bindings)
        {
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
                        $"KoenigseggJesko steering caliper rejected angle={steeringAngle:0.0}deg " +
                        $"wheel='{binding.Wheel.name}'.");
                }
                continue;
            }

            var uprightRotation =
                vehicleTransform.rotation * Quaternion.Euler(0f, steeringAngle, 0f);
            binding.Pivot.SetPositionAndRotation(
                binding.Wheel.position + uprightRotation * binding.PositionOffset,
                uprightRotation);
        }
    }

    private static Vector3 CenterPivotWithoutMovingGeometry(
        Transform pivot,
        Transform wheel,
        Transform chassis)
    {
        var childPositions = new Vector3[pivot.childCount];
        var childRotations = new Quaternion[pivot.childCount];
        for (var index = 0; index < pivot.childCount; index++)
        {
            childPositions[index] = pivot.GetChild(index).position;
            childRotations[index] = pivot.GetChild(index).rotation;
        }

        pivot.SetPositionAndRotation(wheel.position, chassis.rotation);
        for (var index = 0; index < pivot.childCount; index++)
            pivot.GetChild(index).SetPositionAndRotation(childPositions[index], childRotations[index]);

        var boundsFound = false;
        var bounds = default(Bounds);
        foreach (var renderer in pivot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            if (!boundsFound)
            {
                bounds = renderer.bounds;
                boundsFound = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (boundsFound)
        {
            var delta = chassis.up * Vector3.Dot(wheel.position - bounds.center, chassis.up) +
                        chassis.forward * Vector3.Dot(wheel.position - bounds.center, chassis.forward);
            pivot.position += delta;
        }

        return chassis.InverseTransformVector(pivot.position - wheel.position);
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
        public CaliperBinding(Transform pivot, Transform wheel, Vector3 positionOffset)
        {
            Pivot = pivot;
            Wheel = wheel;
            PositionOffset = positionOffset;
        }

        public Transform Pivot { get; }
        public Transform Wheel { get; }
        public Vector3 PositionOffset { get; }
    }
}


