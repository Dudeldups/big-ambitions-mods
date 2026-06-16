#nullable enable
using System.Collections.Generic;
using UnityEngine;
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Applying
{
    public sealed class WheelColliderTuningApplier
    {
        public void Apply(IReadOnlyList<WheelCollider> wheelColliders, VehicleTuningProfile profile)
        {
            if (wheelColliders.Count == 0)
                return;

            VehicleWheelClassifier.SplitWheelColliders(wheelColliders, out var front, out var rear);
            ApplyGroup(front, profile.wheels.frontRadius, profile.suspension.frontSuspensionDistance, profile.suspension.frontSpring, profile.suspension.frontDamper, profile.suspension.frontTargetPosition);
            ApplyGroup(rear, profile.wheels.rearRadius, profile.suspension.rearSuspensionDistance, profile.suspension.rearSpring, profile.suspension.rearDamper, profile.suspension.rearTargetPosition);
        }

        private static void ApplyGroup(
            List<WheelCollider> wheelColliders,
            OptionalFloat radius,
            OptionalFloat suspensionDistance,
            OptionalFloat springValue,
            OptionalFloat damperValue,
            OptionalFloat targetPosition)
        {
            foreach (var wheelCollider in wheelColliders)
            {
                if (radius.hasValue)
                    wheelCollider.radius = radius.value;
                if (suspensionDistance.hasValue)
                    wheelCollider.suspensionDistance = suspensionDistance.value;

                if (springValue.hasValue || damperValue.hasValue || targetPosition.hasValue)
                {
                    var spring = wheelCollider.suspensionSpring;
                    if (springValue.hasValue)
                        spring.spring = springValue.value;
                    if (damperValue.hasValue)
                        spring.damper = damperValue.value;
                    if (targetPosition.hasValue)
                        spring.targetPosition = targetPosition.value;
                    wheelCollider.suspensionSpring = spring;
                }
            }
        }
    }
}
