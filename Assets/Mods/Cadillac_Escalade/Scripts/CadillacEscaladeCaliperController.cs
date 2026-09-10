#nullable enable
using System;
using BAModAPI;
using NWH.WheelController3D;
using UnityEngine;

internal sealed class CadillacEscaladeCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "CadillacFixedCaliperFrontLeft", "FrontLeft_WheelController" },
        { "CadillacFixedCaliperFrontRight", "FrontRight_WheelController" },
        { "CadillacFixedCaliperRearLeft", "RearLeft_WheelController" },
        { "CadillacFixedCaliperRearRight", "RearRight_WheelController" },
    };

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        var bound = 0;
        for (var index = 0; index < BindingNames.GetLength(0); index++)
        {
            var pivotName = BindingNames[index, 0];
            var controllerName = BindingNames[index, 1];
            var pivot = FindTransform(controller.transform, pivotName) ??
                        throw new InvalidOperationException($"Caliper pivot '{pivotName}' is missing.");
            var wheelControllerTransform = FindTransform(controller.transform, controllerName) ??
                                           throw new InvalidOperationException(
                                               $"Wheel controller '{controllerName}' is missing.");
            var wheelController = wheelControllerTransform.GetComponent<WheelController>() ??
                                  throw new InvalidOperationException(
                                      $"Wheel controller component on '{controllerName}' is missing.");

            // NWH owns the final suspension and steering pose. Its dedicated
            // non-rotating visual follows both while excluding axle spin, which
            // is exactly the brake-caliper contract. Rotors remain children of
            // the rolling visual and therefore steer and spin with the wheel.
            wheelController.wheel.nonRotatingVisualPositionOffset = Vector3.zero;
            wheelController.wheel.nonRotatingVisualRotationOffset = Quaternion.identity;
            wheelController.NonRotatingVisual = pivot.gameObject;
            bound++;
        }

        CadillacEscaladeDiagnostics.Info(modContext,
            $"CadillacEscalade native brake visuals ready vehicle={controller.GetInstanceID()}, " +
            $"calipers={bound}, rotors=rolling-wheel-children.");
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

}
