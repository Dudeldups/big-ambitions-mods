#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Applying
{
    public sealed class ReflectionTuningApplier
    {
        public int LastRuntimeScalarWriteCount { get; private set; }
        public int LastWheelStructWriteCount { get; private set; }

        private static readonly string[] EnginePowerMemberNames =
        {
            "enginePower",
            "maxEnginePower",
            "maxPower",
            "power",
            "motorPower"
        };

        private static readonly string[] MaxSpeedMemberNames =
        {
            "maxSpeed",
            "topSpeed",
            "speedLimit",
            "maxForwardSpeed",
            "maximumSpeed"
        };

        private static readonly string[] BrakeMemberNames =
        {
            "brakeForce",
            "brakeTorque",
            "maxBrakeTorque",
            "maxTorque",
            "handbrakeTorque"
        };

        private static readonly string[] NestedRuntimeObjectMemberNames =
        {
            "engine",
            "motor",
            "brakes",
            "module",
            "drivetrain",
            "powertrain",
            "transmission",
            "settings",
            "vehicleSettings"
        };

        public void Apply(ActiveVehicleInfo activeVehicle, VehicleTuningProfile profile)
        {
            LastRuntimeScalarWriteCount = 0;
            LastWheelStructWriteCount = 0;
            ApplyRuntimeScalars(activeVehicle, profile);

            var wheelControllers = FindWheelControllers(activeVehicle.MonoBehaviours);
            VehicleWheelClassifier.SplitWheelControllers(wheelControllers, out var frontControllers, out var rearControllers);

            ApplyWheelController(frontControllers, profile.wheels.frontRadius, profile.wheels.frontWidth, profile.suspension.frontSpring, profile.suspension.frontDamper, profile.suspension.frontSuspensionDistance);
            ApplyWheelController(rearControllers, profile.wheels.rearRadius, profile.wheels.rearWidth, profile.suspension.rearSpring, profile.suspension.rearDamper, profile.suspension.rearSuspensionDistance);
        }

        private void ApplyRuntimeScalars(ActiveVehicleInfo activeVehicle, VehicleTuningProfile profile)
        {
            var targets = new List<object>();
            if (activeVehicle.VehicleController != null)
                targets.Add(activeVehicle.VehicleController);

            foreach (var behaviour in activeVehicle.MonoBehaviours)
            {
                if (behaviour != null)
                    targets.Add(behaviour);
            }

            ApplyOptionalFloatToTargets(targets, profile.engine.enginePower, EnginePowerMemberNames);
            ApplyOptionalFloatToTargets(targets, profile.engine.maxSpeed, MaxSpeedMemberNames);
            ApplyOptionalFloatToTargets(targets, profile.brakes.brakeTorque, BrakeMemberNames);
        }

        private static List<MonoBehaviour> FindWheelControllers(IReadOnlyList<MonoBehaviour> behaviours)
        {
            var list = new List<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                if (VehicleRuntimeTunerReflection.HasMember(behaviour, "wheel") &&
                    VehicleRuntimeTunerReflection.HasMember(behaviour, "spring"))
                {
                    list.Add(behaviour);
                }
            }

            return list;
        }

        private void ApplyWheelController(
            IEnumerable<MonoBehaviour> wheelControllers,
            OptionalFloat radius,
            OptionalFloat width,
            OptionalFloat springForce,
            OptionalFloat damperRate,
            OptionalFloat springLength)
        {
            foreach (var wheelController in wheelControllers)
            {
                if (!VehicleRuntimeTunerReflection.TryGetMemberValue(wheelController, "wheel", out var wheelStruct) || wheelStruct == null)
                    continue;

                var changed = false;
                if (radius.hasValue)
                    changed |= VehicleRuntimeTunerReflection.TrySetMemberValue(wheelStruct, "radius", radius.value);
                if (width.hasValue)
                    changed |= VehicleRuntimeTunerReflection.TrySetMemberValue(wheelStruct, "width", width.value);

                if (changed)
                {
                    VehicleRuntimeTunerReflection.TrySetMemberValue(wheelController, "wheel", wheelStruct);
                    LastWheelStructWriteCount++;
                }

                if (VehicleRuntimeTunerReflection.TryGetMemberValue(wheelController, "spring", out var springStruct) && springStruct != null)
                {
                    var springChanged = false;
                    if (springForce.hasValue)
                        springChanged |= VehicleRuntimeTunerReflection.TrySetMemberValue(springStruct, "maxForce", springForce.value);
                    if (springLength.hasValue)
                        springChanged |= VehicleRuntimeTunerReflection.TrySetMemberValue(springStruct, "maxLength", springLength.value);

                    if (springChanged)
                    {
                        VehicleRuntimeTunerReflection.TrySetMemberValue(wheelController, "spring", springStruct);
                        LastWheelStructWriteCount++;
                    }
                }

                if (VehicleRuntimeTunerReflection.TryGetMemberValue(wheelController, "damper", out var damperStruct) && damperStruct != null)
                {
                    var damperChanged = false;
                    if (damperRate.hasValue)
                    {
                        damperChanged |= VehicleRuntimeTunerReflection.TrySetMemberValue(damperStruct, "bumpRate", damperRate.value);
                        damperChanged |= VehicleRuntimeTunerReflection.TrySetMemberValue(damperStruct, "reboundRate", damperRate.value);
                    }

                    if (damperChanged)
                    {
                        VehicleRuntimeTunerReflection.TrySetMemberValue(wheelController, "damper", damperStruct);
                        LastWheelStructWriteCount++;
                    }
                }
            }
        }

        private void ApplyOptionalFloatToTargets(IEnumerable<object> targets, OptionalFloat value, string[] memberNames)
        {
            if (!value.hasValue)
                return;

            var visited = new HashSet<object>();
            foreach (var target in targets)
                ApplyOptionalFloatToTargetGraph(target, value.value, memberNames, visited, 0);
        }

        private void ApplyOptionalFloatToTargetGraph(
            object? target,
            float value,
            string[] memberNames,
            ISet<object> visited,
            int depth)
        {
            if (target == null || depth > 1 || visited.Contains(target))
                return;

            visited.Add(target);

            foreach (var memberName in memberNames)
            {
                if (VehicleRuntimeTunerReflection.TrySetMemberValue(target, memberName, value))
                    LastRuntimeScalarWriteCount++;
            }

            foreach (var nestedName in NestedRuntimeObjectMemberNames)
            {
                if (!VehicleRuntimeTunerReflection.TryGetMemberValue(target, nestedName, out var nestedTarget) || nestedTarget == null)
                    continue;

                var nestedType = nestedTarget.GetType();
                if (nestedType.IsPrimitive || nestedTarget is string || nestedType.IsEnum)
                    continue;

                ApplyOptionalFloatToTargetGraph(nestedTarget, value, memberNames, visited, depth + 1);
            }
        }
    }
}
