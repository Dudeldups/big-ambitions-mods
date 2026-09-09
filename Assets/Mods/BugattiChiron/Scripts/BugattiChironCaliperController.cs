#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class BugattiChironCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "BugattiFixedCaliperFrontLeft", "BugattiWheelFrontLeft" },
        { "BugattiFixedCaliperFrontRight", "BugattiWheelFrontRight" },
        { "BugattiFixedCaliperRearLeft", "BugattiWheelRearLeft" },
        { "BugattiFixedCaliperRearRight", "BugattiWheelRearRight" },
    };

    private readonly List<CaliperBinding> bindings = new(4);
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
            var wheel = FindTransform(controller.transform, wheelName) ??
                        throw new InvalidOperationException(
                            $"Wheel visual '{wheelName}' is missing.");
            var pivot = FindTransform(controller.transform, pivotName);
            if (pivot == null)
            {
                var caliper = FindTransformWithNameFragment(wheel, "_Caliper_") ??
                              throw new InvalidOperationException(
                                  $"Wheel visual '{wheelName}' has no brake caliper renderer.");
                var pivotObject = new GameObject(pivotName);
                pivot = pivotObject.transform;
                pivot.SetParent(controller.transform, false);
                pivot.SetPositionAndRotation(wheel.position, controller.transform.rotation);
                caliper.SetParent(pivot, true);
            }

            CenterPivotWithoutMovingGeometry(pivot, wheel, controller.transform.rotation);
            bindings.Add(new CaliperBinding(pivot, wheel));
        }

        ApplyBindings();
        context?.Logger.Info(
            $"BugattiChiron steering calipers ready vehicle={controller.GetInstanceID()}, " +
            $"bindings={bindings.Count}, followsSteeringAndSuspension=true, " +
            "inheritsWheelSpin=false.");
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
            // Wheel roll occurs around the visual's right axis. That axis still
            // contains steering, allowing the caliper to follow yaw and
            // suspension while discarding the wheel's spin rotation.
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
                        $"BugattiChiron steering caliper rejected angle={steeringAngle:0.0}deg " +
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
        {
            pivot.GetChild(index).SetPositionAndRotation(
                childPositions[index],
                childRotations[index]);
        }
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static Transform? FindTransformWithNameFragment(Transform root, string fragment)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
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
