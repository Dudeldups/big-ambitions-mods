#nullable enable
using System;
using System.Collections.Generic;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using UnityEngine;
using VehicleRuntimeTuner.Utils;

namespace VehicleRuntimeTuner.Vehicle
{
    public sealed class ActiveVehicleResolver
    {
        private string? lastActiveVehicleId;
        private ActiveVehicleInfo? cachedVehicle;

        public ActiveVehicleInfo? Resolve(bool forceRefresh = false)
        {
            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            if (!forceRefresh &&
                cachedVehicle != null &&
                !string.IsNullOrWhiteSpace(activeVehicleId) &&
                string.Equals(lastActiveVehicleId, activeVehicleId, StringComparison.Ordinal) &&
                cachedVehicle.Root != null)
            {
                return cachedVehicle;
            }

            lastActiveVehicleId = activeVehicleId;
            cachedVehicle = ResolveById(activeVehicleId) ?? ResolveFallbackVehicle();
            return cachedVehicle;
        }

        private static ActiveVehicleInfo? ResolveById(string? vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return null;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return null;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null ||
                    !string.Equals(vehicleController.vehicleInstance.id, vehicleId, StringComparison.Ordinal))
                {
                    continue;
                }

                return CreateInfoFromController(vehicleController, vehicleController.vehicleInstance, vehicleController.vehicleType);
            }

            return null;
        }

        private static ActiveVehicleInfo? ResolveFallbackVehicle()
        {
            var allBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var behaviour in allBehaviours)
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var typeName = type.FullName ?? type.Name;
                if (typeName.IndexOf("VehicleController", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var vehicleInstance = VehicleRuntimeTunerReflection.GetMemberValue(behaviour, "vehicleInstance");
                var vehicleInstanceId = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "id") as string;
                if (string.IsNullOrWhiteSpace(vehicleInstanceId))
                    continue;

                var controlledByPlayer = VehicleRuntimeTunerReflection.TryGetBooleanMemberValue(behaviour, "controlledByPlayer");
                if (controlledByPlayer != true)
                    continue;

                var vehicleType = VehicleRuntimeTunerReflection.GetMemberValue(behaviour, "vehicleType");
                return CreateInfoFromController(behaviour, vehicleInstance, vehicleType);
            }

            return null;
        }

        public static ActiveVehicleInfo CreateInfoFromController(object vehicleController, object? vehicleInstance, object? vehicleType)
        {
            var behaviour = vehicleController as MonoBehaviour;
            var root = behaviour != null ? behaviour.gameObject : null;
            var rigidbody = root != null ? root.GetComponent<Rigidbody>() : null;
            var wheelColliders = root != null ? root.GetComponentsInChildren<WheelCollider>(true) : Array.Empty<WheelCollider>();
            var monoBehaviours = root != null ? root.GetComponentsInChildren<MonoBehaviour>(true) : Array.Empty<MonoBehaviour>();

            return new ActiveVehicleInfo
            {
                VehicleController = vehicleController,
                Root = root,
                VehicleInstance = vehicleInstance,
                VehicleInstanceId = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "id") as string ?? string.Empty,
                VehicleTypeName = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "vehicleTypeName") as string ?? string.Empty,
                Rigidbody = rigidbody,
                VehicleType = vehicleType as Vehicles.VehicleTypes.VehicleType,
                WheelColliders = wheelColliders,
                MonoBehaviours = monoBehaviours
            };
        }
    }
}
