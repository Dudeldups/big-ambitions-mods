#nullable enable
using System.Linq;
using UnityEngine;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerDefaultValues
    {
        public string VehicleInstanceId = string.Empty;
        public string Mass = string.Empty;
        public string Drag = string.Empty;
        public string AngularDrag = string.Empty;
        public string CenterOfMassX = string.Empty;
        public string CenterOfMassY = string.Empty;
        public string CenterOfMassZ = string.Empty;
        public string EnginePower = string.Empty;
        public string MaxSpeed = string.Empty;
        public string BrakeTorque = string.Empty;
        public string FrontSpring = string.Empty;
        public string FrontDamper = string.Empty;
        public string FrontTarget = string.Empty;
        public string FrontSuspensionDistance = string.Empty;
        public string RearSpring = string.Empty;
        public string RearDamper = string.Empty;
        public string RearTarget = string.Empty;
        public string RearSuspensionDistance = string.Empty;
        public string FrontRadius = string.Empty;
        public string RearRadius = string.Empty;
        public string FrontWidth = string.Empty;
        public string RearWidth = string.Empty;

        public void Capture(ActiveVehicleInfo activeVehicle)
        {
            VehicleInstanceId = activeVehicle.VehicleInstanceId;

            if (activeVehicle.Rigidbody != null)
            {
                Mass = Format(activeVehicle.Rigidbody.mass);
                Drag = Format(activeVehicle.Rigidbody.drag);
                AngularDrag = Format(activeVehicle.Rigidbody.angularDrag);
                CenterOfMassX = Format(activeVehicle.Rigidbody.centerOfMass.x);
                CenterOfMassY = Format(activeVehicle.Rigidbody.centerOfMass.y);
                CenterOfMassZ = Format(activeVehicle.Rigidbody.centerOfMass.z);
            }

            if (activeVehicle.VehicleType != null)
            {
                EnginePower = Format(activeVehicle.VehicleType.enginePower);
                MaxSpeed = Format(activeVehicle.VehicleType.maxSpeed);
                BrakeTorque = Format(activeVehicle.VehicleType.brakeForce);
            }

            VehicleWheelClassifier.SplitWheelColliders(activeVehicle.WheelColliders, out var front, out var rear);
            CaptureWheelGroup(front, isFront: true);
            CaptureWheelGroup(rear, isFront: false);
            CaptureWheelControllerDefaults(activeVehicle);
        }

        private void CaptureWheelGroup(System.Collections.Generic.IReadOnlyList<WheelCollider> colliders, bool isFront)
        {
            if (colliders.Count == 0)
                return;

            var radius = colliders.Average(wheel => wheel.radius);
            var distance = colliders.Average(wheel => wheel.suspensionDistance);
            var spring = colliders.Average(wheel => wheel.suspensionSpring.spring);
            var damper = colliders.Average(wheel => wheel.suspensionSpring.damper);
            var target = colliders.Average(wheel => wheel.suspensionSpring.targetPosition);

            if (isFront)
            {
                FrontRadius = Format(radius);
                FrontSuspensionDistance = Format(distance);
                FrontSpring = Format(spring);
                FrontDamper = Format(damper);
                FrontTarget = Format(target);
            }
            else
            {
                RearRadius = Format(radius);
                RearSuspensionDistance = Format(distance);
                RearSpring = Format(spring);
                RearDamper = Format(damper);
                RearTarget = Format(target);
            }
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", InvariantParsing.Culture);
        }

        private void CaptureWheelControllerDefaults(ActiveVehicleInfo activeVehicle)
        {
            var wheelControllers = activeVehicle.MonoBehaviours
                .Where(behaviour => behaviour != null &&
                                    VehicleRuntimeTunerReflection.HasMember(behaviour, "wheel") &&
                                    VehicleRuntimeTunerReflection.HasMember(behaviour, "spring"))
                .ToList();

            VehicleWheelClassifier.SplitWheelControllers(wheelControllers, out var frontControllers, out var rearControllers);
            CaptureWheelControllerGroup(frontControllers, isFront: true);
            CaptureWheelControllerGroup(rearControllers, isFront: false);

            if (string.IsNullOrWhiteSpace(FrontTarget))
                FrontTarget = "n/a";
            if (string.IsNullOrWhiteSpace(RearTarget))
                RearTarget = "n/a";
        }

        private void CaptureWheelControllerGroup(System.Collections.Generic.IEnumerable<MonoBehaviour> wheelControllers, bool isFront)
        {
            var radii = ReadWheelControllerFloatValues(wheelControllers, "wheel", "radius");
            var widths = ReadWheelControllerFloatValues(wheelControllers, "wheel", "width");
            var springMaxForce = ReadWheelControllerFloatValues(wheelControllers, "spring", "maxForce");
            var springMaxLength = ReadWheelControllerFloatValues(wheelControllers, "spring", "maxLength");
            var damperBumpRate = ReadWheelControllerFloatValues(wheelControllers, "damper", "bumpRate");

            if (isFront)
            {
                if (string.IsNullOrWhiteSpace(FrontRadius))
                    FrontRadius = AverageOrEmpty(radii);
                if (string.IsNullOrWhiteSpace(FrontWidth))
                    FrontWidth = AverageOrEmpty(widths);
                if (string.IsNullOrWhiteSpace(FrontSpring))
                    FrontSpring = AverageOrEmpty(springMaxForce);
                if (string.IsNullOrWhiteSpace(FrontSuspensionDistance))
                    FrontSuspensionDistance = AverageOrEmpty(springMaxLength);
                if (string.IsNullOrWhiteSpace(FrontDamper))
                    FrontDamper = AverageOrEmpty(damperBumpRate);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(RearRadius))
                    RearRadius = AverageOrEmpty(radii);
                if (string.IsNullOrWhiteSpace(RearWidth))
                    RearWidth = AverageOrEmpty(widths);
                if (string.IsNullOrWhiteSpace(RearSpring))
                    RearSpring = AverageOrEmpty(springMaxForce);
                if (string.IsNullOrWhiteSpace(RearSuspensionDistance))
                    RearSuspensionDistance = AverageOrEmpty(springMaxLength);
                if (string.IsNullOrWhiteSpace(RearDamper))
                    RearDamper = AverageOrEmpty(damperBumpRate);
            }
        }

        private static float[] ReadWheelControllerFloatValues(
            System.Collections.Generic.IEnumerable<MonoBehaviour> wheelControllers,
            string nestedMemberName,
            string memberName)
        {
            return wheelControllers
                .Select(controller =>
                {
                    if (!VehicleRuntimeTunerReflection.TryGetMemberValue(controller, nestedMemberName, out var nestedStruct) || nestedStruct == null)
                        return (float?)null;

                    var value = VehicleRuntimeTunerReflection.GetMemberValue(nestedStruct, memberName);
                    return value switch
                    {
                        float floatValue => floatValue,
                        double doubleValue => (float)doubleValue,
                        int intValue => intValue,
                        _ => (float?)null
                    };
                })
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
        }

        private static string AverageOrEmpty(float[] values)
        {
            return values.Length == 0 ? string.Empty : Format(values.Average());
        }
    }
}
