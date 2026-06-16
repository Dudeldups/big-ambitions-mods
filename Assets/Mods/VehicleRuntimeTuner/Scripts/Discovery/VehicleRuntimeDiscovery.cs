#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Discovery
{
    public sealed class VehicleRuntimeDiscovery
    {
        private static readonly string[] RelevantTokens =
        {
            "engine", "power", "torque", "speed", "maxspeed", "brake", "mass", "drag", "angulardrag",
            "centerofmass", "com", "gear", "ratio", "rpm", "wheel", "radius", "width",
            "suspension", "spring", "damper", "damping", "distance", "target", "friction",
            "steer", "steering", "collider", "damage", "body", "chassis"
        };

        public IReadOnlyList<VehicleMemberSnapshot> Capture(ActiveVehicleInfo activeVehicle)
        {
            var snapshots = new List<VehicleMemberSnapshot>();
            if (activeVehicle.Root == null)
                return snapshots;

            AddRigidbodySnapshots(activeVehicle, snapshots);
            AddColliderSnapshots(activeVehicle, snapshots);
            AddWheelColliderSnapshots(activeVehicle, snapshots);
            AddMonoBehaviourSnapshots(activeVehicle, snapshots);

            return snapshots;
        }

        private static void AddRigidbodySnapshots(ActiveVehicleInfo activeVehicle, List<VehicleMemberSnapshot> snapshots)
        {
            if (activeVehicle.Rigidbody == null || activeVehicle.Root == null)
                return;

            var rigidbody = activeVehicle.Rigidbody;
            var basePath = VehicleRuntimeTunerReflection.GetGameObjectPath(activeVehicle.Root.transform);
            snapshots.Add(Create(basePath, nameof(Rigidbody), "mass", rigidbody.mass));
            snapshots.Add(Create(basePath, nameof(Rigidbody), "drag", rigidbody.drag));
            snapshots.Add(Create(basePath, nameof(Rigidbody), "angularDrag", rigidbody.angularDrag));
            snapshots.Add(Create(basePath, nameof(Rigidbody), "centerOfMass", rigidbody.centerOfMass));
        }

        private static void AddWheelColliderSnapshots(ActiveVehicleInfo activeVehicle, List<VehicleMemberSnapshot> snapshots)
        {
            foreach (var wheelCollider in activeVehicle.WheelColliders)
            {
                if (wheelCollider == null)
                    continue;

                var path = VehicleRuntimeTunerReflection.GetGameObjectPath(wheelCollider.transform);
                snapshots.Add(Create(path, nameof(WheelCollider), "radius", wheelCollider.radius));
                snapshots.Add(Create(path, nameof(WheelCollider), "suspensionDistance", wheelCollider.suspensionDistance));
                var spring = wheelCollider.suspensionSpring;
                snapshots.Add(Create(path, nameof(WheelCollider), "suspensionSpring.spring", spring.spring));
                snapshots.Add(Create(path, nameof(WheelCollider), "suspensionSpring.damper", spring.damper));
                snapshots.Add(Create(path, nameof(WheelCollider), "suspensionSpring.targetPosition", spring.targetPosition));
            }
        }

        private static void AddColliderSnapshots(ActiveVehicleInfo activeVehicle, List<VehicleMemberSnapshot> snapshots)
        {
            if (activeVehicle.Root == null)
                return;

            foreach (var collider in activeVehicle.Root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null)
                    continue;

                var path = VehicleRuntimeTunerReflection.GetGameObjectPath(collider.transform);
                snapshots.Add(Create(path, collider.GetType().Name, "enabled", collider.enabled));
                snapshots.Add(Create(path, collider.GetType().Name, "isTrigger", collider.isTrigger));
                snapshots.Add(Create(path, collider.GetType().Name, "localPosition", collider.transform.localPosition));
                snapshots.Add(Create(path, collider.GetType().Name, "bounds.center", collider.bounds.center));
                snapshots.Add(Create(path, collider.GetType().Name, "bounds.size", collider.bounds.size));
            }
        }

        private static void AddMonoBehaviourSnapshots(ActiveVehicleInfo activeVehicle, List<VehicleMemberSnapshot> snapshots)
        {
            foreach (var behaviour in activeVehicle.MonoBehaviours)
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var path = VehicleRuntimeTunerReflection.GetGameObjectPath(behaviour.transform);

                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsRelevant(field.Name))
                        continue;

                    if (!VehicleRuntimeTunerReflection.TryGetFieldValue(behaviour, field, out var value))
                        continue;

                    snapshots.Add(Create(path, type.FullName ?? type.Name, field.Name, value));
                }

                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0 || !IsRelevant(property.Name))
                        continue;

                    if (!VehicleRuntimeTunerReflection.TryGetPropertyValue(behaviour, property, out var value))
                        continue;

                    snapshots.Add(Create(path, type.FullName ?? type.Name, property.Name, value));
                }
            }
        }

        private static bool IsRelevant(string memberName)
        {
            foreach (var token in RelevantTokens)
            {
                if (memberName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static VehicleMemberSnapshot Create(string path, string componentTypeName, string memberName, object? value)
        {
            return new VehicleMemberSnapshot
            {
                ComponentPath = path,
                ComponentTypeName = componentTypeName,
                MemberName = memberName,
                ValueText = VehicleRuntimeTunerReflection.FormatValue(value)
            };
        }
    }
}
